using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

// Define our namespace.
namespace Ninlil.Web.OpenApi;

/// <summary>
///     This class runs <see cref="XmlCrefText.Render" /> over every summary and description of the
///     published OpenAPI document, so the document and the TypeScript client generated from it name
///     endpoints and types instead of internal documentation IDs. It changes only text, never a
///     path, schema or example value. <see cref="Program" /> registers it with
///     <c>SwaggerGenOptions.DocumentFilter</c>.
///     <para>
///         It walks the document info, tags, security schemes, every operation (parameters, request
///         body, responses and their examples) and every component schema recursively (properties,
///         items, <c>allOf</c>, <c>oneOf</c>, <c>anyOf</c> and <c>additionalProperties</c>). A
///         schema that is only a reference keeps the description of the schema it points to.
///     </para>
/// </summary>
public class XmlCrefTextDocumentFilter : IDocumentFilter
{
    private static readonly Lazy<HashSet<string>> NinlilTypeNames = new(LoadNinlilTypeNames);

    /// <inheritdoc />
    public void Apply(OpenApiDocument document, DocumentFilterContext context)
    {
        Dictionary<string, string> operationsByAction = BuildOperationsByAction(
            context.ApiDescriptions
        );
        HashSet<string> typeNames = NinlilTypeNames.Value;
        string Render(string text) =>
            XmlCrefText.Render(text, operationsByAction, typeNames.Contains);

        HashSet<object> seen = new(ReferenceEqualityComparer.Instance);

        if (document.Info is not null)
            document.Info.Description = Render(document.Info.Description);

        foreach (OpenApiTag tag in document.Tags ?? Enumerable.Empty<OpenApiTag>())
            tag.Description = Render(tag.Description);

        foreach (IOpenApiPathItem pathItem in ValuesOf(document.Paths))
        {
            pathItem.Summary = Render(pathItem.Summary);
            pathItem.Description = Render(pathItem.Description);
            foreach (OpenApiOperation operation in ValuesOf(pathItem.Operations))
            {
                operation.Summary = Render(operation.Summary);
                operation.Description = Render(operation.Description);
                foreach (IOpenApiParameter parameter in operation.Parameters ?? [])
                    RewriteParameter(parameter, Render, seen);
                RewriteBody(operation.RequestBody, Render, seen);
                foreach (IOpenApiResponse response in ValuesOf(operation.Responses))
                    RewriteResponse(response, Render, seen);
            }
        }

        OpenApiComponents components = document.Components;
        if (components is null)
            return;

        foreach (IOpenApiSchema schema in ValuesOf(components.Schemas))
            RewriteSchema(schema, Render, seen);
        foreach (IOpenApiParameter parameter in ValuesOf(components.Parameters))
            RewriteParameter(parameter, Render, seen);
        foreach (IOpenApiRequestBody body in ValuesOf(components.RequestBodies))
            RewriteBody(body, Render, seen);
        foreach (IOpenApiResponse response in ValuesOf(components.Responses))
            RewriteResponse(response, Render, seen);
        foreach (IOpenApiSecurityScheme scheme in ValuesOf(components.SecuritySchemes))
        {
            if (scheme is OpenApiSecurityScheme concrete)
                concrete.Description = Render(concrete.Description);
        }
    }

    /// <summary>
    ///     This method maps each controller action that has exactly one route to
    ///     <c>HTTP-METHOD /path</c>. An action name that serves several routes or methods is left
    ///     out, so a reference to it renders as <c>Type.Member</c> instead of a guessed route.
    /// </summary>
    private static Dictionary<string, string> BuildOperationsByAction(
        IEnumerable<ApiDescription> apiDescriptions
    )
    {
        Dictionary<string, HashSet<string>> routes = [];
        foreach (ApiDescription description in apiDescriptions)
        {
            if (
                description.ActionDescriptor is not ControllerActionDescriptor action
                || description.HttpMethod is null
                || action.MethodInfo.DeclaringType is null
            )
                continue;

            string route = description.RelativePath ?? string.Empty;
            int query = route.IndexOf('?');
            if (query >= 0)
                route = route[..query];

            string key = action.MethodInfo.DeclaringType.FullName + "." + action.MethodInfo.Name;
            if (!routes.TryGetValue(key, out HashSet<string> set))
                routes[key] = set = [];
            set.Add($"{description.HttpMethod.ToUpperInvariant()} /{route}");
        }

        return routes
            .Where(pair => pair.Value.Count == 1)
            .ToDictionary(p => p.Key, p => p.Value.Single());
    }

