using System.Net;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Authoritative.Resolution;
using Ninlil.Dns.Server.Authoritative.Tests.Resolution;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;
using Ninlil.Dns.Server.Tsig;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class checks that a signed secondary zone answers with denial proofs built from the
///     NSEC3 chain it received, not from its own <see cref="ZoneEntity" /> row. The primary signs
///     with NSEC3, 5 iterations and the salt <c>BEEF</c>; the secondary's row keeps the defaults
///     (NSEC, 0 iterations, no salt), as a secondary's row does. Two <see cref="AuthoritativeTestZoneHost" />
///     instances stand in for two servers: a real TSIG-signed AXFR is pulled from the primary and
///     written to the secondary with <see cref="IZoneUpdateService.BootstrapZoneAsync" />, the
///     call the refresh service makes on a first pull. The proofs are then checked with
///     <see cref="DenialOfExistenceValidator" />.
/// </summary>
public sealed class SecondaryZoneNsec3ProofTests : IAsyncLifetime
{
    private const string ZoneName = "nsec3-secondary.test";

    private readonly Guid _secondaryZoneId = Guid.NewGuid();
    private readonly string _keyName = $"nsec3-secondary-key-{Guid.NewGuid()}";

    private AuthoritativeTestZoneHost _primary;
    private AuthoritativeTestZoneHost _secondary;
    private Guid _primaryZoneId;

    /// <summary>
    ///     This method signs the primary, starts it, pulls its AXFR and loads that into the
    ///     secondary, then starts the secondary and waits for its signed data.
    /// </summary>
    public async Task InitializeAsync()
    {
        _primary = await AuthoritativeTestZoneHost.CreateAsync();
        _primaryZoneId = await _primary.AddZoneAsync(
            ZoneName,
            [TestRecord.A($"www.{ZoneName}", "192.0.2.20")],
            TestZoneSigning.Nsec3(5, "BEEF")
        );
        await AllowTransferAsync();
        await _primary.StartAsync();

        // The secondary row is created before its host starts, so the caches know the zone; the
        // transferred rows arrive after the start, as they would on a first pull.
        _secondary = await AuthoritativeTestZoneHost.CreateAsync();
        await using (AsyncServiceScope scope = _secondary.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneRepository>()
                .AddAsync(
                    new ZoneEntity
                    {
                        Id = _secondaryZoneId,
                        Name = ZoneName,
                        Type = ZoneTypeEnum.Secondary,
                        IsDnssecSigned = true,
                        DenialOfExistenceMode = DnssecDenialOfExistenceModeEnum.Nsec,
                        CreatedAtUtc = DateTimeOffset.UtcNow,
                    }
                );
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }

        await _secondary.StartAsync();

        List<DnsResourceRecord> transferred = await PullAxfrFromPrimaryAsync();
        await using (AsyncServiceScope scope = _secondary.Services.CreateAsyncScope())
        {
            await scope
                .ServiceProvider.GetRequiredService<IZoneUpdateService>()
                .BootstrapZoneAsync(
                    _secondaryZoneId,
                    [
                        .. transferred
                            .Select(record =>
                                RecordEntityCodec.FromWireRecord(record, _secondaryZoneId)
                            )
                            .Where(record => record is not null),
                    ]
                );
        }

        // The zone-changed event reaches the signed-zone cache through a background channel, so
        // the cache fills a moment after BootstrapZoneAsync returns.
        SignedZoneCache cache = _secondary.Services.GetRequiredService<SignedZoneCache>();

        for (
            int attempt = 0;
            attempt < 100 && cache.GetSignedData(_secondaryZoneId) is null;
            attempt++
        )
            await Task.Delay(100);

        Assert.NotNull(cache.GetSignedData(_secondaryZoneId));
    }

    /// <summary>
    ///     This method stops and disposes both hosts.
    /// </summary>
    public async Task DisposeAsync()
    {
        await _primary.DisposeAsync();
        await _secondary.DisposeAsync();
    }

    [Fact]
    public async Task NxDomain_AtASecondaryWithDifferentLocalSettings_IsProvenWithThePrimarysNsec3Chain()
    {
        DnsMessage response = await _secondary.QueryAsync(
            $"nothere.{ZoneName}",
            DnsResourceRecordTypeEnum.A,
            dnssecOk: true
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
        List<DnsResourceRecord> nsec3 =
        [
            .. response.AuthorityRecords.Where(record =>
                record.Type == DnsResourceRecordTypeEnum.Nsec3
            ),
        ];
        Assert.NotEmpty(nsec3);
        Assert.Equal(
            DenialOfExistenceOutcomeEnum.Proven,
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse($"nothere.{ZoneName}"),
                DnsName.Parse(ZoneName),
                nsec3
            )
        );
    }

