using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Dnssec;
using Ninlil.Dns.Server.Dnssec;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Dnssec;

/// <summary>
///     This class covers how <see cref="TransferredSignedZoneDataBuilder" /> treats NSEC3 rows
///     whose owner label is not valid base32hex. The tests build <see cref="RecordEntity" /> rows in
///     memory and call the static builder; no host or database is involved.
/// </summary>
public sealed class TransferredSignedZoneDataBuilderTests
{
    private static readonly DnsName Apex = DnsName.Parse("b32.test");

    /// <summary>
    ///     This method builds a stored NSEC3 row for a hash, or for an arbitrary owner name.
    /// </summary>
    private static RecordEntity Nsec3Row(DnsName owner, byte[] nextHash) =>
        RecordEntityCodec.FromWireRecord(
            new DnsResourceRecord(
                owner,
                DnsResourceRecordTypeEnum.Nsec3,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Nsec3,
                    new Nsec3RecordData(1, false, 0, [], nextHash, [DnsResourceRecordTypeEnum.A])
                )
            ),
            Guid.NewGuid()
        );

    /// <summary>
    ///     This method builds the stored DNSKEY row the builder needs before it returns anything.
    /// </summary>
    private static RecordEntity DnsKeyRow() =>
        RecordEntityCodec.FromWireRecord(
            new DnsResourceRecord(
                Apex,
                DnsResourceRecordTypeEnum.DnsKey,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.DnsKey,
                    new DnsKeyRecordData(true, true, DnssecAlgorithmEnum.Ed25519, new byte[32])
                )
            ),
            Guid.NewGuid()
        );

    [Fact]
    public void Build_Nsec3RowWithAnInvalidOwnerLabel_LeavesItOutOfTheChain()
    {
        byte[] first = new byte[20];
        first[0] = 0x10;
        byte[] second = new byte[20];
        second[0] = 0x40;
        byte[] third = new byte[20];
        third[0] = 0x70;
        DnsName badOwner = new([
            new string('w', 32).Select(c => (byte)c).ToArray(),
            .. Apex.Labels,
        ]);

        SignedZoneData data = TransferredSignedZoneDataBuilder.Build(
            Apex,
            [
                DnsKeyRow(),
                Nsec3Row(Nsec3Hasher.ToOwnerName(first, Apex), second),
                Nsec3Row(badOwner, first),
                Nsec3Row(Nsec3Hasher.ToOwnerName(third, Apex), first),
                Nsec3Row(Nsec3Hasher.ToOwnerName(second, Apex), third),
            ]
        );

        Assert.NotNull(data);
        Assert.Equal(3, data.DenialOfExistenceRecords.Count);
        Assert.Equal(
            [first, second, third],
            data.DenialOfExistenceRecords.Select(record =>
                Nsec3Hasher.ExtractHashFromOwnerName(record.Name)
            )
        );
    }
}
