using System.Net;
using Microsoft.AspNetCore.Http;
using Ninlil.Common.Extension.HttpContext;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.HttpContext;

/// <summary>
///     These tests run <see cref="HttpContextExtensions" /> against an in-memory
///     <see cref="DefaultHttpContext" />: the forwarded-for header and remote-address fallback, the
///     bearer token, and the user agent.
/// </summary>
public class HttpContextExtensionsTests
{
    [Fact]
    public void GetIpAddressReturnsFirstAddressFromXForwardedForHeader()
    {
        DefaultHttpContext context = new();
        context.Request.Headers["x-forwarded-for"] = "203.0.113.5, 10.0.0.1";

        string result = context.GetIpAddress();

        Assert.Equal("203.0.113.5", result);
    }

    [Fact]
    public void GetIpAddressFallsBackToRemoteIpAddressWhenHeaderAbsent()
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.10");

        string result = context.GetIpAddress();

        Assert.Equal("192.168.1.10", result);
    }

    [Fact]
    public void GetIpAddressFallsBackToRemoteIpAddressWhenHeaderIsWhitespace()
    {
        DefaultHttpContext context = new();
        context.Request.Headers["x-forwarded-for"] = "   ";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.20");

        string result = context.GetIpAddress();

        Assert.Equal("192.168.1.20", result);
    }

    [Fact]
    public void GetAuthorizationBearerReturnsTheTokenWhenPresent()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = "Bearer abc123";

        string result = context.GetAuthorizationBearer();

        Assert.Equal("abc123", result);
    }

    [Fact]
    public void GetAuthorizationBearerIsCaseInsensitiveForTheSchemeName()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = "bearer abc123";

        string result = context.GetAuthorizationBearer();

        Assert.Equal("abc123", result);
    }

    [Fact]
    public void GetAuthorizationBearerReturnsNullWhenHeaderIsAbsent()
    {
        DefaultHttpContext context = new();

        Assert.Null(context.GetAuthorizationBearer());
    }

    [Fact]
    public void GetAuthorizationBearerReturnsNullWhenSchemeIsNotBearer()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = "Basic dXNlcjpwYXNz";

        Assert.Null(context.GetAuthorizationBearer());
    }

    [Fact]
    public void GetUserAgentReturnsTheHeaderValue()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.UserAgent = "test-agent/1.0";

        string result = context.GetUserAgent();

        Assert.Equal("test-agent/1.0", result);
    }

    [Fact]
    public void GetUserAgentReturnsEmptyStringWhenHeaderIsAbsent()
    {
        DefaultHttpContext context = new();

        Assert.Equal(string.Empty, context.GetUserAgent());
    }
}
