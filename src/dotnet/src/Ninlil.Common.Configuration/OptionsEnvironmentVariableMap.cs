using System.Collections;
using System.Reflection;

// Define our namespace.
namespace Ninlil.Common.Configuration;

/// <summary>
///     This readonly struct names one real, top-level <c>Options</c>-bound configuration class this
///     project reflects over to build its own NINLIL_ environment-variable mapping table --
///     <see cref="OptionsType" /> is the CLR type itself (its own public <c>SectionName</c>
///     constant is read directly via reflection, so the section path is never re-typed by hand
///     anywhere this struct is constructed), and <see cref="SectionNameOverride" /> is used only
///     for the rare real bound configuration type that has no <c>SectionName</c> constant of its
///     own -- today, exactly one: ASP.NET Core Identity's own built-in <c>IdentityOptions</c>,
///     bound at this project's own <c>Identity</c> section by
///     <c>ServiceCollectionIdentityExtension.IdentityOptionsSectionName</c> rather than a constant
///     declared on <c>IdentityOptions</c> itself.
/// </summary>
/// <param name="OptionsType">The real bound <c>Options</c> class (or framework type) to reflect.</param>
/// <param name="SectionNameOverride">
///     The section path to use instead of reading <paramref name="OptionsType" />'s own
///     <c>SectionName</c> constant, for a type that has none of its own. Leave <see langword="null" />
///     for every ordinary project-owned <c>Options</c> class.
/// </param>
public readonly record struct OptionsEnvironmentVariableRoot(
    Type OptionsType,
    string SectionNameOverride = null
);

/// <summary>
///     This static class builds the explicit, generated NINLIL_-environment-variable-name-to-
///     real-configuration-path mapping table this project's custom configuration provider
///     (<see cref="NinlilEnvironmentVariablesConfigurationProvider" />) consults at startup. It
///     reflects, once, over every real bound <c>Options</c> class this application registers
///     (recursing into a nested complex property -- for example
///     <c>DnsOverTlsOptions.Certificate</c>, itself a <c>TlsCertificateOptions</c> -- exactly the
///     way ASP.NET Core's own configuration binder already would), producing one NINLIL_ name per
///     real scalar (leaf) configuration key.
///     <para>
///         General algorithmic reversal of a NINLIL_ name back to its own configuration path is
///         genuinely ambiguous once both the section-hierarchy separator and the PascalCase
///         word-split both use the same single underscore character -- this is exactly why this
///         mapping is built once, explicitly, from the real property tree of every real bound
///         class, rather than attempted as a generic two-way transform.
///     </para>
///     <para>
///         <b>A list-valued (or other, non-string <see cref="IEnumerable" />) property is
///         deliberately excluded from this mapping</b>, and left reachable only through the
///         configuration file or this project's own REST API -- ASP.NET Core's own configuration
///         binder represents a list as a run of index-suffixed keys (<c>Path:0</c>, <c>Path:1</c>,
///         ...) of unbounded, run-time-only-known length, which has no fixed, finite NINLIL_ name
///         to assign at reflection time. Every real list-valued setting in this project (allowed
///         DNSSEC algorithms, ACME contact emails, a reverse proxy's own known-proxy list, and
///         so on) already has full REST/UI coverage of its own, so
///         this is a deliberate, low-cost scope cut, not a silent gap.
///     </para>
/// </summary>
public static class OptionsEnvironmentVariableMap
{
    /// <summary>
    ///     This constant bounds how many nested complex-property levels
    ///     <see cref="CollectLeafConfigurationPaths" /> will recurse into before giving up --
    ///     purely a defensive guard against a future, accidentally self-referential
    ///     <c>Options</c> class turning into an infinite loop; no real class in this project comes
    ///     anywhere close to it (the deepest real nesting today is two levels, a transport's own
    ///     <c>Options</c> class down to its <c>Certificate</c> property's own
    ///     <c>TlsCertificateOptions</c>).
    /// </summary>
    private const int MaxRecursionDepth = 8;

    /// <summary>
    ///     This method reflects over every root in <paramref name="roots" /> and every nested
    ///     complex property it reaches, building the full NINLIL_-name-to-configuration-path
    ///     mapping table.
    /// </summary>
    /// <param name="roots">Every real, top-level bound <c>Options</c> class to reflect.</param>
    /// <param name="standaloneConfigurationPaths">
    ///     Every real configuration path this application reads directly by string key (via
    ///     <see cref="Microsoft.Extensions.Configuration.IConfiguration" />'s own indexer) rather than through a bound
    ///     <c>Options</c> class -- today, exactly the two bootstrap-administrator credential keys
    ///     <c>AdministratorBootstrapper</c> reads directly (<c>Identity:BootstrapAdministratorUserName</c>/
    ///     <c>...Password</c>), since a one-time bootstrap credential has no real reason to be a
    ///     bound, injectable <c>Options</c> class of its own. Optional; omit if there are none.
    /// </param>
    /// <returns>
    ///     A dictionary, keyed by the generated <c>NINLIL_...</c> environment variable name,
    ///     whose value is the real internal configuration path (for example
    ///     <c>DnsServer:Web:HttpPort</c>) it should be written to.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if two different real configuration paths would generate the exact same NINLIL_
    ///     name -- a genuine naming collision that must be caught and fixed by hand (most likely by
    ///     renaming one of the colliding C# properties) rather than silently letting one setting
    ///     shadow the other at runtime.
    /// </exception>
    public static IReadOnlyDictionary<string, string> BuildMappings(
        IEnumerable<OptionsEnvironmentVariableRoot> roots,
        IEnumerable<string> standaloneConfigurationPaths = null
    )
    {
        Dictionary<string, string> mapping = new(StringComparer.Ordinal);

        foreach (OptionsEnvironmentVariableRoot root in roots)
        {
            string sectionPath = root.SectionNameOverride ?? ReadSectionName(root.OptionsType);
            CollectLeafConfigurationPaths(root.OptionsType, sectionPath, mapping, [], 0);
        }

        foreach (string standalonePath in standaloneConfigurationPaths ?? [])
            RegisterLeaf(standalonePath, mapping);

        return mapping;
    }

