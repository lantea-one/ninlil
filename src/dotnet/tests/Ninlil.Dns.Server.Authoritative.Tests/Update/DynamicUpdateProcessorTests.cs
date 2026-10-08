using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Update;
using Xunit;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Update;

/// <summary>
///     This class tests <see cref="DynamicUpdateProcessor" /> (RFC 2136 §3.2 prerequisites, §3.4.1
///     prescan and §3.4.2 apply rules) with plain wire records and stored rows for the zone
///     <c>example.</c>. No host, database or socket is involved.
/// </summary>
public sealed class DynamicUpdateProcessorTests
{
    private static readonly DnsName Apex = DnsName.Parse("example.");
    private static readonly Guid ZoneId = Guid.NewGuid();

    private static DnsResourceRecord Wire(
        string name,
        DnsResourceRecordTypeEnum type,
        DnsClassEnum recordClass,
        uint ttl,
        byte[] data
    ) => new(DnsName.Parse(name), type, recordClass, ttl, data);

    private static byte[] Ip(byte lastOctet) => [192, 0, 2, lastOctet];

    private static byte[] NameData(string name) => DnsName.Parse(name).ToUncompressedWireBytes();

    private static DnsResourceRecord A(string name, byte lastOctet, uint ttl = 300) =>
        Wire(name, DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet, ttl, Ip(lastOctet));

    private static DnsResourceRecord Cname(string name, string target) =>
        Wire(name, DnsResourceRecordTypeEnum.Cname, DnsClassEnum.Internet, 300, NameData(target));

    private static DnsResourceRecord Ns(string name, string target) =>
        Wire(name, DnsResourceRecordTypeEnum.NS, DnsClassEnum.Internet, 3600, NameData(target));

    private static RecordEntity Stored(DnsResourceRecord record) =>
        RecordEntityCodec.FromWireRecord(record, ZoneId);

    private static (List<RecordEntity> ToAdd, List<RecordEntity> ToRemove) Diff(
        List<DnsResourceRecord> updates,
        params DnsResourceRecord[] existing
    ) => DynamicUpdateProcessor.ComputeDiff(updates, [.. existing.Select(Stored)], ZoneId, Apex);

    // ---- Prerequisites (§3.2) ----

    [Fact]
    public void CheckPrerequisites_ForAnOwnerOutsideTheZone_ReturnsNotZone()
    {
        DnsResponseCodeEnum result = DynamicUpdateProcessor.CheckPrerequisites(
            [Wire("other.test.", DnsResourceRecordTypeEnum.Any, DnsClassEnum.Any, 0, [])],
            [],
            Apex
        );

        Assert.Equal(DnsResponseCodeEnum.NotZone, result);
    }

    [Fact]
    public void CheckPrerequisites_WithANonZeroTtl_ReturnsFormatError()
    {
        DnsResponseCodeEnum result = DynamicUpdateProcessor.CheckPrerequisites(
            [A("host.example.", 1, ttl: 1)],
            [],
            Apex
        );

        Assert.Equal(DnsResponseCodeEnum.FormatError, result);
    }

    [Fact]
    public void CheckPrerequisites_ForClassAnyWithRdata_ReturnsFormatError()
    {
        DnsResponseCodeEnum result = DynamicUpdateProcessor.CheckPrerequisites(
            [Wire("host.example.", DnsResourceRecordTypeEnum.A, DnsClassEnum.Any, 0, Ip(1))],
            [],
            Apex
        );

        Assert.Equal(DnsResponseCodeEnum.FormatError, result);
    }

    [Fact]
    public void CheckPrerequisites_ForClassChaos_ReturnsFormatError()
    {
        DnsResponseCodeEnum result = DynamicUpdateProcessor.CheckPrerequisites(
            [Wire("host.example.", DnsResourceRecordTypeEnum.A, DnsClassEnum.Chaos, 0, Ip(1))],
            [],
            Apex
        );

        Assert.Equal(DnsResponseCodeEnum.FormatError, result);
    }

    [Fact]
    public void CheckPrerequisites_ForAMalformedPrerequisiteAfterAFailingOne_ReturnsFormatError()
    {
        // The form of the whole section is checked before any prerequisite meets the zone.
        DnsResponseCodeEnum result = DynamicUpdateProcessor.CheckPrerequisites(
            [
                Wire("gone.example.", DnsResourceRecordTypeEnum.Any, DnsClassEnum.Any, 0, []),
                A("host.example.", 1, ttl: 5),
            ],
            [],
            Apex
        );

        Assert.Equal(DnsResponseCodeEnum.FormatError, result);
    }

