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
///     This class tests the trust decision of <see cref="ReverseProxyTrustGatingMiddleware" />
///     directly, on a hand-built <see cref="DefaultHttpContext" />, without a host. Through
///     <see cref="NinlilDnsWebApplicationFactory" /> it could not be tested: <c>TestServer</c>
///     leaves <c>HttpContext.Connection.RemoteIpAddress</c> null for every request, so every
///     request would be untrusted and the trusted branch could never run. The class therefore
///     does not join <see cref="WebTestCollection" />.
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

        // A new ForwardedHeadersOptions() already trusts loopback (see
        // ReverseProxyForwardedHeadersConfigurator). Building it through that configurator, as
        // Program.cs does, rather than by hand, is what lets the loopback tests below catch the
        // configurator leaving those defaults in place.
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
        // Unlike ForwardedHeadersMiddleware, which trusts every peer when KnownProxies and
        // KnownIPNetworks are both empty, this middleware never trusts everyone just because
        // nothing is configured.
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware();
        DefaultHttpContext context = CreateContext("203.0.113.10");

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromLoopbackWithNoAllowlistConfigured_StripsForwardedHeaders()
    {
        // A new ForwardedHeadersOptions() trusts loopback (KnownProxies holds ::1 and
        // KnownIPNetworks 127.0.0.0/8). The configurator must clear those defaults before adding
        // the configured entries; otherwise a request over loopback (::1, as a local curl
        // connects) would have its forwarded headers honored with nothing configured.
        // CreateMiddleware() goes through the same ReverseProxyForwardedHeadersConfigurator.Configure
        // call Program.cs makes, so this test checks that rule.
        ReverseProxyTrustGatingMiddleware middleware = CreateMiddleware();
        DefaultHttpContext context = CreateContext("::1");

        await middleware.InvokeAsync(context);

        Assert.False(context.Request.Headers.ContainsKey(ForwardedForHeaderName));
    }

    [Fact]
    public async Task InvokeAsync_FromLoopbackNotInAnExplicitlyConfiguredAllowlist_StripsForwardedHeaders()
    {
        // The same rule with a non-loopback allowlist configured: the default loopback trust
        // must not survive alongside the operator's entries.
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
        // The allowlist and the shared secret are both required, so a correct secret from an
        // address outside the allowlist is not trusted.
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
        // Kestrel's dual-stack IPv6Any listener reports an IPv4 connection in the mapped
        // ::ffff:a.b.c.d form, and a plain IPv4 proxy address must still match it.
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
        // An untrusted request is answered normally, using the connection's address; it is
        // never rejected.
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