    /// <summary>
    ///     This method collects the dotted names (nested types written with a dot, generic arity
    ///     dropped) of every type in the Ninlil assemblies the host references, loading the ones
    ///     the runtime has not loaded yet, so a reference to a type in any of them is recognised.
    /// </summary>
    private static HashSet<string> LoadNinlilTypeNames()
    {
        Dictionary<string, Assembly> assemblies = [];
        Queue<Assembly> pending = new([typeof(Program).Assembly]);
        foreach (Assembly loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (loaded.GetName().Name?.StartsWith("Ninlil.", StringComparison.Ordinal) == true)
                pending.Enqueue(loaded);
        }

        while (pending.TryDequeue(out Assembly assembly))
        {
            string name = assembly.GetName().Name;
            if (!assemblies.TryAdd(name, assembly))
                continue;

            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (
                    reference.Name?.StartsWith("Ninlil.", StringComparison.Ordinal) != true
                    || assemblies.ContainsKey(reference.Name)
                )
                    continue;
                try
                {
                    pending.Enqueue(Assembly.Load(reference));
                }
                catch (FileNotFoundException)
                {
                    // A referenced assembly that is not shipped holds no type this host documents.
                }
            }
        }

        HashSet<string> names = [];
        foreach (Assembly assembly in assemblies.Values)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                types = exception.Types.Where(type => type is not null).ToArray();
            }

            foreach (Type type in types)
            {
                // A documentation ID writes a nested type with a dot and a generic type without its
                // arity, which is how the filter's text spells them.
                string fullName = (type.FullName ?? string.Empty).Replace('+', '.');
                int tick = fullName.IndexOf('`');
                while (tick >= 0)
                {
                    int end = tick + 1;
                    while (end < fullName.Length && char.IsDigit(fullName[end]))
                        end++;
                    fullName = fullName.Remove(tick, end - tick);
                    tick = fullName.IndexOf('`');
                }
                names.Add(fullName);
            }
        }

        return names;
    }

    /// <summary>
    ///     This method returns the values of a dictionary that may be null.
    /// </summary>
    private static IEnumerable<T> ValuesOf<TKey, T>(IDictionary<TKey, T> dictionary) =>
        dictionary?.Values ?? Enumerable.Empty<T>();

    private static void RewriteParameter(
        IOpenApiParameter parameter,
        Func<string, string> render,
        HashSet<object> seen
    )
    {
        if (parameter is OpenApiParameter concrete && seen.Add(concrete))
        {
            concrete.Description = render(concrete.Description);
            RewriteSchema(concrete.Schema, render, seen);
            RewriteExamples(concrete.Examples, render);
            RewriteContent(concrete.Content, render, seen);
        }
    }

    private static void RewriteBody(
        IOpenApiRequestBody body,
        Func<string, string> render,
        HashSet<object> seen
    )
    {
        if (body is OpenApiRequestBody concrete && seen.Add(concrete))
        {
            concrete.Description = render(concrete.Description);
            RewriteContent(concrete.Content, render, seen);
        }
    }

    private static void RewriteResponse(
        IOpenApiResponse response,
        Func<string, string> render,
        HashSet<object> seen
    )
    {
        if (response is OpenApiResponse concrete && seen.Add(concrete))
        {
            concrete.Description = render(concrete.Description);
            RewriteContent(concrete.Content, render, seen);
            foreach (IOpenApiHeader header in ValuesOf(concrete.Headers))
            {
                if (header is OpenApiHeader concreteHeader)
                {
                    concreteHeader.Description = render(concreteHeader.Description);
                    RewriteSchema(concreteHeader.Schema, render, seen);
                }
            }
        }
    }

    private static void RewriteContent(
        IDictionary<string, OpenApiMediaType> content,
        Func<string, string> render,
        HashSet<object> seen
    )
    {
        foreach (OpenApiMediaType mediaType in ValuesOf(content))
        {
            RewriteSchema(mediaType.Schema, render, seen);
            RewriteExamples(mediaType.Examples, render);
        }
    }

    private static void RewriteExamples(
        IDictionary<string, IOpenApiExample> examples,
        Func<string, string> render
    )
    {
        foreach (IOpenApiExample example in ValuesOf(examples))
        {
            if (example is OpenApiExample concrete)
            {
                concrete.Summary = render(concrete.Summary);
                concrete.Description = render(concrete.Description);
            }
        }
    }

    private static void RewriteSchema(
        IOpenApiSchema schema,
        Func<string, string> render,
        HashSet<object> seen
    )
    {
        if (schema is not OpenApiSchema concrete || !seen.Add(concrete))
            return;

        concrete.Description = render(concrete.Description);
        foreach (IOpenApiSchema property in ValuesOf(concrete.Properties))
            RewriteSchema(property, render, seen);
        RewriteSchema(concrete.Items, render, seen);
        RewriteSchema(concrete.AdditionalProperties, render, seen);
        RewriteSchema(concrete.Not, render, seen);
        foreach (IOpenApiSchema part in concrete.AllOf ?? [])
            RewriteSchema(part, render, seen);
        foreach (IOpenApiSchema part in concrete.OneOf ?? [])
            RewriteSchema(part, render, seen);
        foreach (IOpenApiSchema part in concrete.AnyOf ?? [])
            RewriteSchema(part, render, seen);
    }
}
