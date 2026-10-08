using System.Collections;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Ninlil.Dns.Common.IO;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This class merges <see cref="PresetConfigurationSetting" /> values into a JSON
///     configuration file on disk. Only the named section/key pairs are overwritten; every other
///     setting in the file, in the same section or another, is kept. Presets use it through
///     <see cref="PresetApplier" />, and the runtime settings controllers in <c>Ninlil.Web</c> and
///     the cluster settings sync use the settings-list overload directly.
///     <para>
///         Each apply holds a per-file lock around its read, merge and write, so concurrent
///         applies (two settings saves, or a save and a cluster sync) cannot lose each other's
///         keys. The file is parsed leniently: comments and trailing commas are accepted, but a
///         rewrite drops the comments. A file that is not a JSON object is never overwritten; the
///         apply fails instead, because replacing it would lose its content. The file keeps its
///         Unix mode, so a 0600 file stays 0600.
///     </para>
/// </summary>
public static class PresetConfigurationFileApplier
{
    /// <summary>
    ///     This method applies the <see cref="PresetDefinition.ConfigurationSettings" /> of
    ///     <paramref name="preset" /> to the JSON file at <paramref name="configurationFilePath" />,
    ///     creating the file and its directory if they do not exist.
    /// </summary>
    /// <param name="preset">The preset whose configuration settings should be applied.</param>
    /// <param name="configurationFilePath">The JSON configuration file to read from and write back to.</param>
    public static Task ApplyAsync(PresetDefinition preset, string configurationFilePath) =>
        ApplyAsync(preset.ConfigurationSettings, configurationFilePath);

    /// <summary>
    ///     This method applies a list of configuration values to the JSON file at
    ///     <paramref name="configurationFilePath" />, creating the file and its directory if they
    ///     do not exist. <see cref="ApplyAsync(PresetDefinition,string)" /> delegates here, and
    ///     callers without a preset, such as the runtime settings controllers, call it directly
    ///     so they share the same merge and atomic write.
    /// </summary>
    /// <param name="settings">Every configuration leaf to overwrite.</param>
    /// <param name="configurationFilePath">The JSON configuration file to read from and write back to.</param>
    /// <exception cref="InvalidDataException">
    ///     The existing file is not empty and is not a JSON object. The file is left as it is.
    /// </exception>
    public static async Task ApplyAsync(
        IReadOnlyList<PresetConfigurationSetting> settings,
        string configurationFilePath
    )
    {
        SemaphoreSlim fileLock = AtomicFile.GetPathLock(configurationFilePath);
        await fileLock.WaitAsync();

        try
        {
            // The whole file is read once and each setting is merged into it, creating missing
            // sections on the way, so everything the settings do not name is written back as it
            // was.
            JsonObject root = await ReadExistingRootAsync(configurationFilePath);

            foreach (PresetConfigurationSetting setting in settings)
            {
                JsonObject section = NavigateToSection(root, setting.SectionPath);
                section[setting.Key] = ToJsonNode(setting.Value);
            }

            await WriteAtomicallyAsync(configurationFilePath, root);
        }
        finally
        {
            fileLock.Release();
        }
    }

    /// <summary>
    ///     This method reads and parses the existing configuration file, or starts from an empty
    ///     object if the file does not exist yet (or is itself empty). Comments and trailing
    ///     commas are accepted, as the configuration system accepts them. Any other content,
    ///     including a top level of <c>[]</c> or <c>null</c>, throws so the caller never replaces
    ///     a file it could not understand.
    /// </summary>
    private static async Task<JsonObject> ReadExistingRootAsync(string configurationFilePath)
    {
        if (!File.Exists(configurationFilePath))
            return new JsonObject();

        string existingContent = await File.ReadAllTextAsync(configurationFilePath);

        if (string.IsNullOrWhiteSpace(existingContent))
            return new JsonObject();

        JsonNode parsed;

        try
        {
            parsed = JsonNode.Parse(
                existingContent,
                documentOptions: new JsonDocumentOptions
                {
                    CommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true,
                }
            );
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(NotAnObjectMessage(configurationFilePath), exception);
        }

        return parsed as JsonObject
            ?? throw new InvalidDataException(NotAnObjectMessage(configurationFilePath));
    }

    private static string NotAnObjectMessage(string configurationFilePath) =>
        $"'{configurationFilePath}' is not a JSON object; fix or remove it before saving settings.";

    /// <summary>
    ///     This method walks (creating, when necessary) every nested <see cref="JsonObject" />
    ///     along a colon-delimited section path, returning the innermost one.
    /// </summary>
    private static JsonObject NavigateToSection(JsonObject root, string sectionPath)
    {
        JsonObject current = root;

        foreach (string segment in sectionPath.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (current[segment] is not JsonObject child)
            {
                child = new JsonObject();
                current[segment] = child;
            }

            current = child;
        }

        return current;
    }

    /// <summary>
    ///     This method converts a setting's value into the <see cref="JsonNode" /> it is written
    ///     as. A <see langword="string" /> or <see langword="bool" /> becomes a scalar. Any other
    ///     enumerable (a list of algorithm names, or of objects such as Router Advertisement
    ///     routes) becomes a JSON array, converted element by element; <see langword="string" />
    ///     is itself enumerable, so it is matched first. Anything else, numbers and objects
    ///     included, goes through <see cref="JsonSerializer" />, because
    ///     <c>JsonValue.Create(object)</c> throws for types it does not know.
    /// </summary>
    private static JsonNode ToJsonNode(object value) =>
        value switch
        {
            null => null,
            string text => JsonValue.Create(text),
            bool flag => JsonValue.Create(flag),
            IEnumerable items => new JsonArray([.. items.Cast<object>().Select(ToJsonNode)]),
            _ => JsonSerializer.SerializeToNode(value),
        };

    /// <summary>
    ///     This method writes the merged configuration through <see cref="AtomicFile" />, so a
    ///     reader never sees a half-written file, a failed write leaves the previous file in
    ///     place and the file keeps its mode.
    /// </summary>
    private static Task WriteAtomicallyAsync(string configurationFilePath, JsonObject root) =>
        AtomicFile.WriteAllBytesAsync(
            configurationFilePath,
            JsonSerializer.SerializeToUtf8Bytes(
                root,
                new JsonSerializerOptions { WriteIndented = true }
            ),
            AtomicFilePermissions.PreserveExisting
        );
}
