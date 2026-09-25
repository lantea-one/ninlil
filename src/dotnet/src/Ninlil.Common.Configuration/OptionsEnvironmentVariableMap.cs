using System.Collections;
using System.Reflection;

// Define our namespace.
namespace Ninlil.Common.Configuration;

/// <summary>
///     This readonly struct names one top-level bound <c>Options</c> class that
///     <see cref="OptionsEnvironmentVariableMap" /> reflects over. The section path normally comes
///     from the type's public <c>SectionName</c> constant, read by reflection so it is never typed
///     twice. <see cref="SectionNameOverride" /> covers a type with no such constant, such as
///     ASP.NET Core Identity's <c>IdentityOptions</c>, which Ninlil binds at the section named by
///     <c>ServiceCollectionIdentityExtension.IdentityOptionsSectionName</c>.
/// </summary>
/// <param name="OptionsType">The bound <c>Options</c> class (or framework type) to reflect.</param>
/// <param name="SectionNameOverride">
///     The section path to use instead of <paramref name="OptionsType" />'s <c>SectionName</c>
///     constant, for a type that has none. Leave <see langword="null" /> for Ninlil's own
///     <c>Options</c> classes.
/// </param>
public readonly record struct OptionsEnvironmentVariableRoot(
    Type OptionsType,
    string SectionNameOverride = null
);

/// <summary>
///     This static class builds the table from NINLIL_ environment variable name to configuration
///     path that <see cref="NinlilEnvironmentVariablesConfigurationProvider" /> consults at startup.
///     It reflects once over every bound <c>Options</c> class the host registers, recursing into
///     nested complex properties (for example <c>DnsOverTlsOptions.Certificate</c>, a
///     <c>TlsCertificateOptions</c>) the same way the ASP.NET Core configuration binder does. Each
///     scalar (leaf) key gets one NINLIL_ name.
///     <para>
///         A NINLIL_ name cannot be reliably turned back into a path, because the hierarchy
///         separator and the word separator are both a single underscore. That is why the table is
///         built forward from the property tree instead.
///     </para>
///     <para>
///         <b>List-valued properties (any non-string <see cref="IEnumerable" />) are left out</b>
///         and can only be set through the configuration file or the REST API. The binder stores a
///         list as index-suffixed keys (<c>Path:0</c>, <c>Path:1</c>, ...) whose count is only known
///         at run time, so there is no fixed name to assign. List settings such as allowed DNSSEC
///         algorithms, ACME contact emails and known proxies are all editable through the REST API
///         and the console.
///     </para>
/// </summary>
public static class OptionsEnvironmentVariableMap
{
    /// <summary>
    ///     This constant limits how many nested levels <see cref="CollectLeafConfigurationPaths" />
    ///     recurses into. It guards against a self-referential <c>Options</c> class; current classes
    ///     nest far less deeply (a transport's <c>Options</c> down to its <c>Certificate</c>
    ///     property).
    /// </summary>
    private const int MaxRecursionDepth = 8;

    /// <summary>
    ///     This method reflects over every root in <paramref name="roots" /> and every nested
    ///     complex property it reaches, and builds the NINLIL_ name -&gt; configuration path table.
    /// </summary>
    /// <param name="roots">Every top-level bound <c>Options</c> class to reflect.</param>
    /// <param name="standaloneConfigurationPaths">
    ///     Configuration paths the application reads directly by key through
    ///     <see cref="Microsoft.Extensions.Configuration.IConfiguration" /> instead of through a
    ///     bound <c>Options</c> class, such as the bootstrap administrator credentials that
    ///     <c>AdministratorBootstrapper</c> reads. Optional.
    /// </param>
    /// <returns>
    ///     A dictionary keyed by the generated <c>NINLIL_...</c> name, whose value is the
    ///     configuration path (for example <c>DnsServer:Web:HttpPort</c>) it writes to.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if two different configuration paths generate the same NINLIL_ name. The fix is
    ///     to rename one of the colliding C# properties; silently letting one setting shadow the
    ///     other is not acceptable.
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
    ///     This method reads a bound <c>Options</c> class's public static <c>SectionName</c>
    ///     constant by reflection, so the section path used here cannot drift from the one the
    ///     application binds with <c>Configure&lt;T&gt;</c>/<c>GetSection(...)</c>.
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
    ///     This method walks every public readable and writable instance property of
    ///     <paramref name="type" />, records a mapping entry for each scalar (leaf) property and
    ///     recurses into each nested complex-object property.
    /// </summary>
    private static void CollectLeafConfigurationPaths(
        Type type,
        string configurationPath,
        Dictionary<string, string> mapping,
        HashSet<Type> typesOnCurrentPath,
        int depth
    )
    {
        // Returning here on a too-deep or circular graph leaves the table incomplete instead of
        // overflowing the stack.
        if (depth > MaxRecursionDepth || !typesOnCurrentPath.Add(type))
            return;

        foreach (
            PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
        )
        {
            // The configuration binder only binds properties with a public getter and setter, so
            // computed read-only properties and indexers are never configuration keys.
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
                // Skipped: a list has no fixed NINLIL_ name (see the class summary).
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

        // Remove the type from the current recursion path (not from the mapping) so a type used
        // by several sibling properties, such as TlsCertificateOptions under each TLS transport's
        // Options class, is reflected under every one of them.
        typesOnCurrentPath.Remove(type);
    }

    /// <summary>
    ///     This method adds one NINLIL_ name -&gt; configuration path entry. It throws if the
    ///     generated name is already registered for a different configuration path.
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
    ///     This method reports whether a property type is a scalar (leaf) value that the
    ///     configuration binder can convert from a single string.
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
    ///     This method reports whether a property type is a collection (any non-<see cref="string" />
    ///     <see cref="IEnumerable" />). Collections are left out of the table; see the class summary.
    /// </summary>
    private static bool IsUnsupportedCollectionType(Type type) =>
        type != typeof(string) && typeof(IEnumerable).IsAssignableFrom(type);
}
