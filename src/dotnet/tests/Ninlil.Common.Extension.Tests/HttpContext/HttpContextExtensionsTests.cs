using System.Net;
using Microsoft.AspNetCore.Http;
using Ninlil.Common.Extension.HttpContext;

// Define our namespace.
namespace Ninlil.Common.Extension.Tests.HttpContext;

/// <summary>
///     These tests run <see cref="HttpContextExtensions" /> against an in-memory
///     <see cref="DefaultHttpContext" />: the remote address in canonical form (a spoofed
///     forwarded-for header is ignored), the bearer token, and the user agent.
/// </summary>
public class HttpContextExtensionsTests
{
    [Fact]
    public void GetIpAddress_WithAForwardedForHeader_IgnoresItAndReturnsTheRemoteAddress()
    {
        DefaultHttpContext context = new();
        context.Request.Headers["x-forwarded-for"] = "203.0.113.5, 10.0.0.1";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");

        Assert.Equal("192.0.2.10", context.GetIpAddress());
    }

    [Fact]
    public void GetIpAddress_WithASpoofedForwardedForHeaderAndNoRemoteAddress_ReturnsNull()
    {
        DefaultHttpContext context = new();
        context.Request.Headers["X-Forwarded-For"] = "203.0.113.5";

        Assert.Null(context.GetIpAddress());
    }

    [Fact]
    public void GetIpAddress_WithARemoteAddress_ReturnsIt()
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.10");

        Assert.Equal("192.168.1.10", context.GetIpAddress());
    }

    [Fact]
    public void GetIpAddress_WithAWhitespaceForwardedForHeader_ReturnsTheRemoteAddress()
    {
        DefaultHttpContext context = new();
        context.Request.Headers["x-forwarded-for"] = "   ";
        context.Connection.RemoteIpAddress = IPAddress.Parse("192.168.1.20");

        Assert.Equal("192.168.1.20", context.GetIpAddress());
    }

    [Theory]
    [InlineData("::ffff:192.0.2.9", "192.0.2.9")]
    [InlineData("2001:db8::1", "2001:db8::1")]
    [InlineData("::1", "::1")]
    [InlineData("fe80::1%2", "fe80::1")]
    public void GetIpAddress_ReturnsTheRemoteAddressInCanonicalForm(string remote, string expected)
    {
        DefaultHttpContext context = new();
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);

        Assert.Equal(expected, context.GetIpAddress());
    }

    [Fact]
    public void GetIpAddress_WithNoRemoteAddress_ReturnsNull()
    {
        DefaultHttpContext context = new();

        Assert.Null(context.GetIpAddress());
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

    [Theory]
    [InlineData("BearerXYZ")]
    [InlineData("bearerfoo")]
    [InlineData("Bearer ")]
    [InlineData("Bearer")]
    public void GetAuthorizationBearer_WithoutASpaceAndAToken_ReturnsNull(string header)
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = header;

        Assert.Null(context.GetAuthorizationBearer());
    }

    [Fact]
    public void GetAuthorizationBearer_WithSeveralSpaces_ReturnsTheTrimmedToken()
    {
        DefaultHttpContext context = new();
        context.Request.Headers.Authorization = "Bearer    tok ";

        Assert.Equal("tok", context.GetAuthorizationBearer());
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
