using Ninlil.Common.Configuration;

// Define our namespace.
namespace Ninlil.Common.Configuration.Tests;

/// <summary>
///     These tests exercise <see cref="NinlilEnvironmentVariablesConfigurationProvider" />/
///     <see cref="NinlilEnvironmentVariablesConfigurationSource" /> directly against the real
///     process environment (there is no in-process fake for
///     <see cref="Environment.GetEnvironmentVariables()" /> -- ASP.NET Core's own built-in
///     environment-variable provider tests work exactly the same way). Every test uses its own
///     uniquely-named variable and always removes it again in a <c>finally</c> block, so a test
///     failure never leaks a stray environment variable into a later test or the wider test
///     process.
/// </summary>
public sealed class NinlilEnvironmentVariablesConfigurationProviderTests
{
    [Fact]
    public void Load_MapsARecognizedNinlilVariableOntoItsRealConfigurationPath()
    {
        const string variableName = "NINLIL_TEST_RECOGNIZED_ONE";
        Dictionary<string, string> map = new() { [variableName] = "Real:Path:One" };

        Environment.SetEnvironmentVariable(variableName, "hello");
        try
        {
            NinlilEnvironmentVariablesConfigurationProvider provider = new(map);
            provider.Load();

            Assert.True(provider.TryGet("Real:Path:One", out string value));
            Assert.Equal("hello", value);
            Assert.Empty(provider.UnrecognizedVariableNames);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void Load_IgnoresAVariableThatDoesNotStartWithTheNinlilPrefix()
    {
        const string variableName = "SOME_OTHER_TEST_VARIABLE_NAME";
        Environment.SetEnvironmentVariable(variableName, "should-be-ignored");
        try
        {
            NinlilEnvironmentVariablesConfigurationProvider provider = new(
                new Dictionary<string, string>()
            );
            provider.Load();

            Assert.False(provider.TryGet("Real:Path:One", out _));
            Assert.Empty(provider.UnrecognizedVariableNames);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void Load_RecordsAnUnrecognizedNinlilPrefixedVariableRatherThanSilentlyDroppingIt()
    {
        const string variableName = "NINLIL_TEST_TOTALLY_UNKNOWN_NAME";
        Environment.SetEnvironmentVariable(variableName, "typo-value");
        try
        {
            NinlilEnvironmentVariablesConfigurationProvider provider = new(
                new Dictionary<string, string>()
            );
            provider.Load();

            Assert.Contains(variableName, provider.UnrecognizedVariableNames);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void Load_OldDoubleUnderscoreStyleVariableIsGenuinelyInert()
    {
        // The exact style of variable the NINLIL_-only scheme retires: even if a mapping entry
        // happened to exist for the real configuration path, an old-style name is never even
        // inspected, because it never starts with the NINLIL_ prefix at all.
        const string variableName = "DnsServer__Web__HttpPort";
        Environment.SetEnvironmentVariable(variableName, "8080");
        try
        {
            Dictionary<string, string> map = new()
            {
                ["NINLIL_DNS_SERVER_WEB_HTTP_PORT"] = "DnsServer:Web:HttpPort",
            };
            NinlilEnvironmentVariablesConfigurationProvider provider = new(map);
            provider.Load();

            Assert.False(provider.TryGet("DnsServer:Web:HttpPort", out _));
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    [Fact]
    public void Source_BuildExposesTheSameProviderInstanceItReturns()
    {
        NinlilEnvironmentVariablesConfigurationSource source = new(
            new Dictionary<string, string>()
        );

        // Build() never actually reads the IConfigurationBuilder argument it is handed -- it only
        // needs one to satisfy IConfigurationSource's own signature -- so a null reference is a
        // perfectly real exercise of the real method, not a shortcut around it.
        Microsoft.Extensions.Configuration.IConfigurationProvider built = source.Build(null);

        Assert.Same(built, source.Provider);
    }
}