    [Fact]
    public void CheckPrerequisites_ForANameThatDoesNotExist_ReturnsNxDomain()
    {
        DnsResponseCodeEnum result = DynamicUpdateProcessor.CheckPrerequisites(
            [Wire("gone.example.", DnsResourceRecordTypeEnum.Any, DnsClassEnum.Any, 0, [])],
            [Stored(A("host.example.", 1))],
            Apex
        );

        Assert.Equal(DnsResponseCodeEnum.NxDomain, result);
    }

    [Fact]
    public void CheckPrerequisites_ForAnExistingRrsetAndExactValues_ReturnsNoError()
    {
        DnsResponseCodeEnum result = DynamicUpdateProcessor.CheckPrerequisites(
            [
                Wire("host.example.", DnsResourceRecordTypeEnum.A, DnsClassEnum.Any, 0, []),
                Wire("host.example.", DnsResourceRecordTypeEnum.A, DnsClassEnum.Internet, 0, Ip(1)),
            ],
            [Stored(A("host.example.", 1))],
            Apex
        );

        Assert.Equal(DnsResponseCodeEnum.NoError, result);
    }

    // ---- Prescan (§3.4.1) ----

    private static DnsResponseCodeEnum Prescan(params DnsResourceRecord[] updates) =>
        DynamicUpdateProcessor.PrescanUpdateSection(updates, DnsClassEnum.Internet, Apex);

    [Fact]
    public void PrescanUpdateSection_ForAnOwnerOutsideTheZone_ReturnsNotZone()
    {
        Assert.Equal(DnsResponseCodeEnum.NotZone, Prescan(A("host.other.test.", 1)));
    }

    [Fact]
    public void PrescanUpdateSection_ForAnOwnerWhoseNameEndsLikeTheApexButIsAnotherZone_ReturnsNotZone()
    {
        // "notexample." ends in the characters of "example." but is not below it.
        Assert.Equal(DnsResponseCodeEnum.NotZone, Prescan(A("notexample.", 1)));
    }

    [Fact]
    public void PrescanUpdateSection_ForAnAddOfTypeAny_ReturnsFormatError()
    {
        Assert.Equal(
            DnsResponseCodeEnum.FormatError,
            Prescan(
                Wire(
                    "host.example.",
                    DnsResourceRecordTypeEnum.Any,
                    DnsClassEnum.Internet,
                    300,
                    Ip(1)
                )
            )
        );
    }

    [Fact]
    public void PrescanUpdateSection_ForAClassAnyDeleteWithTtl_ReturnsFormatError()
    {
        Assert.Equal(
            DnsResponseCodeEnum.FormatError,
            Prescan(Wire("host.example.", DnsResourceRecordTypeEnum.A, DnsClassEnum.Any, 1, []))
        );
    }

    [Fact]
    public void PrescanUpdateSection_ForAClassNoneDeleteOfTypeAny_ReturnsFormatError()
    {
        Assert.Equal(
            DnsResponseCodeEnum.FormatError,
            Prescan(Wire("host.example.", DnsResourceRecordTypeEnum.Any, DnsClassEnum.None, 0, []))
        );
    }

    [Theory]
    [InlineData(251)]
    [InlineData(252)]
    [InlineData(253)]
    [InlineData(254)]
    public void PrescanUpdateSection_ForAMetaTypeInAnyOperation_ReturnsFormatError(ushort type)
    {
        DnsResourceRecordTypeEnum meta = (DnsResourceRecordTypeEnum)type;

        Assert.Equal(
            DnsResponseCodeEnum.FormatError,
            Prescan(Wire("h.example.", meta, DnsClassEnum.Internet, 300, Ip(1)))
        );
        Assert.Equal(
            DnsResponseCodeEnum.FormatError,
            Prescan(Wire("h.example.", meta, DnsClassEnum.Any, 0, []))
        );
        Assert.Equal(
            DnsResponseCodeEnum.FormatError,
            Prescan(Wire("h.example.", meta, DnsClassEnum.None, 0, Ip(1)))
        );
    }

    [Fact]
    public void PrescanUpdateSection_ForAClassAnyDeleteOfAllRrsetsAtAName_ReturnsNoError()
    {
        Assert.Equal(
            DnsResponseCodeEnum.NoError,
            Prescan(Wire("host.example.", DnsResourceRecordTypeEnum.Any, DnsClassEnum.Any, 0, []))
        );
    }

    [Fact]
    public void PrescanUpdateSection_ForAnUnknownClass_ReturnsFormatError()
    {
        Assert.Equal(
            DnsResponseCodeEnum.FormatError,
            Prescan(
                Wire("host.example.", DnsResourceRecordTypeEnum.A, DnsClassEnum.Chaos, 0, Ip(1))
            )
        );
    }

    // ---- Apply (§3.4.2) ----

    [Fact]
    public void ComputeDiff_AddingACnameAtANameWithAnA_IsIgnored()
    {
        (List<RecordEntity> add, List<RecordEntity> remove) = Diff(
            [Cname("host.example.", "target.example.")],
            A("host.example.", 1)
        );

        Assert.Empty(add);
        Assert.Empty(remove);
    }

