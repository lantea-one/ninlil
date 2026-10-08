using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Tsig;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Transfer;

/// <summary>
///     This class is a test host with a temporary SQLite database, the real TSIG service, the real
///     zone cache and the real <see cref="ZoneTransferService" />. It hosts one primary zone,
///     <see cref="ZoneName" />, with an SOA and five A records, and lets a test add TSIG keys, sign
///     requests and run a transfer the way <see cref="ZoneTransferRequestRunner" /> does (a TCP
///     context carrying the raw request bytes). It serves the TSIG transfer tests and the signed
///     SOA and stub NS client tests.
/// </summary>
public sealed class TsigTransferHost : IAsyncDisposable
{
    /// <summary>
    ///     This constant contains the name of the one hosted zone.
    /// </summary>
    public const string ZoneName = "xfer-tsig.example.com";

    /// <summary>
    ///     This constant contains the name of a second hosted zone whose key lists are empty, so
    ///     no key may transfer it.
    /// </summary>
    public const string UnboundZoneName = "xfer-unbound.example.com";

    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-tsig-transfer-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;

    /// <summary>
    ///     This method builds the host, seeds the zone and starts the host so the zone cache loads.
    /// </summary>
    /// <param name="recordsPerMessage">The records-per-message setting of the transfer service.</param>
    public static async Task<TsigTransferHost> CreateAsync(int recordsPerMessage = 2)
    {
        TsigTransferHost result = new();
        await result.StartAsync(recordsPerMessage);
        return result;
    }

    /// <summary>
    ///     This property contains the host's service provider.
    /// </summary>
    public IServiceProvider Services => _host.Services;

