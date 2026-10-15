using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Authoritative.Extension;
using Ninlil.Dns.Server.Authoritative.Resolution;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Dnssec;
using Ninlil.Dns.Server.Dnssec.Extension;
using Ninlil.Dns.Server.Tsig.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Resolution;

/// <summary>
///     This enum picks how <see cref="AuthoritativeTestZoneHost.AddZoneAsync" /> signs a test
///     zone.
/// </summary>
public enum TestZoneSigningKind
{
    /// <summary>The zone is not DNSSEC signed.</summary>
    None,

    /// <summary>The zone is signed and uses an NSEC chain (RFC 4034 §4).</summary>
    Nsec,

    /// <summary>The zone is signed and uses an NSEC3 chain (RFC 5155).</summary>
    Nsec3,
}

/// <summary>
///     This record describes how a test zone is signed: the kind and, for NSEC3, the hash
///     parameters stored on the zone.
/// </summary>
/// <param name="Kind">The kind of signing.</param>
/// <param name="Nsec3Iterations">The NSEC3 iteration count; ignored unless the kind is NSEC3.</param>
/// <param name="Nsec3SaltHex">
///     The NSEC3 salt as hex, or <see langword="null" /> for none; ignored unless the kind is
///     NSEC3.
/// </param>
public sealed record TestZoneSigning(
    TestZoneSigningKind Kind,
    ushort Nsec3Iterations = 0,
    string Nsec3SaltHex = null
)
{
    /// <summary>This value leaves the zone unsigned.</summary>
    public static TestZoneSigning None { get; } = new(TestZoneSigningKind.None);

    /// <summary>This value signs the zone with an NSEC chain.</summary>
    public static TestZoneSigning Nsec { get; } = new(TestZoneSigningKind.Nsec);

    /// <summary>
    ///     This method signs the zone with an NSEC3 chain.
    /// </summary>
    /// <param name="iterations">The NSEC3 iteration count.</param>
    /// <param name="saltHex">The NSEC3 salt as hex, or <see langword="null" /> for none.</param>
    /// <returns>The signing description.</returns>
    public static TestZoneSigning Nsec3(ushort iterations = 0, string saltHex = null) =>
        new(TestZoneSigningKind.Nsec3, iterations, saltHex);
}

/// <summary>
///     This record is one record to seed into a test zone: the owner (a full name, not relative to
///     the zone), the type, the typed data and the TTL.
/// </summary>
/// <param name="Owner">The owner name, for example <c>www.example.test</c>.</param>
/// <param name="Type">The record type; it must match the type of <paramref name="Data" />.</param>
/// <param name="Data">The typed record data, stored as JSON the way the API stores it.</param>
/// <param name="TimeToLiveSeconds">The record TTL in seconds.</param>
public sealed record TestRecord(
    string Owner,
    DnsResourceRecordTypeEnum Type,
    IDnsRecordData Data,
    uint TimeToLiveSeconds = 300
)
{
    /// <summary>
    ///     This method builds an NS record.
    /// </summary>
    /// <param name="owner">The delegation point, or the apex.</param>
    /// <param name="nameServer">The name server host name.</param>
    /// <returns>The record.</returns>
    public static TestRecord Ns(string owner, string nameServer) =>
        new(owner, DnsResourceRecordTypeEnum.NS, new NsRecordData(DnsName.Parse(nameServer)));

    /// <summary>
    ///     This method builds an A record.
    /// </summary>
    /// <param name="owner">The owner name.</param>
    /// <param name="address">The IPv4 address in text form.</param>
    /// <returns>The record.</returns>
    public static TestRecord A(string owner, string address) =>
        new(owner, DnsResourceRecordTypeEnum.A, new ARecordData(IPAddress.Parse(address)));

    /// <summary>
    ///     This method builds a CNAME record.
    /// </summary>
    /// <param name="owner">The alias name.</param>
    /// <param name="target">The canonical name.</param>
    /// <returns>The record.</returns>
    public static TestRecord Cname(string owner, string target) =>
        new(owner, DnsResourceRecordTypeEnum.Cname, new CnameRecordData(DnsName.Parse(target)));
}

