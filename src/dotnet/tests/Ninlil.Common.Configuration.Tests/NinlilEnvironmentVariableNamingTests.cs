using Ninlil.Common.Configuration;

// Define our namespace.
namespace Ninlil.Common.Configuration.Tests;

/// <summary>
///     These tests cover <see cref="NinlilEnvironmentVariableNaming" />'s own PascalCase-to-
///     underscore transform, including the codebase's own two real irregular-token cases
///     (<c>IPv4</c>/<c>IPv6</c>) that ordinary word-boundary splitting cannot handle correctly on
///     its own.
/// </summary>
public sealed class NinlilEnvironmentVariableNamingTests
{
    /// <summary>
    ///     Confirms the exact worked example from <c>ENVIRONMENT.md</c>'s own <c>NINLIL_</c>
    ///     environment-variable reference: <c>DnsServer:Web:HttpPort</c> becomes
    ///     <c>NINLIL_DNS_SERVER_WEB_HTTP_PORT</c>.
    /// </summary>
    [Fact]
    public void ToEnvironmentVariableName_MatchesTheDocumentedWorkedExample()
    {
        string result = NinlilEnvironmentVariableNaming.ToEnvironmentVariableName(
            "DnsServer:Web:HttpPort"
        );

        Assert.Equal("NINLIL_DNS_SERVER_WEB_HTTP_PORT", result);
    }

    [Theory]
    [InlineData("Storage:Engine", "NINLIL_STORAGE_ENGINE")]
    [InlineData("Cluster:NodeId", "NINLIL_CLUSTER_NODE_ID")]
    [InlineData(
        "DnsServer:AdminLoginRateLimit:PermitLimit",
        "NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_PERMIT_LIMIT"
    )]
    [InlineData(
        "DnsServer:Protocol:DnsOverTls:ProxyProtocol:Enable",
        "NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_PROXY_PROTOCOL_ENABLE"
    )]
    public void ToEnvironmentVariableName_SplitsEveryOrdinaryPascalCaseSegment(
        string configurationPath,
        string expected
    )
    {
        Assert.Equal(
            expected,
            NinlilEnvironmentVariableNaming.ToEnvironmentVariableName(configurationPath)
        );
    }

    [Theory]
    [InlineData(
        "DnsServer:ClientSubnet:SourcePrefixLengthIPv4",
        "NINLIL_DNS_SERVER_CLIENT_SUBNET_SOURCE_PREFIX_LENGTH_IPV4"
    )]
    [InlineData(
        "DnsServer:ClientSubnet:SourcePrefixLengthIPv6",
        "NINLIL_DNS_SERVER_CLIENT_SUBNET_SOURCE_PREFIX_LENGTH_IPV6"
    )]
    [InlineData(
        "DnsServer:RateLimiting:SubnetPrefixLengthIPv4",
        "NINLIL_DNS_SERVER_RATE_LIMITING_SUBNET_PREFIX_LENGTH_IPV4"
    )]
    public void ToEnvironmentVariableName_TreatsIPv4AndIPv6AsOneAtomicWord(
        string configurationPath,
        string expected
    )
    {
        Assert.Equal(
            expected,
            NinlilEnvironmentVariableNaming.ToEnvironmentVariableName(configurationPath)
        );
    }

    [Fact]
    public void ToEnvironmentVariableName_NeverContainsADoubleUnderscore()
    {
        string result = NinlilEnvironmentVariableNaming.ToEnvironmentVariableName(
            "DnsServer:ClientSubnet:SourcePrefixLengthIPv4"
        );

        Assert.DoesNotContain("__", result);
    }
}
