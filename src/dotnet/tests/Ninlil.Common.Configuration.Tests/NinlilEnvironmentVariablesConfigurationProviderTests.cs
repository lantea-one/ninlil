using Ninlil.Common.Configuration;

// Define our namespace.
namespace Ninlil.Common.Configuration.Tests;

/// <summary>
///     These tests cover <see cref="NinlilEnvironmentVariablesConfigurationProvider" /> and
///     <see cref="NinlilEnvironmentVariablesConfigurationSource" /> against the process
///     environment, since <see cref="Environment.GetEnvironmentVariables()" /> has no in-process
///     fake. Each test sets a uniquely named variable and removes it in a <c>finally</c> block, so
///     a failing test never leaks a variable into later tests.
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
        // A stock ASP.NET Core double-underscore name is ignored even when the table maps the
        // same configuration path, because it does not start with the NINLIL_ prefix.
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

        // Build() ignores its IConfigurationBuilder argument, which only exists to satisfy
        // IConfigurationSource, so passing null is safe.
        Microsoft.Extensions.Configuration.IConfigurationProvider built = source.Build(null);

        Assert.Same(built, source.Provider);
    }
}
