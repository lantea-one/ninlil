using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="RuntimeConfigurationFileSource" />, which makes a custom
///     runtime configuration file a reload-on-change configuration source. The unit tests build a
///     <see cref="ConfigurationBuilder" /> and check the source list. The host tests start a second
///     host from <see cref="NinlilDnsWebApplicationFactory.CreateSecondHost" /> whose
///     <c>NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH</c> points at a file of the
///     test's own, and check that the file's values apply at startup, that a settings
///     <c>PATCH</c> reaches <see cref="IOptionsMonitor{TOptions}" /> without a restart and that an
///     environment variable still overrides the file. The tests use <see cref="AppearanceOptions" />
///     because its only reader uses <see cref="IOptionsMonitor{TOptions}" />.
/// </summary>
[Collection(WebTestCollection.Name)]
public class RuntimeConfigurationFileSourceTests(NinlilDnsWebApplicationFactory factory)
{
    private const string FilePathVariable =
        "NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH";

    [Fact]
    public void AddIfMissing_ForTheFileAnExistingJsonSourceLoads_AddsNothing()
    {
        string directory = Directory.CreateTempSubdirectory("ninlil-runtime-file-source-").FullName;
        try
        {
            string path = Path.Combine(directory, "appsettings.json");
            ConfigurationBuilder builder = new();
            builder.AddJsonFile(
                new Microsoft.Extensions.FileProviders.PhysicalFileProvider(directory),
                "appsettings.json",
                optional: true,
                reloadOnChange: true
            );

            bool added = RuntimeConfigurationFileSource.AddIfMissing(builder.Sources, path, 0);

            Assert.False(added);
            Assert.Single(builder.Sources.OfType<JsonConfigurationSource>());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void AddIfMissing_ForAnotherFile_AddsOneOptionalReloadingSourceAtTheIndex()
    {
        string directory = Directory.CreateTempSubdirectory("ninlil-runtime-file-source-").FullName;
        try
        {
            ConfigurationBuilder builder = new();
            builder.AddInMemoryCollection();
            builder.AddInMemoryCollection();
            string path = Path.Combine(directory, "missing-directory", "custom.json");

            bool added = RuntimeConfigurationFileSource.AddIfMissing(builder.Sources, path, 1);

            Assert.True(added);
            JsonConfigurationSource source = Assert.IsType<JsonConfigurationSource>(
                builder.Sources[1]
            );
            Assert.True(source.Optional);
            Assert.True(source.ReloadOnChange);
            Assert.Equal(3, builder.Sources.Count);

            // A second call for the same file adds nothing.
            Assert.False(RuntimeConfigurationFileSource.AddIfMissing(builder.Sources, path, 1));
            Assert.Equal(3, builder.Sources.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ACustomFile_AppliesItsValuesAtStartup_ThenReloadsASettingsPatchWithoutARestart()
    {
        using TempSettingsFile file = new();
        await File.WriteAllTextAsync(
            file.Path,
            """{"DnsServer":{"Appearance":{"Palette":"Indigo"}}}"""
        );

        using EnvironmentScope scope = new((FilePathVariable, file.Path));
        using WebApplicationFactory<Program> host = factory.CreateSecondHost(_ => { });
        IOptionsMonitor<AppearanceOptions> monitor = host.Services.GetRequiredService<
            IOptionsMonitor<AppearanceOptions>
        >();

        // The file's value is already in force when the host starts.
        Assert.Equal(UiThemePaletteEnum.Indigo, monitor.CurrentValue.Palette);

        HttpClient client = await SignInAsAdminAsync(host);
        using HttpRequestMessage request = new(HttpMethod.Patch, "/api/appearance-settings")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { palette = "Amber", mode = "Dark" }),
                Encoding.UTF8,
                "application/json"
            ),
        };
        HttpResponseMessage response = await client.SendAsync(request);
        response.EnsureSuccessStatusCode();

        // The JSON provider debounces file-change notifications, so wait for the value itself.
        await WaitForAsync(() =>
            monitor.CurrentValue.Palette == UiThemePaletteEnum.Amber
            && monitor.CurrentValue.Mode == UiThemeModeEnum.Dark
        );
        Assert.Equal(UiThemePaletteEnum.Amber, monitor.CurrentValue.Palette);
        Assert.Equal(UiThemeModeEnum.Dark, monitor.CurrentValue.Mode);
    }

    [Fact]
    public void AddIfMissing_ForAFileInAMissingFolder_CreatesTheFolder()
    {
        string directory = Directory.CreateTempSubdirectory("ninlil-runtime-file-source-").FullName;
        try
        {
            string folder = Path.Combine(directory, "not-yet", "nested");
            ConfigurationBuilder builder = new();

            RuntimeConfigurationFileSource.AddIfMissing(
                builder.Sources,
                Path.Combine(folder, "custom.json"),
                0
            );

            Assert.True(Directory.Exists(folder));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ACustomFileInAMissingFolder_ReloadsTheFirstSettingsPatchWithoutARestart()
    {
        string directory = Directory.CreateTempSubdirectory("ninlil-runtime-file-source-").FullName;
        try
        {
            // The folder does not exist when the host starts; the first save creates the file.
            string path = Path.Combine(directory, "missing-folder", "settings.json");
            using EnvironmentScope scope = new((FilePathVariable, path));
            using WebApplicationFactory<Program> host = factory.CreateSecondHost(_ => { });
            IOptionsMonitor<AppearanceOptions> monitor = host.Services.GetRequiredService<
                IOptionsMonitor<AppearanceOptions>
            >();

            HttpClient client = await SignInAsAdminAsync(host);
            using HttpRequestMessage request = new(HttpMethod.Patch, "/api/appearance-settings")
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(new { palette = "Amber", mode = "Dark" }),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
            HttpResponseMessage response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();

            await WaitForAsync(() => monitor.CurrentValue.Palette == UiThemePaletteEnum.Amber);
            Assert.Equal(UiThemePaletteEnum.Amber, monitor.CurrentValue.Palette);
            Assert.Equal(UiThemeModeEnum.Dark, monitor.CurrentValue.Mode);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ACustomFile_IsOverriddenByAnEnvironmentVariable()
    {
        using TempSettingsFile file = new();
        await File.WriteAllTextAsync(
            file.Path,
            """{"DnsServer":{"Appearance":{"Palette":"Indigo","Mode":"Dark"}}}"""
        );

        using EnvironmentScope scope = new(
            (FilePathVariable, file.Path),
            ("NINLIL_DNS_SERVER_APPEARANCE_PALETTE", "Amber")
        );
        using WebApplicationFactory<Program> host = factory.CreateSecondHost(_ => { });
        AppearanceOptions options = host
            .Services.GetRequiredService<IOptionsMonitor<AppearanceOptions>>()
            .CurrentValue;

        Assert.Equal(UiThemePaletteEnum.Amber, options.Palette);
        Assert.Equal(UiThemeModeEnum.Dark, options.Mode);
    }

    private static async Task<HttpClient> SignInAsAdminAsync(WebApplicationFactory<Program> host)
    {
        HttpClient client = host.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        login.EnsureSuccessStatusCode();
        LoginResponse body = await login.Content.ReadFromJsonAsync<LoginResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            body.AccessToken
        );
        return client;
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 200 && !condition(); attempt++)
            await Task.Delay(50);
    }

    /// <summary>
    ///     This class owns a directory of its own that holds one settings file. The file watcher
    ///     behind reload-on-change reports every change in the directory it watches, so the file
    ///     does not share the temp directory with unrelated churn.
    /// </summary>
    private sealed class TempSettingsFile : IDisposable
    {
        private readonly string directory = Directory
            .CreateTempSubdirectory("ninlil-runtime-file-source-")
            .FullName;

        /// <summary>This property contains the full path of the settings file.</summary>
        public string Path => System.IO.Path.Combine(directory, "settings.json");

        /// <inheritdoc />
        public void Dispose()
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup; a leftover temporary directory is harmless.
            }
        }
    }

    /// <summary>
    ///     This class sets process environment variables for the host a test builds and restores
    ///     them afterwards. The shared <see cref="WebTestCollection" /> runs one test at a time, so
    ///     no other test reads them meanwhile.
    /// </summary>
    private sealed class EnvironmentScope : IDisposable
    {
        private readonly (string Name, string Previous)[] saved;

        /// <summary>This constructor sets each variable and remembers its old value.</summary>
        public EnvironmentScope(params (string Name, string Value)[] variables)
        {
            saved = variables
                .Select(v => (v.Name, Environment.GetEnvironmentVariable(v.Name)))
                .ToArray();
            foreach ((string name, string value) in variables)
                Environment.SetEnvironmentVariable(name, value);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            foreach ((string name, string previous) in saved)
                Environment.SetEnvironmentVariable(name, previous);
        }
    }
}
