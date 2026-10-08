using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Identity.Email;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class encrypts an SMTP password that an earlier version left in plaintext in the
///     runtime configuration file. At startup it moves <c>Email:Password</c> into
///     <c>Email:PasswordEncrypted</c>, so the plaintext does not stay on disk and in every backup
///     until someone happens to re-save the SMTP settings.
///     <para>
///         It reads the raw file and not <c>IOptions</c>, because the options cannot tell a file
///         value from the <c>NINLIL_EMAIL_PASSWORD</c> environment variable. An environment value
///         is never in the file, so it is never touched. A failure is logged as a warning and
///         startup goes on: <see cref="SmtpPasswordProtector.ResolvePassword" /> still accepts
///         the plaintext value.
///     </para>
/// </summary>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file to migrate.</param>
/// <param name="dataProtectionProvider">Supplies the key the password is encrypted with.</param>
/// <param name="logger">Receives the outcome.</param>
public sealed class SmtpPasswordMigrationService(
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<SmtpPasswordMigrationService> logger
) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        MigrateAsync(
            runtimeConfigurationOptions.ConfigurationFilePath,
            new SmtpPasswordProtector(dataProtectionProvider),
            logger
        );

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     This method moves a plaintext <c>Email:Password</c> in the file at
    ///     <paramref name="path" /> to <c>Email:PasswordEncrypted</c>. It does nothing when the
    ///     file is missing or unparsable, or the value is absent, blank or not a string. It never
    ///     throws.
    /// </summary>
    /// <param name="path">The runtime configuration file.</param>
    /// <param name="protector">Encrypts the password.</param>
    /// <param name="logger">Receives the outcome.</param>
    public static async Task MigrateAsync(
        string path,
        SmtpPasswordProtector protector,
        ILogger logger
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            JsonNode root = JsonNode.Parse(await File.ReadAllTextAsync(path));

            if (
                root is not JsonObject rootObject
                || rootObject[EmailSenderOptions.SectionName] is not JsonObject section
                || section[nameof(EmailSenderOptions.Password)] is not JsonValue passwordNode
                || !passwordNode.TryGetValue(out string password)
                || string.IsNullOrEmpty(password)
            )
                return;

            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        EmailSenderOptions.SectionName,
                        nameof(EmailSenderOptions.PasswordEncrypted),
                        protector.Protect(password)
                    ),
                    new PresetConfigurationSetting(
                        EmailSenderOptions.SectionName,
                        nameof(EmailSenderOptions.Password),
                        string.Empty
                    ),
                ],
                path
            );

            logger.LogInformation(
                "The SMTP password in {Path} was stored in plaintext and is now encrypted.",
                path
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(
                exception,
                "The SMTP password in {Path} is stored in plaintext and could not be encrypted. "
                    + "It still works. Save it again in the SMTP settings to encrypt it.",
                path
            );
        }
    }
}