    [Fact]
    public void ComputeDiff_AddingAnAAtACnameName_IsIgnored()
    {
        (List<RecordEntity> add, List<RecordEntity> remove) = Diff(
            [A("alias.example.", 1)],
            Cname("alias.example.", "target.example.")
        );

        Assert.Empty(add);
        Assert.Empty(remove);
    }

    [Fact]
    public void ComputeDiff_AddingASecondCname_ReplacesTheFirst()
    {
        (List<RecordEntity> add, List<RecordEntity> remove) = Diff(
            [Cname("alias.example.", "second.example.")],
            Cname("alias.example.", "first.example.")
        );

        RecordEntity added = Assert.Single(add);
        Assert.Contains("second", added.DataJson);
        RecordEntity removed = Assert.Single(remove);
        Assert.Contains("first", removed.DataJson);
    }

    [Fact]
    public void ComputeDiff_AddingTheSameAWithANewTtl_LeavesOneRecordWithTheNewTtl()
    {
        DnsResourceRecord first = A("host.example.", 1, ttl: 60);
        (List<RecordEntity> addFirst, List<RecordEntity> removeFirst) = Diff([first]);
        RecordEntity stored = Assert.Single(addFirst);
        Assert.Empty(removeFirst);
        Assert.Equal(60u, stored.TimeToLiveSeconds);

        // The second update meets the row the first one created.
        (List<RecordEntity> add, List<RecordEntity> remove) = DynamicUpdateProcessor.ComputeDiff(
            [A("host.example.", 1, ttl: 300)],
            [stored],
            ZoneId,
            Apex
        );

        Assert.Equal(stored.Id, Assert.Single(remove).Id);
        Assert.Equal(300u, Assert.Single(add).TimeToLiveSeconds);
    }

    [Fact]
    public void ComputeDiff_AddingTheSameATwiceInOneUpdate_YieldsOneRecordWithTheLastTtl()
    {
        (List<RecordEntity> add, List<RecordEntity> remove) = Diff([
            A("host.example.", 1, ttl: 60),
            A("host.example.", 1, ttl: 300),
        ]);

        Assert.Empty(remove);
        Assert.Equal(300u, Assert.Single(add).TimeToLiveSeconds);
    }

    [Fact]
    public void ComputeDiff_AddingTheSameAWithTheSameTtl_ChangesNothing()
    {
        (List<RecordEntity> add, List<RecordEntity> remove) = Diff(
            [A("host.example.", 1)],
            A("host.example.", 1)
        );

        Assert.Empty(add);
        Assert.Empty(remove);
    }

    [Fact]
    public void ComputeDiff_ClassAnyTypeNsAtTheApex_KeepsTheApexNs()
    {
        (List<RecordEntity> add, List<RecordEntity> remove) = Diff(
            [Wire("example.", DnsResourceRecordTypeEnum.NS, DnsClassEnum.Any, 0, [])],
            Ns("example.", "ns1.example."),
            Ns("example.", "ns2.example.")
        );

        Assert.Empty(add);
        Assert.Empty(remove);
    }

    [Fact]
    public void ComputeDiff_ClassNoneDeletingTheLastApexNs_KeepsIt()
    {
        (List<RecordEntity> add, List<RecordEntity> remove) = Diff(
            [
                Wire(
                    "example.",
                    DnsResourceRecordTypeEnum.NS,
                    DnsClassEnum.None,
                    0,
                    NameData("ns1.example.")
                ),
            ],
            Ns("example.", "ns1.example.")
        );

        Assert.Empty(add);
        Assert.Empty(remove);
    }

    [Fact]
    public void ComputeDiff_ClassNoneDeletingOneOfTwoApexNs_RemovesIt()
    {
        (List<RecordEntity> add, List<RecordEntity> remove) = Diff(
            [
                Wire(
                    "example.",
                    DnsResourceRecordTypeEnum.NS,
                    DnsClassEnum.None,
                    0,
                    NameData("ns1.example.")
                ),
            ],
            Ns("example.", "ns1.example."),
            Ns("example.", "ns2.example.")
        );

        Assert.Empty(add);
        Assert.Contains("ns1", Assert.Single(remove).DataJson);
    }

    [Fact]
    public void ComputeDiff_ClassAnyTypeAnyAtTheApex_KeepsNsButRemovesOtherData()
    {
        (List<RecordEntity> add, List<RecordEntity> remove) = Diff(
            [Wire("example.", DnsResourceRecordTypeEnum.Any, DnsClassEnum.Any, 0, [])],
            Ns("example.", "ns1.example."),
            A("example.", 9)
        );

        Assert.Empty(add);
        Assert.Equal(DnsResourceRecordTypeEnum.A, Assert.Single(remove).Type);
    }
}
