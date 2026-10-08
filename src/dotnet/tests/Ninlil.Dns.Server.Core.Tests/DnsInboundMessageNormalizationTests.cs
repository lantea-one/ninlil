using System.Net;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Pipeline;
using Ninlil.Dns.Server.Core.Resolution;

// Define our namespace.
namespace Ninlil.Dns.Server.Core.Tests;

/// <summary>
///     This class tests that <see cref="DnsInboundMessage" /> and <see cref="DnsQueryContext" />
///     store their <c>RemoteAddress</c> in canonical form, both when constructed and when copied
///     with a <c>with</c> expression. The two records are where every transport's client address
///     enters the pipeline, so this is what keeps IPv4-mapped IPv6 addresses out of everything
///     behind them. The tests use plain in-memory objects.
/// </summary>
public sealed class DnsInboundMessageNormalizationTests
{
    private static readonly IPAddress Mapped = IPAddress.Parse("::ffff:192.0.2.9");
    private static readonly IPAddress Plain = IPAddress.Parse("192.0.2.9");

    private static DnsInboundMessage BuildMessage(IPAddress address) =>
        new(
            new byte[] { 1, 2 },
            DnsTransportProtocolEnum.Udp,
            address,
            (_, _) => ValueTask.CompletedTask
        );

    private static DnsQueryContext BuildContext(IPAddress address) =>
        new(new DnsMessage(), DnsTransportProtocolEnum.Udp, address, DateTimeOffset.UtcNow);

    [Fact]
    public void InboundMessage_ConstructorStoresAMappedAddressAsPlainIPv4() =>
        Assert.Equal(Plain, BuildMessage(Mapped).RemoteAddress);

    [Fact]
    public void InboundMessage_WithExpressionNormalizesTheNewAddress()
    {
        DnsInboundMessage copy = BuildMessage(IPAddress.Loopback) with { RemoteAddress = Mapped };

        Assert.Equal(Plain, copy.RemoteAddress);
    }

    [Fact]
    public void InboundMessage_DropsTheScopeIdOfAnIPv6Address() =>
        Assert.Equal(
            IPAddress.Parse("fe80::1"),
            BuildMessage(IPAddress.Parse("fe80::1%2")).RemoteAddress
        );

    [Fact]
    public void InboundMessage_KeepsAPlainAddressAsTheSameInstance() =>
        Assert.Same(Plain, BuildMessage(Plain).RemoteAddress);

    [Fact]
    public void InboundMessage_DeconstructsTheNormalizedAddress()
    {
        (_, _, IPAddress address, _, _) = BuildMessage(Mapped);

        Assert.Equal(Plain, address);
    }

    [Fact]
    public void QueryContext_ConstructorStoresAMappedAddressAsPlainIPv4() =>
        Assert.Equal(Plain, BuildContext(Mapped).RemoteAddress);

    [Fact]
    public void QueryContext_WithExpressionNormalizesTheNewAddress()
    {
        DnsQueryContext copy = BuildContext(IPAddress.Loopback) with { RemoteAddress = Mapped };

        Assert.Equal(Plain, copy.RemoteAddress);
    }

    [Fact]
    public void QueryContext_DropsTheScopeIdOfAnIPv6Address() =>
        Assert.Equal(
            IPAddress.Parse("fe80::1"),
            BuildContext(IPAddress.Parse("fe80::1%2")).RemoteAddress
        );
}
