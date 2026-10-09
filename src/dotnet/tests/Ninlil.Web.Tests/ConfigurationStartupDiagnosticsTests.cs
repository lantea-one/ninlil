using Microsoft.Extensions.Logging;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Server.Recursive;
using Ninlil.Web.Startup;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="ConfigurationStartupDiagnostics" /> without a host: each check
///     is called directly on hand-built options, and <c>LogWarnings</c> runs against a recording
///     fake logger to confirm the level, the text and that each finding is logged once.
/// </summary>
public class ConfigurationStartupDiagnosticsTests
{
    [Fact]
    public void DescribeIgnoredInsecureDomains_EntriesWithoutTheFlag_NamesBothSettingsAndTheVariable()
    {
        RecursiveServerOptions options = new() { InsecureDomains = ["corp.example"] };

        string message = ConfigurationStartupDiagnostics.DescribeIgnoredInsecureDomains(options);

        Assert.NotNull(message);
        Assert.Contains("DnsServer:Recursive:InsecureDomains", message);
        Assert.Contains("DnsServer:Recursive:UseCustomInsecureDomains", message);
        Assert.Contains("NINLIL_DNS_SERVER_RECURSIVE_USE_CUSTOM_INSECURE_DOMAINS", message);
    }

    [Fact]
    public void DescribeIgnoredInsecureDomains_FlagOnWithAnEmptyList_ReturnsNull()
    {
        RecursiveServerOptions options = new() { UseCustomInsecureDomains = true };

        Assert.Null(ConfigurationStartupDiagnostics.DescribeIgnoredInsecureDomains(options));
    }

    [Fact]
    public void DescribeIgnoredInsecureDomains_FlagOnWithEntries_ReturnsNull()
    {
        RecursiveServerOptions options = new()
        {
            UseCustomInsecureDomains = true,
            InsecureDomains = ["corp.example"],
        };

        Assert.Null(ConfigurationStartupDiagnostics.DescribeIgnoredInsecureDomains(options));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void DescribeIgnoredInsecureDomains_FlagOffWithOnlyBlankEntries_ReturnsNull(string entry)
    {
        RecursiveServerOptions options = new() { InsecureDomains = entry == null ? null : [entry] };

        Assert.Null(ConfigurationStartupDiagnostics.DescribeIgnoredInsecureDomains(options));
    }

    [Fact]
    public void DescribeSmtpUsernameWithoutPassword_UsernameOnly_SaysEverySendFails()
    {
        EmailSenderOptions options = new() { Username = "mailer" };

        string message = ConfigurationStartupDiagnostics.DescribeSmtpUsernameWithoutPassword(
            options
        );

        Assert.NotNull(message);
        Assert.Contains("Email:Username", message);
        Assert.Contains("Email:PasswordEncrypted", message);
        Assert.Contains("every e-mail send fails", message);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("", null, null)]
    [InlineData("mailer", "secret", null)]
    [InlineData("mailer", null, "ciphertext")]
    [InlineData(null, null, "ciphertext")]
    public void DescribeSmtpUsernameWithoutPassword_ConsistentStates_ReturnNull(
        string username,
        string password,
        string passwordEncrypted
    )
    {
        EmailSenderOptions options = new()
        {
            Username = username,
            Password = password,
            PasswordEncrypted = passwordEncrypted,
        };

        Assert.Null(ConfigurationStartupDiagnostics.DescribeSmtpUsernameWithoutPassword(options));
    }

    [Fact]
    public void LogWarnings_BothFindings_LogsEachOnceAtWarning()
    {
        RecursiveServerOptions recursive = new() { InsecureDomains = ["corp.example"] };
        EmailSenderOptions email = new() { Username = "mailer" };
        FakeLogger logger = new();

        ConfigurationStartupDiagnostics.LogWarnings(recursive, email, logger);

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry => Assert.Equal(LogLevel.Warning, entry.Level));
        Assert.Single(logger.Entries, entry => entry.Message.Contains("InsecureDomains"));
        Assert.Single(logger.Entries, entry => entry.Message.Contains("Email:Username"));
    }

    [Fact]
    public void LogWarnings_DefaultSettings_LogsNothing()
    {
        FakeLogger logger = new();

        ConfigurationStartupDiagnostics.LogWarnings(
            new RecursiveServerOptions(),
            new EmailSenderOptions(),
            logger
        );

        Assert.Empty(logger.Entries);
    }

    /// <summary>
    ///     This class is a fake <see cref="ILogger" /> that records the level and formatted text
    ///     of every message.
    /// </summary>
    private sealed class FakeLogger : ILogger
    {
        /// <summary>This property contains every message logged, in order.</summary>
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        /// <inheritdoc />
        public IDisposable BeginScope<TState>(TState state)
            where TState : notnull => null;

        /// <inheritdoc />
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc />
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter
        ) => Entries.Add((logLevel, formatter(state, exception)));
    }
}
