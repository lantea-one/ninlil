using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Web.Middleware;
using Ninlil.Web.Options;
using MicrosoftOptions = Microsoft.Extensions.Options.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="ReverseProxyTrustGatingMiddleware" />'s own trust decision
///     directly, against a hand-constructed <see cref="DefaultHttpContext" /> rather than through
///     a real <see cref="NinlilDnsWebApplicationFactory" /> host -- confirmed necessary, not
///     merely convenient, by first checking <c>TestServer</c>'s own actual behavior directly: its
///     in-memory transport leaves <c>HttpContext.Connection.RemoteIpAddress</c>
///     <see langword="null" /> for every request, which would make every request this
///     middleware ever sees "untrusted" by construction and leave the one branch that matters
///     most (a genuinely trusted request) impossible to exercise through that host at all. This
///     class needs no <see cref="NinlilDnsWebApplicationFactory" />/<see cref="WebTestCollection" />
///     of its own, and is not part of that shared collection, since it builds no host.
/// </summary>
public class ReverseProxyTrustGatingMiddlewareTests
{
    private const string ForwardedForHeaderName = "X-Forwarded-For";
    private const string ForwardedProtoHeaderName = "X-Forwarded-Proto";

    private static ReverseProxyTrustGatingMiddleware CreateMiddleware(
        IReadOnlyList<string> knownProxies = null,
        IReadOnlyList<string> knownNetworks = null,
        string sharedSecret = null,
        string sharedSecretHeaderName = null
    )
    {
        ReverseProxyOptions reverseProxyOptions = new()
        {
            KnownProxies = [.. knownProxies ?? []],
            KnownNetworks = [.. knownNetworks ?? []],
            SharedSecret = sharedSecret,
            SharedSecretHeaderName =
                sharedSecretHeaderName ?? ReverseProxyOptions.DefaultSharedSecretHeaderName,
        };

        // A brand-new ForwardedHeadersOptions() already pre-trusts loopback on its own (see
        // ReverseProxyForwardedHeadersConfigurator's own doc comment) -- routing every test
        // through that same real configurator, exactly as Program.cs itself does, rather than
        // building a ForwardedHeadersOptions by hand here, is what makes
        // InvokeAsync_FromLoopbackWithNoAllowlistConfigured_StripsForwardedHeaders below a
        // genuine regression test for that real bug, not merely a hand-crafted scenario that
        // could never have caught it.
        ForwardedHeadersOptions forwardedHeadersOptions = new();
        ReverseProxyForwardedHeadersConfigurator.Configure(
            forwardedHeadersOptions,
            reverseProxyOptions,
            NullLogger.Instance
        );

        return new ReverseProxyTrustGatingMiddleware(
            _ => Task.CompletedTask,
            MicrosoftOptions.Create(forwardedHeadersOptions),
            MicrosoftOptions.Create(reverseProxyOptions)
        );
    }

    private static DefaultHttpContext CreateContext(
        string remoteAddress,
        string forwardedForValue = "198.51.100.7",
        string secretHeaderName = null,
        string secretHeaderValue = null
    )
    {
        DefaultHttpContext context = new();

        if (remoteAddress is not null)
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);

        context.Request.Headers[ForwardedForHeaderName] = forwardedForValue;
        context.Request.Headers[ForwardedProtoHeaderName] = "https";

        if (secretHeaderName is not null)
            context.Request.Headers[secretHeaderName] = secretHeaderValue;