    private async Task StartAsync(int recordsPerMessage)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Configuration["DnsServer:Authoritative:ZoneTransfer:RecordsPerMessage"] =
            recordsPerMessage.ToString();
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);
        builder.Services.AddNinlilDnsServerDnssec();
        builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);

        _host = builder.Build();

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        Guid zoneId = Guid.NewGuid();
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = ZoneName,
                Type = ZoneTypeEnum.Primary,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        await recordRepository.AddAsync(
            NewRecord(
                zoneId,
                ZoneName,
                DnsResourceRecordTypeEnum.Soa,
                typeof(SoaRecordData),
                new SoaRecordData(
                    DnsName.Parse($"ns1.{ZoneName}"),
                    DnsName.Parse($"admin.{ZoneName}"),
                    100,
                    3600,
                    900,
                    604800,
                    3600
                )
            )
        );

        for (byte i = 1; i <= 5; i++)
            await recordRepository.AddAsync(
                NewRecord(
                    zoneId,
                    $"host{i}.{ZoneName}",
                    DnsResourceRecordTypeEnum.A,
                    typeof(ARecordData),
                    new ARecordData(new IPAddress([192, 0, 2, i]))
                )
            );

        Guid unboundZoneId = Guid.NewGuid();
        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = unboundZoneId,
                Name = UnboundZoneName,
                Type = ZoneTypeEnum.Primary,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await recordRepository.AddAsync(
            NewRecord(
                unboundZoneId,
                UnboundZoneName,
                DnsResourceRecordTypeEnum.Soa,
                typeof(SoaRecordData),
                new SoaRecordData(
                    DnsName.Parse($"ns1.{UnboundZoneName}"),
                    DnsName.Parse($"admin.{UnboundZoneName}"),
                    1,
                    3600,
                    900,
                    604800,
                    3600
                )
            )
        );

        await unitOfWork.SaveChangesAsync();
        await _host.StartAsync();
    }

    private static RecordEntity NewRecord(
        Guid zoneId,
        string name,
        DnsResourceRecordTypeEnum type,
        Type dataType,
        object data
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = zoneId,
            Name = name,
            Type = type,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = 3600,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(dataType, data),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

    /// <summary>
    ///     This method stores a new enabled hmac-sha256 key and returns its name. Names are unique
    ///     per call, so tests do not collide.
    /// </summary>
    /// <param name="isClusterKey">Whether the key belongs to a cluster peer (TSIG refuses those).</param>
    /// <param name="bindTransfer">
    ///     Whether to list the key in the zone's <see cref="ZoneEntity.TransferTsigKeyNames" />.
    ///     A key that is not listed verifies but may not transfer the zone.
    /// </param>
    public async Task<string> AddKeyAsync(bool isClusterKey = false, bool bindTransfer = true)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        ITsigKeyRepository keyRepository =
            scope.ServiceProvider.GetRequiredService<ITsigKeyRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        IDataProtector protector = scope
            .ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Ninlil.Dns.Server.Tsig.SharedSecret");

        byte[] rawSecret = new byte[32];
        Random.Shared.NextBytes(rawSecret);
        string name = $"xfer-key-{Guid.NewGuid()}";

        await keyRepository.AddAsync(
            new TsigKeyEntity
            {
                Id = Guid.NewGuid(),
                Name = name,
                AlgorithmName = "hmac-sha256",
                SecretEncrypted = protector.Protect(Convert.ToBase64String(rawSecret)),
                IsDisabled = false,
                ClusterNodeId = isClusterKey ? Guid.NewGuid() : null,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        if (bindTransfer)
        {
            IZoneRepository zoneRepository =
                scope.ServiceProvider.GetRequiredService<IZoneRepository>();
            ZoneEntity zone = await zoneRepository.FindByNameAsync(ZoneName);
            zone.TransferTsigKeyNames = [.. zone.TransferTsigKeyNames, name];
            zoneRepository.Update(zone);
            await unitOfWork.SaveChangesAsync();
            await _host
                .Services.GetRequiredService<AuthoritativeZoneCache>()
                .ReloadZoneAsync(zone.Id);
        }

        return name;
    }

    /// <summary>
    ///     This method builds a query for <paramref name="zoneName" />, signs it with
    ///     <paramref name="keyName" /> and returns it as a receiver would see it: the decoded
    ///     message (carrying its TSIG), the bytes and the TSIG record's offset.
    /// </summary>
    public async Task<(DnsMessage Request, byte[] Raw, int TsigOffset)> SignedQueryAsync(
        string zoneName,
        DnsResourceRecordTypeEnum type,
        string keyName
    )
    {
        DnsMessage query = new()
        {
            Id = (ushort)Random.Shared.Next(1, ushort.MaxValue),
            Opcode = DnsOpcodeEnum.Query,
            Questions = { new DnsQuestion(DnsName.Parse(zoneName), type, DnsClassEnum.Internet) },
        };

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();
        DnsMessage signed = await tsigService.SignAsync(query, keyName, query.Id);
        byte[] raw = DnsMessageWriter.Write(signed);
        DnsMessage request = DnsMessageReader.Read(raw, out int tsigOffset);

        return (request, raw, tsigOffset);
    }

    /// <summary>
    ///     This method runs an AXFR for the request through <see cref="IZoneTransferService" />
    ///     over a TCP context and returns every message it yields.
    /// </summary>
    public async Task<List<DnsMessage>> TransferAsync(
        DnsMessage request,
        byte[] raw,
        int tsigOffset
    )
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneTransferService service =
            scope.ServiceProvider.GetRequiredService<IZoneTransferService>();
        DnsQueryContext context = new(
            request,
            DnsTransportProtocolEnum.Tcp,
            IPAddress.Loopback,
            DateTimeOffset.UtcNow,
            raw,
            tsigOffset
        );

        List<DnsMessage> messages = [];
        await foreach (DnsMessage message in service.CreateAxfrResponseAsync(context))
            messages.Add(message);

        return messages;
    }

    /// <summary>
    ///     This method runs <paramref name="action" /> with the TSIG service from a fresh scope.
    /// </summary>
    public async Task<T> WithTsigAsync<T>(Func<ITsigService, Task<T>> action)
    {
        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ITsigService>());
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}

/// <summary>
///     This class shares one <see cref="TsigTransferHost" /> between the tests of a class, so the
///     database and the host start once.
/// </summary>
public sealed class TsigTransferFixture : IAsyncLifetime
{
    /// <summary>
    ///     This property contains the shared host.
    /// </summary>
    public TsigTransferHost Host { get; private set; }

    /// <inheritdoc />
    public async Task InitializeAsync() => Host = await TsigTransferHost.CreateAsync();

    /// <inheritdoc />
    public async Task DisposeAsync() => await Host.DisposeAsync();
}
