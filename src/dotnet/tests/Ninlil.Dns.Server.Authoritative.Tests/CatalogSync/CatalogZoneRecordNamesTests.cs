using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Server.Authoritative.CatalogSync;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.CatalogSync;

/// <summary>
///     This class tests <see cref="CatalogZoneRecordNames" />, which builds and parses the RFC 9432
///     owner names; no database is involved.
/// </summary>
public sealed class CatalogZoneRecordNamesTests
{
    [Fact]
    public void VersionRecordOwnerNameIsVersionDotCatalogApex()
    {
        Assert.Equal(
            "version.catalog.invalid",
            CatalogZoneRecordNames.VersionRecordOwnerName("catalog.invalid")
        );
    }

    [Fact]
    public void ComputeUniqueLabelIsDeterministicForTheSameName()
    {
        string first = CatalogZoneRecordNames.ComputeUniqueLabel("example.com");
        string second = CatalogZoneRecordNames.ComputeUniqueLabel("example.com");

        Assert.Equal(first, second);
        // A SHA-1 digest, lowercase hexadecimal: 40 characters.
        Assert.Equal(40, first.Length);
        Assert.Equal(first.ToLowerInvariant(), first);
    }

    [Fact]
    public void ComputeUniqueLabelIsCaseInsensitiveOnTheMemberName()
    {
        Assert.Equal(
            CatalogZoneRecordNames.ComputeUniqueLabel("Example.COM"),
            CatalogZoneRecordNames.ComputeUniqueLabel("example.com")
        );
    }

    [Fact]
    public void ComputeUniqueLabelDiffersForDifferentNames()
    {
        Assert.NotEqual(
            CatalogZoneRecordNames.ComputeUniqueLabel("example.com"),
            CatalogZoneRecordNames.ComputeUniqueLabel("example.net")
        );
    }

    [Fact]
    public void MemberRecordOwnerNamePlacesTheUniqueLabelUnderTheZonesContainer()
    {
        string uniqueLabel = CatalogZoneRecordNames.ComputeUniqueLabel("example.com");

        Assert.Equal(
            $"{uniqueLabel}.zones.catalog.invalid",
            CatalogZoneRecordNames.MemberRecordOwnerName("catalog.invalid", uniqueLabel)
        );
    }

    [Fact]
    public void TryGetMemberZoneNameRecognizesAGenuineMemberPtrRecord()
    {
        string uniqueLabel = CatalogZoneRecordNames.ComputeUniqueLabel("example.com");
        RecordEntity record = BuildPtrRecord(
            CatalogZoneRecordNames.MemberRecordOwnerName("catalog.invalid", uniqueLabel),
            "example.com"
        );

        bool recognized = CatalogZoneRecordNames.TryGetMemberZoneName(
            record,
            "catalog.invalid",
            out string memberZoneName
        );

        Assert.True(recognized);
        Assert.Equal("example.com", memberZoneName);
    }

    [Fact]
    public void TryGetMemberZoneNameRejectsAGroupSubEntryDespiteBeingAPtrRecordUnderTheSameContainer()
    {
        string uniqueLabel = CatalogZoneRecordNames.ComputeUniqueLabel("example.com");
        // RFC 9432's "group." qualifier sub-entry -- one extra label ahead of <unique-N>,
        // still a PTR record, still nested under "zones.$CATZ", but never a genuine member entry.
        RecordEntity groupRecord = BuildPtrRecord(
            $"group.{uniqueLabel}.zones.catalog.invalid",
            "some-group-value.invalid"
        );

        bool recognized = CatalogZoneRecordNames.TryGetMemberZoneName(
            groupRecord,
            "catalog.invalid",
            out string memberZoneName
        );

        Assert.False(recognized);
        Assert.Null(memberZoneName);
    }

    [Fact]
    public void TryGetMemberZoneNameRejectsARecordOutsideTheZonesContainerEntirely()
    {
        RecordEntity unrelatedRecord = BuildPtrRecord(
            "something-else.catalog.invalid",
            "example.com"
        );

        Assert.False(
            CatalogZoneRecordNames.TryGetMemberZoneName(unrelatedRecord, "catalog.invalid", out _)
        );
    }

    [Fact]
    public void TryGetMemberZoneNameRejectsANonPtrRecordAtAnOtherwiseMatchingOwnerName()
    {
        string uniqueLabel = CatalogZoneRecordNames.ComputeUniqueLabel("example.com");
        RecordEntity txtRecord = new()
        {
            Id = Guid.NewGuid(),
            ZoneId = Guid.NewGuid(),
            Name = CatalogZoneRecordNames.MemberRecordOwnerName("catalog.invalid", uniqueLabel),
            Type = DnsResourceRecordTypeEnum.Txt,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = 0,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                typeof(TxtRecordData),
                new TxtRecordData(["not a member entry"])
            ),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };

        Assert.False(
            CatalogZoneRecordNames.TryGetMemberZoneName(txtRecord, "catalog.invalid", out _)
        );
    }

    private static RecordEntity BuildPtrRecord(string ownerName, string targetName) =>
        new()
        {
            Id = Guid.NewGuid(),
            ZoneId = Guid.NewGuid(),
            Name = ownerName,
            Type = DnsResourceRecordTypeEnum.Ptr,
            Class = DnsClassEnum.Internet,
            TimeToLiveSeconds = 0,
            DataJson = Ninlil.Common.Serializer.JsonSerializer.Serialize(
                typeof(PtrRecordData),
                new PtrRecordData(DnsName.Parse(targetName))
            ),
            CreatedAtUtc = DateTimeOffset.UtcNow,
        };
}
