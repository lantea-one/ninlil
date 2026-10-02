using System.Text.Json.Nodes;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class keeps the admin console's committed OpenAPI snapshot
///     (<c>src/angular/openapi/swagger.json</c>) in step with the controllers, by comparing it with
///     the document the shared in-process host serves at <c>/swagger/v1/swagger.json</c>. The
///     Angular build generates its typed API client from that file, not from a running server, so
///     a controller change that nobody re-fetched the snapshot for would otherwise ship a client
///     that no longer matches the API, with nothing failing until a page broke at runtime.
///     <para>
///         The comparison is on the parsed JSON rather than the raw text, so a difference in line
///         endings or a trailing newline never fails it; any change to a path, schema, enum or
///         example does.
///     </para>
/// </summary>
[Collection(WebTestCollection.Name)]
public class OpenApiSnapshotTests(NinlilDnsWebApplicationFactory factory)
{
    /// <summary>
    ///     This constant contains the environment variable that switches this test from checking
    ///     the snapshot to rewriting it, so regenerating it needs no running server.
    /// </summary>
    private const string UpdateVariableName = "NINLIL_UPDATE_OPENAPI_SNAPSHOT";

    private const string RegenerateCommand =
        $"{UpdateVariableName}=true dotnet test src/dotnet/tests/Ninlil.Web.Tests "
        + "--filter FullyQualifiedName~OpenApiSnapshotTests";

    [Fact]
    public async Task CommittedSnapshot_MatchesTheDocumentTheControllersProduce()
    {
        HttpClient client = factory.CreateClient();
        string served = await client.GetStringAsync("/swagger/v1/swagger.json");
        string snapshotPath = FindSnapshotPath();

        if (Environment.GetEnvironmentVariable(UpdateVariableName) == "true")
        {
            // Written as served, the same bytes `npm run fetch:openapi` would save.
            await File.WriteAllTextAsync(snapshotPath, served);
            return;
        }

        JsonNode expected = JsonNode.Parse(await File.ReadAllTextAsync(snapshotPath));
        JsonNode actual = JsonNode.Parse(served);

        Assert.True(
            JsonNode.DeepEquals(expected, actual),
            $"{snapshotPath} no longer matches the OpenAPI document the controllers produce "
                + $"(first difference at {FindFirstDifference(expected, actual, "$")}). "
                + $"Regenerate it from the repository root with `{RegenerateCommand}`, "
                + "then run `npm run generate:api` in src/angular so the API client picks it up."
        );
    }

    /// <summary>
    ///     This method finds the snapshot by walking up from the test binaries to the repository
    ///     root, so it works wherever the repository is checked out.
    /// </summary>
    private static string FindSnapshotPath()
    {
        for (
            DirectoryInfo directory = new(AppContext.BaseDirectory);
            directory is not null;
            directory = directory.Parent
        )
        {
            string candidate = Path.Combine(
                directory.FullName,
                "src",
                "angular",
                "openapi",
                "swagger.json"
            );
            if (File.Exists(candidate))
                return candidate;
        }

        throw new FileNotFoundException(
            "src/angular/openapi/swagger.json was not found above " + AppContext.BaseDirectory
        );
    }

    /// <summary>
    ///     This method names the first JSON path where the two documents differ, so a failure
    ///     points at the changed schema or operation instead of just saying "something changed".
    /// </summary>
    private static string FindFirstDifference(JsonNode expected, JsonNode actual, string path)
    {
        if (expected is JsonObject expectedObject && actual is JsonObject actualObject)
        {
            foreach (
                string key in expectedObject
                    .Select(p => p.Key)
                    .Union(actualObject.Select(p => p.Key))
            )
            {
                expectedObject.TryGetPropertyValue(key, out JsonNode expectedChild);
                actualObject.TryGetPropertyValue(key, out JsonNode actualChild);
                if (!JsonNode.DeepEquals(expectedChild, actualChild))
                    return FindFirstDifference(expectedChild, actualChild, $"{path}.{key}");
            }
        }
        else if (
            expected is JsonArray expectedArray
            && actual is JsonArray actualArray
            && expectedArray.Count == actualArray.Count
        )
        {
            for (int index = 0; index < expectedArray.Count; index++)
            {
                if (!JsonNode.DeepEquals(expectedArray[index], actualArray[index]))
                    return FindFirstDifference(
                        expectedArray[index],
                        actualArray[index],
                        $"{path}[{index}]"
                    );
            }
        }

        return path;
    }
}