    [Fact]
    public async Task NoData_AtASecondaryWithDifferentLocalSettings_IsProvenWithThePrimarysNsec3Chain()
    {
        DnsMessage response = await _secondary.QueryAsync(
            $"www.{ZoneName}",
            DnsResourceRecordTypeEnum.MX,
            dnssecOk: true
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Empty(response.Answers);
        List<DnsResourceRecord> nsec3 =
        [
            .. response.AuthorityRecords.Where(record =>
                record.Type == DnsResourceRecordTypeEnum.Nsec3
            ),
        ];
        Assert.NotEmpty(nsec3);
        Assert.Equal(
            DenialOfExistenceOutcomeEnum.Proven,
            DenialOfExistenceValidator.VerifyNoDataNsec3(
                DnsName.Parse($"www.{ZoneName}"),
                DnsResourceRecordTypeEnum.MX,
                DnsName.Parse(ZoneName),
                nsec3
            )
        );
    }

    /// <summary>
    ///     This method adds a TSIG key to the primary and binds it to the zone, so the AXFR pull
    ///     can authenticate (a transfer needs a key bound in
    ///     <see cref="ZoneEntity.TransferTsigKeyNames" />).
    /// </summary>
    private async Task AllowTransferAsync()
    {
        await using AsyncServiceScope scope = _primary.Services.CreateAsyncScope();
        IZoneRepository zones = scope.ServiceProvider.GetRequiredService<IZoneRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        ZoneEntity zone = await zones.FindByNameAsync(ZoneName);
        zone.TransferTsigKeyNames = [_keyName];

        IDataProtector protector = scope
            .ServiceProvider.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("Ninlil.Dns.Server.Tsig.SharedSecret");
        byte[] rawSecret = new byte[32];
        Random.Shared.NextBytes(rawSecret);

        await scope
            .ServiceProvider.GetRequiredService<ITsigKeyRepository>()
            .AddAsync(
                new TsigKeyEntity
                {
                    Id = Guid.NewGuid(),
                    Name = _keyName,
                    AlgorithmName = "hmac-sha256.",
                    SecretEncrypted = protector.Protect(Convert.ToBase64String(rawSecret)),
                    IsDisabled = false,
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                }
            );
        await unitOfWork.SaveChangesAsync();
    }

    /// <summary>
    ///     This method performs a TSIG-signed AXFR against the primary and returns every wire
    ///     record of the transfer.
    /// </summary>
    private async Task<List<DnsResourceRecord>> PullAxfrFromPrimaryAsync()
    {
        await using AsyncServiceScope scope = _primary.Services.CreateAsyncScope();
        ITsigService tsigService = scope.ServiceProvider.GetRequiredService<ITsigService>();
        IZoneTransferService transferService =
            scope.ServiceProvider.GetRequiredService<IZoneTransferService>();

        DnsMessage request = new()
        {
            Id = 1,
            Opcode = DnsOpcodeEnum.Query,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(ZoneName),
                    DnsResourceRecordTypeEnum.Axfr,
                    DnsClassEnum.Internet
                ),
            },
        };

        DnsMessage signed = await tsigService.SignAsync(request, _keyName, request.Id);
        byte[] rawMessage = DnsMessageWriter.Write(signed);
        DnsMessage reparsed = DnsMessageReader.Read(rawMessage, out int tsigOffset);

        DnsQueryContext context = new(
            reparsed,
            DnsTransportProtocolEnum.Tcp,
            IPAddress.Loopback,
            DateTimeOffset.UtcNow,
            rawMessage,
            tsigOffset
        );

        List<DnsResourceRecord> records = [];

        await foreach (DnsMessage message in transferService.CreateAxfrResponseAsync(context))
        {
            Assert.Equal(DnsResponseCodeEnum.NoError, message.ResponseCode);
            records.AddRange(message.Answers);
        }

        // A signed transfer must carry the NSEC3 chain; otherwise a failing proof below would
        // not say whether the transfer or the proof builder is at fault.
        Assert.Contains(records, record => record.Type == DnsResourceRecordTypeEnum.Nsec3);

        return records;
    }
}
