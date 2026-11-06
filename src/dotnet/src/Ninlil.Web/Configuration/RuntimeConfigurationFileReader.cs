using System.Text.Json;
using System.Text.Json.Nodes;

// Define our namespace.
namespace Ninlil.Web.Configuration;

/// <summary>
///     This class reads the runtime configuration file as raw JSON. Two callers need what the file
///     holds and not what <c>IConfiguration</c> reports:
///     <see cref="Startup.ConfigurationSecretMigrationService" /> (it moves plaintext secrets into
///     their encrypted keys) and
///     <see cref="ClusterSync.SettingsClusterConfigSyncSource" /> (it sends the values this node
///     owns to a peer).
///     <para>
///         <b>Why the file and not <c>IConfiguration</c>.</b> An environment variable overrides a
///         file value on the node that has it, and the binder cannot tell the two apart. A value
///         read from <c>IConfiguration</c> would carry a pinned value to a node that has no such
///         variable. The file holds only what an operator saved or a peer synced.
///     </para>
/// </summary>
public static class RuntimeConfigurationFileReader
{
    /// <summary>
    ///     This method reads and parses the file. Comments and trailing commas are accepted, as
    ///     the configuration system accepts them. Property names match exactly, the way
    ///     <c>PresetConfigurationFileApplier</c> writes them: a lookup that ignored case could
    ///     match a hand-edited key the applier would not overwrite, and a caller that then wrote
    ///     the canonical key would leave two keys that differ only in case, which the
    ///     configuration system refuses to load.
    /// </summary>
    /// <param name="path">The runtime configuration file.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>
    ///     The root object, or <see langword="null" /> when the path is empty, the file is missing
    ///     or its root is not a JSON object.
    /// </returns>
    /// <exception cref="IOException">The file cannot be read.</exception>
    /// <exception cref="JsonException">The file is not valid JSON.</exception>
    /// <exception cref="ArgumentException">The file repeats a property name.</exception>
    public static async Task<JsonObject> ReadRootAsync(
        string path,
        CancellationToken stoppingToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        JsonNode root = JsonNode.Parse(
            await File.ReadAllTextAsync(path, stoppingToken),
            nodeOptions: null,
            documentOptions: new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }
        );

        // JsonObject builds its property table on first access, and a repeated property name
        // throws then. Touching every object here makes the failure part of the read, so a caller
        // handles one unreadable-file error and not a surprise at its first lookup.
        Validate(root);

        return root as JsonObject;
    }

    /// <summary>
    ///     This method visits every object and array below a node so each object builds its
    ///     property table.
    /// </summary>
    private static void Validate(JsonNode node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                _ = jsonObject.ContainsKey(string.Empty);

                foreach (KeyValuePair<string, JsonNode> property in jsonObject)
                    Validate(property.Value);

                break;
            case JsonArray jsonArray:
                foreach (JsonNode item in jsonArray)
                    Validate(item);

                break;
        }
    }

    /// <summary>
    ///     This method follows a colon-separated section path (for example
    ///     <c>DnsServer:ReverseProxy</c>) through the JSON object.
    /// </summary>
    /// <param name="root">The object <see cref="ReadRootAsync" /> returned.</param>
    /// <param name="sectionPath">The configuration section path.</param>
    /// <returns>The node at the path, or <see langword="null" /> when a segment is missing.</returns>
    public static JsonNode FindSection(JsonObject root, string sectionPath)
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
