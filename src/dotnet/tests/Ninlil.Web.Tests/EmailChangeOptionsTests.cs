using Microsoft.Extensions.Configuration;
using Ninlil.Common.Configuration;
using Ninlil.Dns.Identity.Options;
using Ninlil.Web.EnvironmentVariables;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks, without a host, the default of <see cref="EmailChangeOptions" /> and that
///     its documented environment variable maps onto the configuration path the options bind to.
/// </summary>
public class EmailChangeOptionsTests
{
    /// <summary>
    ///     This constant contains the environment variable ENVIRONMENT.md documents for the setting.
    /// </summary>
    private const string VariableName = "NINLIL_IDENTITY_EMAIL_CHANGE_REQUIRE_CONFIRMATION";

    [Fact]
    public void RequireConfirmation_WithNoConfiguration_IsTrue()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        EmailChangeOptions options = new();
        configuration.GetSection(EmailChangeOptions.SectionName).Bind(options);

        Assert.True(options.RequireConfirmation);
    }

    [Fact]
    public void RequireConfirmation_WithTheDocumentedVariableSetToFalse_BindsToFalse()
    {
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings(
            OptionsEnvironmentVariableRegistry.Roots,
            OptionsEnvironmentVariableRegistry.StandaloneConfigurationPaths
        );
        Assert.True(mappings.TryGetValue(VariableName, out string path), VariableName);

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string> { [path] = "false" })
            .Build();

        EmailChangeOptions options = new();
        configuration.GetSection(EmailChangeOptions.SectionName).Bind(options);

        Assert.False(options.RequireConfirmation);
    }
}
