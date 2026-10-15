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
using Ninlil.Dns.Server.Authoritative.Tests.Resolution;
using Ninlil.Dns.Server.Authoritative.Transfer;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Recursive.Validation;
using Ninlil.Dns.Server.Tsig;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class checks that the hashed owner names of a transferred NSEC3 chain do not exist at a
///     signed secondary (RFC 5155 §7.2.8): a query for one answers NXDOMAIN with the NSEC3 proof, as
///     the primary does, while the secondary's own AXFR still carries the whole chain. It uses the
///     two-host setup of <see cref="SecondaryZoneNsec3ProofTests" />: the primary signs with NSEC3
///     (2 iterations, salt <c>AB</c>), a real TSIG-signed AXFR is pulled from it and written to the
///     secondary with <see cref="IZoneUpdateService.BootstrapZoneAsync" />. Both hosts hold a
///     transfer key, so the test can pull an AXFR from either.
/// </summary>
public sealed class SecondaryZoneNsec3OwnerNameTests : IAsyncLifetime
{
    private const string ZoneName = "nsec3-owners.test";

    private readonly Guid _secondaryZoneId = Guid.NewGuid();
    private readonly string _keyName = $"nsec3-owners-key-{Guid.NewGuid()}";

    private AuthoritativeTestZoneHost _primary;
    private AuthoritativeTestZoneHost _secondary;
    private string _hashedOwner;

    /// <summary>
    ///     This method signs and starts the primary, pulls its AXFR and loads that into the
    ///     secondary, then waits for the secondary's signed data.
    /// </summary>
    public async Task InitializeAsync()
    {
        _primary = await AuthoritativeTestZoneHost.CreateAsync();
        await _primary.AddZoneAsync(
            ZoneName,
            [
                TestRecord.A($"www.{ZoneName}", "192.0.2.20"),
                // sub.<zone> has no record of its own: an empty non-terminal.
                TestRecord.A($"host.sub.{ZoneName}", "192.0.2.21"),
            ],
            TestZoneSigning.Nsec3(2, "AB")
        );
        await AllowTransferAsync(_primary);
        await _primary.StartAsync();

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

        await AllowTransferAsync(_secondary);
        await _secondary.StartAsync();

        List<DnsResourceRecord> transferred = await PullAxfrAsync(_primary);
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

        // The zone-changed event reaches the signed-zone cache through a background channel.
        SignedZoneCache cache = _secondary.Services.GetRequiredService<SignedZoneCache>();

        for (
            int attempt = 0;
            attempt < 100 && cache.GetSignedData(_secondaryZoneId) is null;
            attempt++
        )
            await Task.Delay(100);

        Assert.NotNull(cache.GetSignedData(_secondaryZoneId));

        _hashedOwner = transferred
            .First(record => record.Type == DnsResourceRecordTypeEnum.Nsec3)
            .Name.ToString();
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
    public async Task A_ForAHashedOwnerNameAtASecondary_IsNxDomainWithAProvenNsec3Proof()
    {
        DnsMessage response = await _secondary.QueryAsync(
            _hashedOwner,
            DnsResourceRecordTypeEnum.A,
            dnssecOk: true
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
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
            DenialOfExistenceValidator.VerifyNxDomainNsec3(
                DnsName.Parse(_hashedOwner),
                DnsName.Parse(ZoneName),
                nsec3
            )
        );
    }

    [Fact]
    public async Task Nsec3_ForAHashedOwnerNameAtASecondary_IsNxDomainWithNoNsec3Answer()
    {
        DnsMessage response = await _secondary.QueryAsync(
            _hashedOwner,
            DnsResourceRecordTypeEnum.Nsec3,
            dnssecOk: true
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
        Assert.DoesNotContain(
            response.Answers,
            record => record.Type == DnsResourceRecordTypeEnum.Nsec3
        );
    }

    [Fact]
    public async Task A_ForAHashedOwnerNameAtThePrimary_IsNxDomain()
    {
        DnsMessage response = await _primary.QueryAsync(
            _hashedOwner,
            DnsResourceRecordTypeEnum.A,
            dnssecOk: true
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, response.ResponseCode);
    }

    [Fact]
    public async Task Axfr_OfTheSecondary_CarriesEveryNsec3RowAndItsSignatureOnce()
    {
        List<DnsResourceRecord> fromPrimary = await PullAxfrAsync(_primary);
        List<DnsResourceRecord> fromSecondary = await PullAxfrAsync(_secondary);

        List<string> primaryChain = ChainKeys(fromPrimary);
        List<string> secondaryChain = ChainKeys(fromSecondary);

        Assert.NotEmpty(primaryChain);
        Assert.Contains(
            fromSecondary,
            record =>
                record.Type == DnsResourceRecordTypeEnum.RrSig
                && record.Name.ToString() == _hashedOwner
        );
        Assert.Equal(primaryChain.Count, secondaryChain.Count);
        Assert.Equal(primaryChain.Order(), secondaryChain.Order());
        Assert.Equal(secondaryChain.Count, secondaryChain.Distinct().Count());
    }

    [Fact]
    public async Task A_ForAnExistingNameAtASecondary_StillAnswers()
    {
        DnsMessage response = await _secondary.QueryAsync(
            $"www.{ZoneName}",
            DnsResourceRecordTypeEnum.A,
            dnssecOk: true
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Contains(response.Answers, record => record.Type == DnsResourceRecordTypeEnum.A);
    }

    [Fact]
    public async Task A_ForAnEmptyNonTerminalAtASecondary_StillAnswersNoData()
    {
        DnsMessage response = await _secondary.QueryAsync(
            $"sub.{ZoneName}",
            DnsResourceRecordTypeEnum.A,
            dnssecOk: true
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, response.ResponseCode);
        Assert.Empty(response.Answers);
    }

    /// <summary>
    ///     This method lists the NSEC3 records and the RRSIGs that cover NSEC3 in a transfer as
    ///     strings of owner, type and data, so two transfers compare as multisets.
    /// </summary>
    private static List<string> ChainKeys(List<DnsResourceRecord> records)
    {
        List<string> keys = [];

        foreach (DnsResourceRecord record in records)
        {
            bool isChain =
                record.Type == DnsResourceRecordTypeEnum.Nsec3
                || (
                    record.Type == DnsResourceRecordTypeEnum.RrSig
                    && record.Data.Length >= 2
                    && (DnsResourceRecordTypeEnum)((record.Data[0] << 8) | record.Data[1])
                        == DnsResourceRecordTypeEnum.Nsec3
                );

            if (isChain)
                keys.Add($"{record.Name}|{record.Type}|{Convert.ToHexString(record.Data)}");
        }

        return keys;
    }

    /// <summary>
    ///     This method adds a TSIG key to a host and binds it to the zone, so an AXFR pull can
    ///     authenticate. Each host stores its own secret, so the key is usable only against its own AXFR.
    /// </summary>
    /// <param name="host">The host to add the key to.</param>
    private async Task AllowTransferAsync(AuthoritativeTestZoneHost host)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
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
    ///     This method performs a TSIG-signed AXFR against a host and returns every wire record of
    ///     the transfer, closing SOA included.
    /// </summary>
    private async Task<List<DnsResourceRecord>> PullAxfrAsync(AuthoritativeTestZoneHost host)
    {
        await using AsyncServiceScope scope = host.Services.CreateAsyncScope();
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

        Assert.Contains(records, record => record.Type == DnsResourceRecordTypeEnum.Nsec3);

        return records;
    }
}
