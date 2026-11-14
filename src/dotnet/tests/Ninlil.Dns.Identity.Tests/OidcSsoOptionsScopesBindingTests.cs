using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Identity.Options;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class tests how <see cref="OidcSsoOptions.Scopes" /> reads back from configuration. The
///     settings writer stores the saved list as a JSON array under <c>Identity:Sso:Scopes</c>, and
///     the configuration binder appends array items to a list that already holds items, so a saved
///     list must come back as exactly what was saved. Both binding paths the host uses are
///     covered: <c>GetSection(...).Get</c> (the authentication registration) and the options
///     registration (<c>IOptions</c>, read by the settings controller).
/// </summary>
public sealed class OidcSsoOptionsScopesBindingTests
{
    private static readonly string[] Defaults = ["openid", "profile", "email"];

    [Theory]
    [InlineData("openid", "profile", "email")]
    [InlineData("openid", "email")]
    [InlineData("openid")]
    [InlineData("email", "openid", "groups")]
    public void Get_WithASavedScopeList_ReturnsExactlyThatList(params string[] saved)
    {
        IConfiguration configuration = BuildConfiguration(saved);

        OidcSsoOptions options = configuration
            .GetSection(OidcSsoOptions.SectionName)
            .Get<OidcSsoOptions>();

        Assert.Equal(saved, options.EffectiveScopes);
    }

    [Theory]
    [InlineData("openid", "profile", "email")]
    [InlineData("openid", "email")]
    [InlineData("email", "openid", "groups")]
    public void OptionsRegistration_WithASavedScopeList_ReturnsExactlyThatList(
        params string[] saved
    )
    {
        IConfiguration configuration = BuildConfiguration(saved);
        ServiceCollection services = new();
        services.Configure<OidcSsoOptions>(configuration.GetSection(OidcSsoOptions.SectionName));
        using ServiceProvider provider = services.BuildServiceProvider();

        OidcSsoOptions options = provider.GetRequiredService<IOptions<OidcSsoOptions>>().Value;

        Assert.Equal(saved, options.EffectiveScopes);
    }

    [Fact]
    public void EffectiveScopes_WithNothingSaved_ReturnsTheThreeDefaults()
    {
        IConfiguration configuration = new ConfigurationBuilder().Build();

        OidcSsoOptions options =
            configuration.GetSection(OidcSsoOptions.SectionName).Get<OidcSsoOptions>()
            ?? new OidcSsoOptions();

        Assert.Equal(Defaults, options.EffectiveScopes);
        Assert.Equal(Defaults, new OidcSsoOptions().EffectiveScopes);
    }

    /// <summary>
    ///     This method builds configuration from a JSON document shaped like the one the settings
    ///     writer produces: <c>Identity:Sso:Scopes</c> as an array of strings.
    /// </summary>
    private static IConfiguration BuildConfiguration(string[] saved)
    {
        string json =
            "{\"Identity\":{\"Sso\":{\"Scopes\":["
            + string.Join(",", saved.Select(scope => $"\"{scope}\""))
            + "]}}}";

        return new ConfigurationBuilder()
            .AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(json)))
            .Build();
    }
}
