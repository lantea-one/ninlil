using System.Reflection;
using Microsoft.AspNetCore.Identity;
using Ninlil.Common.Configuration;
using Ninlil.Dhcp.Server.Relay;
using Ninlil.Dns.Identity.Extension;
using Ninlil.Web.EnvironmentVariables;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class checks, by reflection and without a host, that
///     <see cref="OptionsEnvironmentVariableRegistry" /> lists every bound options class, so every
///     setting has its <c>NINLIL_</c> environment variable. The registry deliberately does not
///     scan assemblies (which assemblies are loaded at a given point in <c>Program.cs</c> is
///     not guaranteed). The test therefore loads every <c>Ninlil.*</c> assembly reachable from
///     <c>Ninlil.Web</c> explicitly, so a new options class that nobody added to the registry
///     fails here.
/// </summary>
public class OptionsEnvironmentVariableRegistryTests
{
    [Fact]
    public void Roots_ListEveryOptionsClassWithASectionName()
    {
        HashSet<Type> registered = OptionsEnvironmentVariableRegistry
            .Roots.Select(root => root.OptionsType)
            .ToHashSet();

        List<string> missing = LoadNinlilAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(HasSectionNameConstant)
            .Where(type => !registered.Contains(type))
            .Select(type => type.FullName)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            "Options classes with a SectionName missing from OptionsEnvironmentVariableRegistry.Roots: "
                + string.Join(", ", missing)
        );
    }

    [Fact]
    public void Roots_ListIdentityOptionsWithItsExplicitSection()
    {
        OptionsEnvironmentVariableRoot root = Assert.Single(
            OptionsEnvironmentVariableRegistry.Roots,
            r => r.OptionsType == typeof(IdentityOptions)
        );

        Assert.Equal(
            ServiceCollectionIdentityExtension.IdentityOptionsSectionName,
            root.SectionNameOverride
        );
    }

    [Fact]
    public void Roots_MapTheDhcpv6RelayUpstreamServerPort()
    {
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings(
            OptionsEnvironmentVariableRegistry.Roots,
            OptionsEnvironmentVariableRegistry.StandaloneConfigurationPaths
        );

        Assert.True(
            mappings.TryGetValue(
                "NINLIL_DHCP_SERVER_RELAY_DHCPV6_UPSTREAM_SERVER_PORT",
                out string path
            )
        );
        Assert.Equal(Dhcpv6RelayOptions.SectionName + ":UpstreamServerPort", path);
    }

    private static bool HasSectionNameConstant(Type type)
    {
        FieldInfo field = type.GetField(
            "SectionName",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.DeclaredOnly
        );
        return field != null && field.IsLiteral && field.FieldType == typeof(string);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.Where(type => type != null);
        }
    }

    private static List<Assembly> LoadNinlilAssemblies()
    {
        Dictionary<string, Assembly> loaded = new(StringComparer.Ordinal);
        Queue<Assembly> pending = new();
        pending.Enqueue(typeof(Program).Assembly);

        while (pending.Count > 0)
        {
            Assembly assembly = pending.Dequeue();
            if (!loaded.TryAdd(assembly.GetName().Name, assembly))
                continue;

            foreach (AssemblyName reference in assembly.GetReferencedAssemblies())
            {
                if (
                    reference.Name != null
                    && reference.Name.StartsWith("Ninlil.", StringComparison.Ordinal)
                )
                    pending.Enqueue(Assembly.Load(reference));
            }
        }

        return loaded.Values.ToList();
    }
}
