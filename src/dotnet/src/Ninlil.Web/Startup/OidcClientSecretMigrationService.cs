using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Identity.Options;
using Ninlil.Dns.Identity.Sso;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class encrypts an SSO client secret that an operator typed into the runtime
///     configuration file in plaintext. At startup it moves <c>Identity:Sso:ClientSecret</c> into
///     <c>Identity:Sso:ClientSecretEncrypted</c>, so the plaintext does not stay on disk and in
///     every backup. It mirrors <see cref="SmtpPasswordMigrationService" />.
///     <para>
///         It reads the raw file and not <c>IOptions</c>, because the options cannot tell a file
///         value from the <c>NINLIL_IDENTITY_SSO_CLIENT_SECRET</c> environment variable. <b>An
///         environment value is never in the file, so it is never written to disk.</b> A failure
///         is logged as a warning and startup goes on:
///         <see cref="OidcClientSecretProtector.ResolveClientSecret(OidcSsoOptions)" /> still
///         accepts the plaintext value.
///     </para>
/// </summary>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file to migrate.</param>
/// <param name="dataProtectionProvider">Supplies the key the secret is encrypted with.</param>
/// <param name="logger">Receives the outcome.</param>
public sealed class OidcClientSecretMigrationService(
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<OidcClientSecretMigrationService> logger
) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        MigrateAsync(
            runtimeConfigurationOptions.ConfigurationFilePath,
            new OidcClientSecretProtector(dataProtectionProvider),
            logger
        );

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     This method moves a plaintext <c>Identity:Sso:ClientSecret</c> in the file at
    ///     <paramref name="path" /> to <c>Identity:Sso:ClientSecretEncrypted</c>. It does nothing
    ///     when the file is missing or unparsable, or the value is absent, blank or not a string.
    ///     It never throws and never logs the secret.
    /// </summary>
    /// <param name="path">The runtime configuration file.</param>
    /// <param name="protector">Encrypts the secret.</param>
    /// <param name="logger">Receives the outcome.</param>
    public static async Task MigrateAsync(
        string path,
        OidcClientSecretProtector protector,
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
                || ResolveSection(rootObject) is not JsonObject section
                || section[nameof(OidcSsoOptions.ClientSecret)] is not JsonValue secretNode
                || !secretNode.TryGetValue(out string secret)
                || string.IsNullOrEmpty(secret)
            )
                return;

            await PresetConfigurationFileApplier.ApplyAsync(
                [
                    new PresetConfigurationSetting(
                        OidcSsoOptions.SectionName,
                        nameof(OidcSsoOptions.ClientSecretEncrypted),
                        protector.Protect(secret)
                    ),
                    new PresetConfigurationSetting(
                        OidcSsoOptions.SectionName,
                        nameof(OidcSsoOptions.ClientSecret),
                        string.Empty
                    ),
                ],
                path
            );

            logger.LogInformation(
                "The SSO client secret in {Path} was stored in plaintext and is now encrypted.",
                path
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.LogWarning(
                exception,
                "The SSO client secret in {Path} is stored in plaintext and could not be "
                    + "encrypted. It still works. Save it again in the SSO settings to encrypt it.",
                path
            );
        }
    }

    /// <summary>
    ///     This method finds the <c>Identity:Sso</c> object, which is nested two levels deep
    ///     (<c>Identity</c>, then <c>Sso</c>).
    /// </summary>
    private static JsonNode ResolveSection(JsonObject root)
    {
        JsonNode node = root;

        foreach (string segment in OidcSsoOptions.SectionName.Split(':'))
        {
            node = (node as JsonObject)?[segment];

            if (node == null)
                return null;
        }

        return node;
    }
}
