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

    [Theory]
    [InlineData(
        "NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING",
        "Storage:ReadReplica:ConnectionString"
    )]
    [InlineData(
        "NINLIL_STORAGE_READ_REPLICA_STICKY_AFTER_WRITE_SECONDS",
        "Storage:ReadReplica:StickyAfterWriteSeconds"
    )]
    [InlineData("NINLIL_STORAGE_READ_REPLICA_MAX_LAG_SECONDS", "Storage:ReadReplica:MaxLagSeconds")]
    [InlineData(
        "NINLIL_STORAGE_READ_REPLICA_LAG_CHECK_INTERVAL_SECONDS",
        "Storage:ReadReplica:LagCheckIntervalSeconds"
    )]
    [InlineData(
        "NINLIL_STORAGE_POSTGRESQL_TARGET_SESSION_ATTRIBUTES",
        "Storage:PostgreSql:TargetSessionAttributes"
    )]
    [InlineData(
        "NINLIL_STORAGE_POSTGRESQL_REPLICA_TARGET_SESSION_ATTRIBUTES",
        "Storage:PostgreSql:ReplicaTargetSessionAttributes"
    )]
    [InlineData(
        "NINLIL_STORAGE_POSTGRESQL_LOAD_BALANCE_HOSTS",
        "Storage:PostgreSql:LoadBalanceHosts"
    )]
    [InlineData(
        "NINLIL_STORAGE_POSTGRESQL_HOST_RECHECK_SECONDS",
        "Storage:PostgreSql:HostRecheckSeconds"
    )]
    [InlineData("NINLIL_STORAGE_MYSQL_LOAD_BALANCE", "Storage:MySql:LoadBalance")]
    [InlineData("NINLIL_STORAGE_MYSQL_REPLICA_LOAD_BALANCE", "Storage:MySql:ReplicaLoadBalance")]
    [InlineData(
        "NINLIL_STORAGE_SQLSERVER_MULTI_SUBNET_FAILOVER",
        "Storage:SqlServer:MultiSubnetFailover"
    )]
    [InlineData("NINLIL_STORAGE_MONGODB_REPLICA_SET", "Storage:MongoDb:ReplicaSet")]
    [InlineData("NINLIL_STORAGE_MONGODB_READ_PREFERENCE", "Storage:MongoDb:ReadPreference")]
    [InlineData(
        "NINLIL_STORAGE_MONGODB_MAX_STALENESS_SECONDS",
        "Storage:MongoDb:MaxStalenessSeconds"
    )]
    [InlineData("NINLIL_STORAGE_MONGODB_WRITE_CONCERN", "Storage:MongoDb:WriteConcern")]
    [InlineData("NINLIL_STORAGE_MONGODB_JOURNAL", "Storage:MongoDb:Journal")]
    public void Roots_MapTheStorageClusterSettings(string variable, string path)
    {
        IReadOnlyDictionary<string, string> mappings = OptionsEnvironmentVariableMap.BuildMappings(
            OptionsEnvironmentVariableRegistry.Roots,
            OptionsEnvironmentVariableRegistry.StandaloneConfigurationPaths
        );

        Assert.True(mappings.TryGetValue(variable, out string actual));
        Assert.Equal(path, actual);
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
