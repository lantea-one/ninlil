// Define our namespace.
namespace Ninlil.Dns.Data.Abstractions.Options;

/// <summary>
///     This class binds to the <c>Storage:SqlServer</c> section (nested as
///     <see cref="StorageOptions.SqlServer" />, so it has no section constant of its own). It
///     holds the availability group settings that Ninlil validates and writes into the connection
///     strings. They apply only when <see cref="StorageOptions.Engine" /> is SQL Server. The read
///     connection always gets <c>ApplicationIntent=ReadOnly</c>, which is what makes a listener
///     route it to a readable secondary.
/// </summary>
public class StorageSqlServerOptions
{
    /// <summary>
    ///     This property says whether <c>MultiSubnetFailover</c> is set on both connections. It
    ///     speeds up the connection to a listener that spans several subnets. When it is false the
    ///     connection strings are used as written.
    /// </summary>
    public bool MultiSubnetFailover { get; set; }
}
