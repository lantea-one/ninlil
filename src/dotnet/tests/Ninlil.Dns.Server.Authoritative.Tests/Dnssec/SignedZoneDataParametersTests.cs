using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Dnssec;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class covers the chain mode and NSEC3 hash parameters that <see cref="SignedZoneData" />
///     derives from the records it holds, and that <see cref="DenialOfExistenceProofBuilder" />
///     gives an empty proof, not an exception, for a chain whose hash algorithm is unsupported. The
///     tests build <see cref="SignedZoneData" /> directly from wire records; no host or database
///     is involved.
/// </summary>
public sealed class SignedZoneDataParametersTests
{
    private static readonly DnsName Apex = DnsName.Parse("params.test");

    /// <summary>
    ///     This method wraps typed record data in a wire record at an owner name.
    /// </summary>
    private static DnsResourceRecord Record(
        DnsName owner,
        DnsResourceRecordTypeEnum type,
        IDnsRecordData data
    ) =>
        new(owner, type, DnsClassEnum.Internet, 300, DnsRecordDataCodecRegistry.Encode(type, data));

    /// <summary>
    ///     This method builds a <see cref="SignedZoneData" /> that holds only a chain and,
    ///     optionally, an NSEC3PARAM record.
    /// </summary>
    private static SignedZoneData Build(
        List<DnsResourceRecord> chain,
        DnsResourceRecord nsec3Param = null
    ) =>
        new(
            [],
            new Dictionary<
                (DnsName Name, DnsResourceRecordTypeEnum Type),
                IReadOnlyList<DnsResourceRecord>
            >(),
            chain,
            nsec3Param,
            new Dictionary<DnsName, IReadOnlyList<DnsResourceRecord>>(),
            DateTimeOffset.UtcNow.AddDays(1)
        );

    /// <summary>
    ///     This method builds one NSEC3 record with the given parameters.
    /// </summary>
    private static DnsResourceRecord Nsec3Record(byte algorithm, ushort iterations, byte[] salt) =>
        Record(
            Nsec3Hasher.ToOwnerName(new byte[20], Apex),
            DnsResourceRecordTypeEnum.Nsec3,
            new Nsec3RecordData(
                algorithm,
                false,
                iterations,
                salt,
                new byte[20],
                [DnsResourceRecordTypeEnum.Soa]
            )
        );

    [Fact]
    public void Properties_WithAnNsec3ParamRecord_ReportNsec3AndItsParameters()
    {
        SignedZoneData data = Build(
            [Nsec3Record(1, 99, [0x01])],
            Record(
                Apex,
                DnsResourceRecordTypeEnum.Nsec3Param,
                new Nsec3ParamRecordData(1, 7, [0xAA])
            )
        );

        Assert.Equal(DnssecDenialOfExistenceModeEnum.Nsec3, data.DenialOfExistenceMode);
        Assert.NotNull(data.Nsec3Parameters);
        Assert.Equal(1, data.Nsec3Parameters.HashAlgorithm);
        Assert.Equal(7, data.Nsec3Parameters.Iterations);
        Assert.Equal(new byte[] { 0xAA }, data.Nsec3Parameters.Salt);
    }

    [Fact]
    public void Properties_WithNsec3RecordsOnly_ReportTheFirstRecordsParameters()
    {
        SignedZoneData data = Build([Nsec3Record(1, 3, [0xBE, 0xEF])]);

        Assert.Equal(DnssecDenialOfExistenceModeEnum.Nsec3, data.DenialOfExistenceMode);
        Assert.Equal(3, data.Nsec3Parameters.Iterations);
        Assert.Equal(new byte[] { 0xBE, 0xEF }, data.Nsec3Parameters.Salt);
    }

    [Fact]
    public void Properties_WithNsecRecords_ReportNsecAndNoParameters()
    {
        SignedZoneData data = Build([
            Record(
                Apex,
                DnsResourceRecordTypeEnum.Nsec,
                new NsecRecordData(Apex, [DnsResourceRecordTypeEnum.Soa])
            ),
        ]);

        Assert.Equal(DnssecDenialOfExistenceModeEnum.Nsec, data.DenialOfExistenceMode);
        Assert.Null(data.Nsec3Parameters);
    }

    [Fact]
    public void BuildNxDomainProof_WithAnUnsupportedHashAlgorithm_ReturnsAnEmptyProof()
    {
        SignedZoneData data = Build(
            [Nsec3Record(2, 0, [])],
            Record(Apex, DnsResourceRecordTypeEnum.Nsec3Param, new Nsec3ParamRecordData(2, 0, []))
        );

        Assert.Empty(
            DenialOfExistenceProofBuilder.BuildNxDomainProof(
                data,
                Apex,
                DnsName.Parse("nothere.params.test")
            )
        );
        Assert.Empty(
            DenialOfExistenceProofBuilder.BuildNoDataProof(
                data,
                Apex,
                DnsName.Parse("www.params.test")
            )
        );
    }
}