    /// <summary>
    ///     This method reads a bound <c>Options</c> class's own public, static, constant
    ///     <c>SectionName</c> field via reflection, so the section path this mapping generator
    ///     uses can never drift from the one the application's own <c>Configure&lt;T&gt;</c>/
    ///     <c>GetSection(...)</c> call site actually binds against.
    /// </summary>
    private static string ReadSectionName(Type optionsType)
    {
        FieldInfo field = optionsType.GetField(
            "SectionName",
            BindingFlags.Public | BindingFlags.Static
        );

        if (field is null || field.FieldType != typeof(string))
            throw new InvalidOperationException(
                $"'{optionsType.FullName}' has no public const string SectionName field -- pass a "
                    + $"{nameof(OptionsEnvironmentVariableRoot.SectionNameOverride)} for this type instead."
            );

        return (string)field.GetValue(null);
    }

    /// <summary>
    ///     This method walks every public, readable-and-writable instance property of
    ///     <paramref name="type" />, recording one mapping entry for each scalar (leaf) property it
    ///     finds and recursing into every nested complex-object property.
    /// </summary>
    private static void CollectLeafConfigurationPaths(
        Type type,
        string configurationPath,
        Dictionary<string, string> mapping,
        HashSet<Type> typesOnCurrentPath,
        int depth
    )
    {
        // Guard against an unexpectedly deep or genuinely circular property graph -- neither
        // occurs in any real class today, but silently returning here (rather than recursing
        // forever) keeps a future mistake a merely-incomplete mapping instead of a stack overflow.
        if (depth > MaxRecursionDepth || !typesOnCurrentPath.Add(type))
            return;

        foreach (
            PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        )
        {
            // Only a property with both a public getter and a public setter is ever actually
            // bindable by ASP.NET Core's own configuration binder -- skip anything else (a
            // read-only computed property, an indexer) since it was never a real configuration key
            // to begin with.
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
                continue;

            string propertyPath = $"{configurationPath}:{property.Name}";
            Type propertyType =
                Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            if (IsLeafType(propertyType))
            {
                RegisterLeaf(propertyPath, mapping);
            }
            else if (IsUnsupportedCollectionType(propertyType))
            {
                // Deliberately skipped -- see this class's own doc comment for why a list-valued
                // setting has no fixed NINLIL_ name to assign.
            }
            else if (propertyType.IsClass)
            {
                CollectLeafConfigurationPaths(
                    propertyType,
                    propertyPath,
                    mapping,
                    typesOnCurrentPath,
                    depth + 1
                );
            }
        }

        // Remove this type from the current recursion path (not from the mapping) once every one
        // of its own properties has been visited, so a type reused by two unrelated sibling
        // properties (for example TlsCertificateOptions, nested under four different transports'
        // own root Options classes) is still fully reflected under each one.
        typesOnCurrentPath.Remove(type);
    }

    /// <summary>
    ///     This method adds one NINLIL_ name -&gt; configuration path mapping entry, throwing
    ///     immediately if the generated name would collide with a different, already-registered
    ///     configuration path.
    /// </summary>
    private static void RegisterLeaf(string configurationPath, Dictionary<string, string> mapping)
    {
        string environmentVariableName = NinlilEnvironmentVariableNaming.ToEnvironmentVariableName(
            configurationPath
        );

        if (
            mapping.TryGetValue(environmentVariableName, out string existingConfigurationPath)
            && existingConfigurationPath != configurationPath
        )
            throw new InvalidOperationException(
                $"NINLIL_ environment variable name collision: '{environmentVariableName}' would "
                    + $"map to both '{existingConfigurationPath}' and '{configurationPath}'."
            );

        mapping[environmentVariableName] = configurationPath;
    }

    /// <summary>
    ///     This method reports whether a property type is a real scalar (leaf) configuration value
    ///     -- every type ASP.NET Core's own configuration binder can already convert a single
    ///     string environment-variable value into.
    /// </summary>
    private static bool IsLeafType(Type type) =>
        type.IsPrimitive
        || type.IsEnum
        || type == typeof(string)
        || type == typeof(decimal)
        || type == typeof(Guid)
        || type == typeof(TimeSpan)
        || type == typeof(DateTime)
        || type == typeof(DateTimeOffset)
        || type == typeof(Uri);

    /// <summary>
    ///     This method reports whether a property type is a collection (anything other than
    ///     <see cref="string" /> that implements <see cref="IEnumerable" />) -- deliberately
    ///     excluded from this mapping; see this class's own doc comment.
    /// </summary>
    private static bool IsUnsupportedCollectionType(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
}
