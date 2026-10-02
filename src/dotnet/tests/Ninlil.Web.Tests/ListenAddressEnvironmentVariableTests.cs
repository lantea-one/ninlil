using Ninlil.Common.Configuration;
using Ninlil.Web.EnvironmentVariables;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks, without a host, that every listener's <c>ListenAddresses</c> setting has
///     the <c>NINLIL_</c> environment variable ENVIRONMENT.md documents for it, by building the
///     mappings from <see cref="OptionsEnvironmentVariableRegistry" />. The environment mapping
///     skips lists, which is why the setting is a comma-separated string. Setting a container's
///     listen addresses from the environment is the main way the setting is expected to be used.
/// </summary>
public class ListenAddressEnvironmentVariableTests
{
    [Theory]
    [InlineData(
        "NINLIL_DNS_SERVER_PROTOCOL_PLAIN_LISTEN_ADDRESSES",
        "DnsServer:Protocol:Plain:ListenAddresses"
    )]
    [InlineData(
        "NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_LISTEN_ADDRESSES",
        "DnsServer:Protocol:DnsOverTls:ListenAddresses"
    )]
    [InlineData(
        "NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_LISTEN_ADDRESSES",
        "DnsServer:Protocol:DnsOverQuic:ListenAddresses"
    )]
    [InlineData(
        "NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_LISTEN_ADDRESSES",
        "DnsServer:Protocol:DnsCrypt:ListenAddresses"
    )]
    [InlineData("NINLIL_DNS_SERVER_WEB_LISTEN_ADDRESSES", "DnsServer:Web:ListenAddresses")]
    public void EachListenerHasItsDocumentedEnvironmentVariable(
        string variableName,
        string configurationPath
    )
    {
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings(
            OptionsEnvironmentVariableRegistry.Roots,
            OptionsEnvironmentVariableRegistry.StandaloneConfigurationPaths
        );

        Assert.True(mappings.TryGetValue(variableName, out string path), variableName);
        Assert.Equal(configurationPath, path);
    }
}
