using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Ninlil.Dns.Identity.Email;
using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class tests <see cref="SmtpPasswordMigrationService.MigrateAsync" /> against temporary
///     JSON files: a plaintext password is moved to the encrypted key with the rest of the file
///     kept, and an already-encrypted, missing or blank password is left alone. A read-only file
///     logs a warning and does not throw. It needs no host.
/// </summary>
public sealed class SmtpPasswordMigrationTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-smtp-migration-{Guid.NewGuid():N}"
    );

    private readonly SmtpPasswordProtector _protector = new(new EphemeralDataProtectionProvider());

    public SmtpPasswordMigrationTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private string WriteFile(string json)
    {
        string path = Path.Combine(_directory, "appsettings.json");
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task APlaintextPasswordIsMovedToPasswordEncryptedAndOtherKeysAreKept()
    {
        string path = WriteFile(
            """{"Email":{"SmtpHost":"smtp.example.invalid","Password":"hunter2"},"Other":{"Key":"v"}}"""
        );

        await SmtpPasswordMigrationService.MigrateAsync(path, _protector, NullLogger.Instance);

        string text = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("hunter2", text);

        JsonObject root = (JsonObject)JsonNode.Parse(text);
        JsonObject email = (JsonObject)root["Email"];
        Assert.Equal("", email["Password"].GetValue<string>());
        Assert.Equal(
            "hunter2",
            _protector.Unprotect(email["PasswordEncrypted"].GetValue<string>())
        );
        Assert.Equal("smtp.example.invalid", email["SmtpHost"].GetValue<string>());
        Assert.Equal("v", root["Other"]["Key"].GetValue<string>());
    }

    [Theory]
    [InlineData("""{"Email":{"Password":"","PasswordEncrypted":"abc"}}""")]
    [InlineData("""{"Email":{"PasswordEncrypted":"abc"}}""")]
    [InlineData("""{"Email":{"Password":""}}""")]
    [InlineData("""{"Email":{"Password":null}}""")]
    [InlineData("""{"Other":{}}""")]
    public async Task AnAlreadyEncryptedOrBlankPasswordLeavesTheFileUntouched(string json)
    {
        string path = WriteFile(json);

        await SmtpPasswordMigrationService.MigrateAsync(path, _protector, NullLogger.Instance);

        Assert.Equal(json, await File.ReadAllTextAsync(path));
    }

    [Fact]
    public async Task AMissingFileIsNotCreated()
    {
        string path = Path.Combine(_directory, "missing.json");

        await SmtpPasswordMigrationService.MigrateAsync(path, _protector, NullLogger.Instance);

        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AFileThatCannotBeWrittenLogsAWarningAndDoesNotThrow()
    {
        if (OperatingSystem.IsWindows())
            return;

        string path = WriteFile("""{"Email":{"Password":"hunter2"}}""");
        // The atomic write creates a temporary file next to the target, so a read-only
        // directory makes the write fail while the file itself stays readable. Root ignores
        // directory permissions, so the test skips itself there.
        File.SetUnixFileMode(_directory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        try
        {
            if (CanWriteIn(_directory))
                return;

            RecordingLogger logger = new();

            await SmtpPasswordMigrationService.MigrateAsync(path, _protector, logger);

            Assert.Contains(LogLevel.Warning, logger.Levels);
            Assert.Contains("hunter2", await File.ReadAllTextAsync(path));
        }
        finally
        {
            File.SetUnixFileMode(
                _directory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
            );
        }
    }

    private static bool CanWriteIn(string directory)
    {
        try
        {
            File.WriteAllText(Path.Combine(directory, "probe"), "");
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    ///     This class is an <see cref="ILogger" /> that records the level of each entry.
    /// </summary>
    private sealed class RecordingLogger : ILogger
    {
        public List<LogLevel> Levels { get; } = [];

        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Levels.Add(logLevel);
    }
}
