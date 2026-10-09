using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Identity.Sso;
using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="OidcClientSecretMigrationService.MigrateAsync" /> against
///     temporary JSON files, with no host. A plaintext <c>Identity:Sso:ClientSecret</c> in the file
///     ends as <c>ClientSecretEncrypted</c> with no plaintext anywhere in the file, and the rest
///     of the file is kept. The other direction is covered too: a file that holds only the
///     ciphertext (what an environment-supplied secret leaves behind, since the environment value
///     never reaches the file) or no secret is not touched or created, and nothing is logged
///     with the secret in it.
/// </summary>
public sealed class OidcClientSecretMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-oidc-migration-{Guid.NewGuid():N}"
    );

    private readonly OidcClientSecretProtector _protector = new(
        new EphemeralDataProtectionProvider()
    );

    public OidcClientSecretMigrationTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WriteFile(string json)
    {
        string path = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task APlaintextSecretIsMovedToClientSecretEncryptedAndNoPlaintextRemains()
    {
        string path = WriteFile(
            """{"Identity":{"Sso":{"Authority":"https://auth.example.test","ClientSecret":"s3cret-value"}},"Other":{"Key":"v"}}"""
        );
        RecordingLogger logger = new();

        await OidcClientSecretMigrationService.MigrateAsync(path, _protector, logger);

        string text = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("s3cret-value", text);
        Assert.DoesNotContain(logger.Messages, message => message.Contains("s3cret-value"));

        JsonObject sso = (JsonObject)JsonNode.Parse(text)["Identity"]["Sso"];
        Assert.Equal("", sso["ClientSecret"].GetValue<string>());
        Assert.Equal(
            "s3cret-value",
            _protector.Unprotect(sso["ClientSecretEncrypted"].GetValue<string>())
        );
        Assert.Equal("https://auth.example.test", sso["Authority"].GetValue<string>());
        Assert.Equal("v", JsonNode.Parse(text)["Other"]["Key"].GetValue<string>());
    }

    [Theory]
    [InlineData("""{"Identity":{"Sso":{"ClientSecret":"","ClientSecretEncrypted":"abc"}}}""")]
    [InlineData("""{"Identity":{"Sso":{"ClientSecretEncrypted":"abc"}}}""")]
    [InlineData("""{"Identity":{"Sso":{"ClientSecret":null}}}""")]
    [InlineData("""{"Identity":{"Sso":{"ClientSecret":5}}}""")]
    [InlineData("""{"Identity":"x"}""")]
    [InlineData("""{"Other":{}}""")]
    public async Task AnEncryptedBlankOrAbsentSecretLeavesTheFileUntouched(string json)
    {
        string path = WriteFile(json);

        await OidcClientSecretMigrationService.MigrateAsync(path, _protector, NullLogger.Instance);

        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AMissingFileIsNotCreated()
    {
        string path = Path.Combine(_directory, "missing.json");

        await OidcClientSecretMigrationService.MigrateAsync(path, _protector, NullLogger.Instance);

        Assert.False(File.Exists(path));
    }

    /// <summary>
    ///     This class is an <see cref="ILogger" /> that records the formatted text of each entry.
    /// </summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<string> Messages { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Messages.Add(formatter(state, exception));
    }
}
