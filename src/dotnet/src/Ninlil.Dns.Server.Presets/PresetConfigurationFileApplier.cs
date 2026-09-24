using System.Collections;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

// Define our namespace.
namespace Ninlil.Dns.Server.Presets;

/// <summary>
///     This class merges a <see cref="PresetDefinition" />'s own <see cref="PresetConfigurationSetting" />
///     values into a JSON configuration file on disk. Only the specific section/key leaves a
///     preset actually names are overwritten -- an unrelated setting already in the file, in the
///     same section or a different one, is left exactly as it was, per the confirmed
///     "overwrite-then-editable" preset semantics.
/// </summary>
public static class PresetConfigurationFileApplier
{
    /// <summary>
    ///     This method applies <paramref name="preset" />'s own <see cref="PresetDefinition.ConfigurationSettings" />
    ///     to the JSON file at <paramref name="configurationFilePath" />, creating the file (and
    ///     its own containing directory) if it does not already exist.
    /// </summary>
    /// <param name="preset">The preset whose configuration settings should be applied.</param>
    /// <param name="configurationFilePath">The JSON configuration file to read from and write back to.</param>
    public static Task ApplyAsync(PresetDefinition preset, string configurationFilePath) =>
        ApplyAsync(preset.ConfigurationSettings, configurationFilePath);

    /// <summary>
    ///     This method applies an arbitrary list of configuration leaves to the JSON file at
    ///     <paramref name="configurationFilePath" />, creating the file (and its own containing
    ///     directory) if it does not already exist -- the same atomic merge
    ///     <see cref="ApplyAsync(PresetDefinition,string)" /> itself delegates to, exposed directly
    ///     so a caller with no <see cref="PresetDefinition" /> of its own (for example, a REST
    ///     endpoint merging a handful of individually-supplied settings) can reuse the exact same
    ///     merge/atomic-write logic instead of duplicating it, per <see cref="PresetConfigurationSetting" />'s
    ///     own doc comment.
    /// </summary>
    /// <param name="settings">Every configuration leaf to overwrite.</param>
    /// <param name="configurationFilePath">The JSON configuration file to read from and write back to.</param>
    public static async Task ApplyAsync(
        IReadOnlyList<PresetConfigurationSetting> settings,
        string configurationFilePath
    )
    {
        // What/why: the existing file is read once, in full, before any leaf is touched -- how:
        // every setting is then merged into that same in-memory root one leaf at a time
        // (NavigateToSection creates a missing section on demand rather than requiring the file
        // to already have it), so only the specific section/key pairs a preset actually names are
        // ever overwritten -- everything else already in the file, whether in one of these same
        // sections or a completely different one, is carried through to the final write
        // untouched, per this class's own "overwrite-then-editable" doc comment above.
        JsonObject root = await ReadExistingRootAsync(configurationFilePath);

        foreach (PresetConfigurationSetting setting in settings)
        {
            JsonObject section = NavigateToSection(root, setting.SectionPath);
            section[setting.Key] = ToJsonNode(setting.Value);
        }

        await WriteAtomicallyAsync(configurationFilePath, root);
    }

    /// <summary>
    ///     This method reads and parses the existing configuration file, or starts from an empty
    ///     object if the file does not exist yet (or is itself empty).
    /// </summary>
    private static async Task<JsonObject> ReadExistingRootAsync(string configurationFilePath)
    {
        if (!File.Exists(configurationFilePath))
            return new JsonObject();

        string existingContent = await File.ReadAllTextAsync(configurationFilePath);

        if (string.IsNullOrWhiteSpace(existingContent))
            return new JsonObject();

        JsonNode parsed = JsonNode.Parse(existingContent);
        return parsed as JsonObject ?? new JsonObject();
    }

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
    ///     This method converts a single setting's own value into the <see cref="JsonNode" />
    ///     it should be written as -- a plain scalar for a <see langword="bool" />/number/
    ///     <see langword="string" />, a JSON array for anything else enumerable (a list of
    ///     algorithm names, or a list of structured objects such as a Router Advertisement's own
    ///     advertised routes, for example), since <see langword="string" /> itself is enumerable
    ///     and must be checked for first, or a JSON object for anything else -- a structured POCO
    ///     one array element (or, in principle, a whole setting) can itself be, serialized
    ///     generically via <see cref="JsonSerializer" /> rather than <c>JsonValue.Create(object)</c>'s
    ///     own narrower "known scalar CLR type" support, which throws on an arbitrary reference
    ///     type at runtime.
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
    ///     This method writes the merged configuration back to disk atomically: a temp file in
    ///     the same directory, written in full, then moved over the real path -- so a reader
    ///     never observes a half-written file, and a crash mid-write never corrupts the
    ///     previously-valid one.
    /// </summary>
    private static async Task WriteAtomicallyAsync(string configurationFilePath, JsonObject root)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(configurationFilePath));

        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        string tempPath = Path.Combine(directory ?? ".", $"{Guid.NewGuid():N}.tmp");

        await using (FileStream stream = File.Create(tempPath))
            await JsonSerializer.SerializeAsync(
                stream,
                root,
                new JsonSerializerOptions { WriteIndented = true }
            );

        File.Move(tempPath, configurationFilePath, overwrite: true);
    }
}
