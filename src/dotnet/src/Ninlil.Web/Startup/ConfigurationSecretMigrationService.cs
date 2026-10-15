using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Configuration;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Startup;

/// <summary>
///     This class encrypts the secrets of <see cref="ConfigurationSecretSettings" /> that an
///     earlier version left in plaintext in the runtime configuration file. At startup it moves
///     each plaintext key into its encrypted sibling and empties the plaintext key, so the secret
///     does not stay on disk and in every backup until someone happens to save the setting again.
///     It follows <see cref="SmtpPasswordMigrationService" />.
///     <para>
///         It reads the raw file and never <c>IOptions</c>, because the options cannot tell a file
///         value from an environment variable. An environment value is never in the file, so it is
///         never touched. A failure is logged as a warning and startup goes on:
///         <see cref="ConfigurationSecretResolution" /> still accepts the plaintext value. A second
///         run finds only empty plaintext keys and changes nothing.
///     </para>
/// </summary>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file to migrate.</param>
/// <param name="dataProtectionProvider">Supplies the key the secrets are encrypted with.</param>
/// <param name="logger">Receives the outcome.</param>
public sealed class ConfigurationSecretMigrationService(
    RuntimeConfigurationOptions runtimeConfigurationOptions,
    IDataProtectionProvider dataProtectionProvider,
    ILogger<ConfigurationSecretMigrationService> logger
) : IHostedService
{
    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken) =>
        MigrateAsync(
            runtimeConfigurationOptions.ConfigurationFilePath,
            dataProtectionProvider,
            logger
        );

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    ///     This method moves every plaintext catalogued secret in the file at
    ///     <paramref name="path" /> to its encrypted key. It does nothing when the file is missing
    ///     or unparsable, or no plaintext secret is present. It never throws for a file problem.
    /// </summary>
    /// <param name="path">The runtime configuration file.</param>
    /// <param name="dataProtectionProvider">Supplies the key the secrets are encrypted with.</param>
    /// <param name="logger">Receives the outcome.</param>
    public static async Task MigrateAsync(
        string path,
        IDataProtectionProvider dataProtectionProvider,
        ILogger logger
    )
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return;

            // The configuration system accepts comments and trailing commas, so the reader does too.
            JsonNode root = JsonNode.Parse(
                await File.ReadAllTextAsync(path),
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                }
            );

            if (root is not JsonObject rootObject)
                return;

            List<PresetConfigurationSetting> changes = [];
            List<string> migrated = [];

            foreach (ConfigurationSecretSetting secret in ConfigurationSecretSettings.All)
            {
                if (
                    FindSection(rootObject, secret.SectionPath) is not JsonObject section
                    || section[secret.PlainKey] is not JsonValue valueNode
                    || !valueNode.TryGetValue(out string plaintext)
                    || string.IsNullOrEmpty(plaintext)
                )
                    continue;

                changes.Add(
                    new PresetConfigurationSetting(
                        secret.SectionPath,
                        secret.EncryptedKey,
                        ConfigurationSecretSettings.Protect(
                            secret,
                            dataProtectionProvider,
                            plaintext
                        )
                    )
                );
                changes.Add(
                    new PresetConfigurationSetting(
                        secret.SectionPath,
                        secret.PlainKey,
                        string.Empty
                    )
                );
                migrated.Add(secret.DisplayName);
            }

            if (changes.Count == 0)
                return;

            await PresetConfigurationFileApplier.ApplyAsync(changes, path);

            logger.LogInformation(
                "These settings in {Path} were stored in plaintext and are now encrypted: {Settings}.",
                path,
                string.Join(", ", migrated)
            );
        }
        catch (Exception exception)
            when (exception
                    is IOException
                        or UnauthorizedAccessException
                        or JsonException
                        or InvalidDataException
                        or CryptographicException
            )
        {
            logger.LogWarning(
                exception,
                "Secrets in {Path} are stored in plaintext and could not be encrypted. They still "
                    + "work. Save them again in their settings page to encrypt them.",
                path
            );
        }
    }

    /// <summary>
    ///     This method follows a colon-separated section path through the JSON object.
    /// </summary>
    private static JsonNode FindSection(JsonObject root, string sectionPath)
    {
        JsonNode current = root;

        foreach (string segment in sectionPath.Split(':'))
        {
            if (current is not JsonObject currentObject)
                return null;

            current = currentObject[segment];
        }

        return current;
    }
}