/// <summary>
///     This class is the shared harness for tests that query
///     <see cref="AuthoritativeQueryResolver" /> against real zones. It builds a SQLite host the
///     way the production host wires it (EF Core storage, the DNS core, TSIG, DNSSEC and the
///     authoritative server), seeds zones and records through the repositories, starts the
///     hosted caches and then resolves queries in process, with no sockets.
///     <para>
///         Typical use: create the host, call <see cref="AddZoneAsync" /> for each zone, call
///         <see cref="StartAsync" />, then call <see cref="QueryAsync" />. Zones are added
///         before <see cref="StartAsync" /> because the caches load the database when they start.
///         A signed zone gets an ED25519 KSK and ZSK, and
///         <c>SignedZoneCache</c> signs it at start. Each instance owns one temporary database
///         file, which <see cref="DisposeAsync" /> deletes. Use one instance per test class (or per
///         test when the tests change the zone).
///     </para>
/// </summary>
public sealed class AuthoritativeTestZoneHost : IAsyncDisposable
{
    /// <summary>
    ///     This field holds the temporary SQLite file of this host.
    /// </summary>
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-authoritative-host-tests-{Guid.NewGuid()}.sqlite3"
    );

    /// <summary>
    ///     This field holds the built host.
    /// </summary>
    private readonly IHost _host;

    /// <summary>
    ///     This field is the next DNS message ID to use, so queries differ.
    /// </summary>
    private ushort _nextMessageId = 1;

    /// <summary>
    ///     This constructor builds the host. Use <see cref="CreateAsync" />, which also creates
    ///     the schema.
    /// </summary>
    /// <param name="loggerProvider">
    ///     A logger provider added to the host so a test can read log output, or
    ///     <see langword="null" /> for none.
    /// </param>
    private AuthoritativeTestZoneHost(ILoggerProvider loggerProvider)
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        if (loggerProvider is not null)
            builder.Logging.AddProvider(loggerProvider);
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);
        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.AddNinlilDnsServerTsig(builder.Configuration);
        builder.Services.AddNinlilDnsServerDnssec();
        builder.Services.AddNinlilDnsServerAuthoritative(builder.Configuration);

        _host = builder.Build();
    }

    /// <summary>
    ///     This property exposes the host's services for tests that need a cache or a service
    ///     directly.
    /// </summary>
    public IServiceProvider Services => _host.Services;

    /// <summary>
    ///     This method builds a host and creates its database schema.
    /// </summary>
    /// <param name="loggerProvider">
    ///     A logger provider added to the host so a test can read log output, or
    ///     <see langword="null" /> for none.
    /// </param>
    /// <returns>The host, not yet started.</returns>
    public static async Task<AuthoritativeTestZoneHost> CreateAsync(
        ILoggerProvider loggerProvider = null
    )
    {
        AuthoritativeTestZoneHost testHost = new(loggerProvider);

        await using AsyncServiceScope scope = testHost._host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();

        return testHost;
    }

    /// <summary>
    ///     This method adds a primary zone with its records. It adds an apex SOA record when
    ///     <paramref name="records" /> holds none, and for a signed zone it generates an ED25519
    ///     KSK and ZSK. Call it before <see cref="StartAsync" />.
    /// </summary>
    /// <param name="name">The zone name.</param>
    /// <param name="records">The records to seed, with full owner names.</param>
    /// <param name="signing">How to sign the zone; <see langword="null" /> means unsigned.</param>
    /// <param name="generateKeys">
    ///     Whether to generate the ED25519 KSK and ZSK of a signed zone. A test that stores its
    ///     own keys passes <see langword="false" />.
    /// </param>
    /// <returns>The identifier of the new zone.</returns>
    public async Task<Guid> AddZoneAsync(
        string name,
        IEnumerable<TestRecord> records,
        TestZoneSigning signing = null,
        bool generateKeys = true
    )
    {
        signing ??= TestZoneSigning.None;
        List<TestRecord> allRecords = [.. records];

        await using AsyncServiceScope scope = _host.Services.CreateAsyncScope();
        IZoneRepository zoneRepository =
            scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IRecordRepository recordRepository =
            scope.ServiceProvider.GetRequiredService<IRecordRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        bool isSigned = signing.Kind != TestZoneSigningKind.None;
        Guid zoneId = Guid.NewGuid();

        await zoneRepository.AddAsync(
            new ZoneEntity
            {
                Id = zoneId,
                Name = name,
                Type = ZoneTypeEnum.Primary,
                IsDnssecSigned = isSigned,
                DnssecAlgorithm = DnssecAlgorithmEnum.Ed25519,
                DenialOfExistenceMode =
                    signing.Kind == TestZoneSigningKind.Nsec3
                        ? DnssecDenialOfExistenceModeEnum.Nsec3
                        : DnssecDenialOfExistenceModeEnum.Nsec,
                Nsec3Iterations = signing.Nsec3Iterations,
                Nsec3SaltHex = signing.Nsec3SaltHex,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );
        await unitOfWork.SaveChangesAsync();

        // The resolver needs a SOA at the apex for negative answers.
        if (
            !allRecords.Exists(record =>
                record.Type == DnsResourceRecordTypeEnum.Soa
                && DnsName.Parse(record.Owner) == DnsName.Parse(name)
            )
        )
            allRecords.Insert(
                0,
                new TestRecord(
                    name,
                    DnsResourceRecordTypeEnum.Soa,
                    new SoaRecordData(
                        DnsName.Parse($"ns1.{name}"),
                        DnsName.Parse($"admin.{name}"),
                        1,
                        3600,
                        900,
                        604800,
                        300
                    ),
                    3600
                )
            );

        foreach (TestRecord record in allRecords)
            await recordRepository.AddAsync(
                new RecordEntity
                {
                    Id = Guid.NewGuid(),
                    ZoneId = zoneId,
                    Name = record.Owner,
                    Type = record.Type,
                    Class = DnsClassEnum.Internet,
                    TimeToLiveSeconds = record.TimeToLiveSeconds,
                    DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                        record.Data.GetType(),
                        record.Data
                    ),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await unitOfWork.SaveChangesAsync();

        if (isSigned && generateKeys)
        {
            IDnssecKeyManagementService keyManagement =
                scope.ServiceProvider.GetRequiredService<IDnssecKeyManagementService>();
            await keyManagement.GenerateKeyAsync(
                zoneId,
                DnssecKeyRoleEnum.Ksk,
                DnssecAlgorithmEnum.Ed25519
            );
            await keyManagement.GenerateKeyAsync(
                zoneId,
                DnssecKeyRoleEnum.Zsk,
                DnssecAlgorithmEnum.Ed25519
            );
        }

        return zoneId;
    }

    /// <summary>
    ///     This method starts the host, which loads the zones into <c>AuthoritativeZoneCache</c>
    ///     and signs the signed ones into <c>SignedZoneCache</c>.
    /// </summary>
    public Task StartAsync() => _host.StartAsync();

    /// <summary>
    ///     This method resolves one query through <see cref="AuthoritativeQueryResolver" />, as a
    ///     UDP query from the loopback address (recursion not desired).
    /// </summary>
    /// <param name="name">The query name.</param>
    /// <param name="type">The query type.</param>
    /// <param name="dnssecOk">Whether to set the EDNS DO bit.</param>
    /// <returns>
    ///     The response message. The call fails the test when the resolver does not handle the
    ///     query.
    /// </returns>
    public async Task<DnsMessage> QueryAsync(
        string name,
        DnsResourceRecordTypeEnum type,
        bool dnssecOk = false
    )
    {
        AuthoritativeQueryResolver resolver = _host
            .Services.GetServices<IDnsQueryResolver>()
            .OfType<AuthoritativeQueryResolver>()
            .Single();

        DnsMessage request = new()
        {
            Id = _nextMessageId++,
            Opcode = DnsOpcodeEnum.Query,
            RecursionDesired = false,
            Questions = { new DnsQuestion(DnsName.Parse(name), type, DnsClassEnum.Internet) },
            Edns = dnssecOk ? new DnsEdnsOptions(0, 4096, true, []) : null,
        };

        DnsQueryResolution resolution = await resolver.TryResolveAsync(
            new DnsQueryContext(
                request,
                DnsTransportProtocolEnum.Udp,
                IPAddress.Loopback,
                DateTimeOffset.UtcNow
            )
        );

        Assert.True(resolution.IsHandled, $"The resolver did not handle {name} {type}.");

        return resolution.Response;
    }

    /// <summary>
    ///     This method stops and disposes the host and deletes the temporary database.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}
