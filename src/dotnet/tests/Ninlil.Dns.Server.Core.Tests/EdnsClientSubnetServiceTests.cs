using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.ClientSubnet;
using Ninlil.Dns.Server.Core.Tests.Fakes;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests <see cref="EdnsClientSubnetService" /> with a fixed options monitor,
///     including its precedence: <see cref="EdnsClientSubnetOptions.OverrideAddress" /> wins,
///     then a client-supplied ECS option is relayed, and only then is one built from the
///     client's source address.
/// </summary>
public class EdnsClientSubnetServiceTests
{
    private static readonly IPAddress DownstreamClient = IPAddress.Parse("203.0.113.55");

    private static EdnsClientSubnetService CreateService(EdnsClientSubnetOptions options) =>
        new(new StaticOptionsMonitor<EdnsClientSubnetOptions>(options));

    private static DnsMessage BuildQuery(DnsEdnsOption clientSubnetOption = null) =>
        new()
        {
            Id = 1,
            Opcode = DnsOpcodeEnum.Query,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("example.com."),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
            Edns = clientSubnetOption is null
                ? null
                : new DnsEdnsOptions(0, 1232, false, [clientSubnetOption]),
        };

    [Fact]
    public void BuildOutboundOptionReturnsNullWhenDisabled()
    {
        EdnsClientSubnetService service = CreateService(
            new EdnsClientSubnetOptions { Enable = false }
        );

        DnsEdnsOption option = service.BuildOutboundOption(DownstreamClient, BuildQuery());

        Assert.Null(option);
    }

    [Fact]
    public void BuildOutboundOptionUsesTheOverrideAddressWhenConfigured()
    {
        EdnsClientSubnetService service = CreateService(
            new EdnsClientSubnetOptions { Enable = true, OverrideAddress = "198.51.100.9" }
        );

        DnsEdnsOption option = service.BuildOutboundOption(DownstreamClient, BuildQuery());

        Assert.NotNull(option);
        Assert.True(EdnsClientSubnetCodec.TryDecode(option, out EdnsClientSubnetOptionData data));
        // /24 default: the override address's host octet is masked away.
        Assert.Equal(IPAddress.Parse("198.51.100.0"), data.Address);
    }

    [Fact]
    public void BuildOutboundOptionRelaysAClientSuppliedOptionUnchangedOverTheRealAddress()
    {
        EdnsClientSubnetService service = CreateService(
            new EdnsClientSubnetOptions { Enable = true, SourcePrefixLengthIPv4 = 24 }
        );
        DnsEdnsOption clientSupplied = EdnsClientSubnetCodec.Encode(
            IPAddress.Parse("192.0.2.200"),
            16,
            scopePrefixLength: 0
        );

        DnsEdnsOption option = service.BuildOutboundOption(
            DownstreamClient,
            BuildQuery(clientSupplied)
        );

        Assert.NotNull(option);
        Assert.True(EdnsClientSubnetCodec.TryDecode(option, out EdnsClientSubnetOptionData data));
        // The client's /16, not the configured /24, and not DownstreamClient's address.
        Assert.Equal(16, data.SourcePrefixLength);
        Assert.Equal(IPAddress.Parse("192.0.0.0"), data.Address);
    }

    [Fact]
    public void BuildOutboundOptionSynthesizesFromTheRealDownstreamAddressAsALastResort()
    {
        EdnsClientSubnetService service = CreateService(
            new EdnsClientSubnetOptions { Enable = true, SourcePrefixLengthIPv4 = 24 }
        );

        DnsEdnsOption option = service.BuildOutboundOption(DownstreamClient, BuildQuery());

        Assert.NotNull(option);
        Assert.True(EdnsClientSubnetCodec.TryDecode(option, out EdnsClientSubnetOptionData data));
        Assert.Equal(24, data.SourcePrefixLength);
        Assert.Equal(IPAddress.Parse("203.0.113.0"), data.Address);
    }

    [Fact]
    public void BuildOutboundOptionReturnsNullWhenNoDownstreamAddressIsKnownAndNothingElseApplies()
    {
        EdnsClientSubnetService service = CreateService(
            new EdnsClientSubnetOptions { Enable = true }
        );

        DnsEdnsOption option = service.BuildOutboundOption(null, BuildQuery());

        Assert.Null(option);
    }

    [Fact]
    public void TryExtractFromDownstreamRequestReturnsFalseWhenNoEcsOptionIsPresent()
    {
        EdnsClientSubnetService service = CreateService(
            new EdnsClientSubnetOptions { Enable = true }
        );

        bool found = service.TryExtractFromDownstreamRequest(
            BuildQuery(),
            out EdnsClientSubnetOptionData data
        );

        Assert.False(found);
        Assert.Null(data);
    }

    [Fact]
    public void AttachToRequestClonesRatherThanMutatingTheOriginal()
    {
        DnsMessage original = BuildQuery();
        DnsEdnsOption option = EdnsClientSubnetCodec.Encode(
            IPAddress.Parse("192.0.2.1"),
            24,
            scopePrefixLength: 0
        );

        DnsMessage attached = EdnsClientSubnetService.AttachToRequest(original, option);

        Assert.Null(original.Edns);
        Assert.NotNull(attached.Edns);
        Assert.Contains(
            attached.Edns.Options,
            o => o.Code == (ushort)EdnsOptionCodeEnum.ClientSubnet
        );
    }

    [Fact]
    public void AttachToRequestReplacesRatherThanDuplicatesAnExistingEcsOption()
    {
        DnsEdnsOption existing = EdnsClientSubnetCodec.Encode(
            IPAddress.Parse("192.0.2.1"),
            24,
            scopePrefixLength: 0
        );
        DnsMessage original = BuildQuery(existing);
        DnsEdnsOption replacement = EdnsClientSubnetCodec.Encode(
            IPAddress.Parse("198.51.100.1"),
            24,
            scopePrefixLength: 0
        );

        DnsMessage attached = EdnsClientSubnetService.AttachToRequest(original, replacement);

        DnsEdnsOption onlyEcsOption = Assert.Single(
            attached.Edns.Options,
            o => o.Code == (ushort)EdnsOptionCodeEnum.ClientSubnet
        );
        Assert.True(
            EdnsClientSubnetCodec.TryDecode(onlyEcsOption, out EdnsClientSubnetOptionData data)
        );
        Assert.Equal(IPAddress.Parse("198.51.100.0"), data.Address);
    }
}