        return context;
    }

    [Fact]
    public async Task InvokeAsync_FromAKnownProxyAddress_LeavesForwardedHeadersIntact()
    {
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware(
            knownProxies: ["203.0.113.10"]
        );
        DefaultHttpContext context = CreateContext("203.0.113.10");

        await middleware.InvokeAsync(context);

        Assert.True(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
        Assert.True(context.Request.Headers.ContainsKey(ForwardedProtoHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromAKnownNetwork_LeavesForwardedHeadersIntact()
    {
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware(
            knownNetworks: ["10.0.0.0/24"]
        );
        DefaultHttpContext context = CreateContext("10.0.0.55");

        await middleware.InvokeAsync(context);

        Assert.True(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromAnAddressNotInEitherAllowlist_StripsForwardedHeaders()
    {
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware(
            knownProxies: ["203.0.113.10"],
            knownNetworks: ["10.0.0.0/24"]
        );
        DefaultHttpContext context = CreateContext("198.51.100.99");

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
        Assert.False(context.Request.Headers.ContainsKey(ForwardedProtoHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_WithNoAllowlistConfiguredAtAll_StripsForwardedHeaders()
    {
        // A real, deliberate difference from ForwardedHeadersMiddleware's own built-in default
        // (which trusts every peer when its own KnownProxies/KnownIPNetworks are both empty) --
        // this middleware never falls back to trusting everyone just because nothing has been
        // configured yet.
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware();
        DefaultHttpContext context = CreateContext("203.0.113.10");

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromLoopbackWithNoAllowlistConfigured_StripsForwardedHeaders()
    {
        // A regression test for a real bug found only by an actual end-to-end request, not by
        // inspection: a brand-new ForwardedHeadersOptions() already pre-trusts loopback on its
        // own (KnownProxies seeded with ::1, KnownIPNetworks seeded with 127.0.0.0/8) -- a first
        // attempt at this wiring only ever added this project's own configured entries to that
        // already-populated instance, without clearing it first, so a request arriving over
        // loopback (::1, exactly what a local `curl` connects as) had its forwarded headers
        // honored regardless of what an operator had actually configured, or even with nothing
        // configured at all. CreateMiddleware() below routes through the exact same
        // ReverseProxyForwardedHeadersConfigurator.Configure this project's own Program.cs calls,
        // so this test genuinely exercises the real fix (clearing those defaults first), not a
        // hand-crafted stand-in that could never have caught the original bug.
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware();
        DefaultHttpContext context = CreateContext("::1");

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromLoopbackNotInAnExplicitlyConfiguredAllowlist_StripsForwardedHeaders()
    {
        // The same regression, with a real, explicit (non-loopback) allowlist configured, so
        // this also confirms the framework's own default loopback trust never survives alongside
        // an operator's own real entries either.
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware(
            knownProxies: ["203.0.113.10"],
            knownNetworks: ["10.0.0.0/24"]
        );
        DefaultHttpContext context = CreateContext("127.0.0.1");

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_WithNoRemoteAddressAtAll_StripsForwardedHeaders()
    {
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware(
            knownProxies: ["203.0.113.10"]
        );
        DefaultHttpContext context = CreateContext(remoteAddress: null);

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromAKnownProxyWithTheCorrectSharedSecret_LeavesHeadersIntact()
    {
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware(
            knownProxies: ["203.0.113.10"],
            sharedSecret: "correct-horse-battery-staple",
            sharedSecretHeaderName: "X-Test-Secret"
        );
        DefaultHttpContext context = CreateContext(
            "203.0.113.10",
            secretHeaderName: "X-Test-Secret",
            secretHeaderValue: "correct-horse-battery-staple"
        );

        await middleware.InvokeAsync(context);

        Assert.True(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromAKnownProxyWithAWrongSharedSecret_StripsHeaders()
    {
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware(
            knownProxies: ["203.0.113.10"],
            sharedSecret: "correct-horse-battery-staple",
            sharedSecretHeaderName: "X-Test-Secret"
        );
        DefaultHttpContext context = CreateContext(
            "203.0.113.10",
            secretHeaderName: "X-Test-Secret",
            secretHeaderValue: "totally-the-wrong-value"
        );

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromAKnownProxyWithNoSharedSecretHeaderAtAllWhenOneIsRequired_StripsHeaders()
    {
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware(
            knownProxies: ["203.0.113.10"],
            sharedSecret: "correct-horse-battery-staple"
        );
        DefaultHttpContext context = CreateContext("203.0.113.10");

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromAnUntrustedAddressEvenWithTheCorrectSharedSecret_StripsHeaders()
    {
        // The allowlist and the shared secret are both required, not either/or -- a correct
        // secret from a source outside the allowlist is still never trusted.
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware(
            knownProxies: ["203.0.113.10"],
            sharedSecret: "correct-horse-battery-staple",
            sharedSecretHeaderName: "X-Test-Secret"
        );
        DefaultHttpContext context = CreateContext(
            "198.51.100.99",
            secretHeaderName: "X-Test-Secret",
            secretHeaderValue: "correct-horse-battery-staple"
        );

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromAnIPv4MappedIPv6AddressMatchingAPlainIPv4KnownProxy_LeavesHeadersIntact()
    {
        // Kestrel's own dual-stack IPv6Any listener reports an IPv4 connection as its own
        // ::ffff:a.b.c.d mapped form -- an operator who configures a plain IPv4 proxy address
        // must still match regardless of which form the connection actually arrived in.
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware(
            knownProxies: ["203.0.113.10"]
        );
        DefaultHttpContext context = CreateContext("::ffff:203.0.113.10");

        await middleware.InvokeAsync(context);

        Assert.True(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_AlwaysCallsTheNextDelegateRegardlessOfTrust()
    {
        // An untrusted request is answered normally, using the real connection's own address --
        // never rejected outright.
        bool nextCalled = false;
        ReverseProxyTrustGatingMiddleware middleware = new(
            context =>
            {
                nextCalled = true;
                return Task.CompletedTask;
            },
            MicrosoftOptions.Create(new ForwardedHeadersOptions()),
            MicrosoftOptions.Create(new ReverseProxyOptions())
        );
        DefaultHttpContext context = CreateContext("198.51.100.99");

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
    }
}
