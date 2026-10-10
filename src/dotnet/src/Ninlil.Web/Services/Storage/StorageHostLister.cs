using Microsoft.Data.SqlClient;
using MongoDB.Driver;
using MySqlConnector;
using Ninlil.Dns.Common.Enum;
using Npgsql;

// Define our namespace.
namespace Ninlil.Web.Services.Storage;

/// <summary>
///     This class reads the server addresses out of a connection string for the storage status
///     (<c>GET /api/storage</c>). It parses the string with the engine's own builder or URL class
///     and returns host names and ports only, so a password, user name or database name in the
///     string never reaches the API. A string that does not parse gives an empty list rather
///     than an error: the status must stay readable while the operator fixes a typo.
/// </summary>
public static class StorageHostLister
{
    /// <summary>
    ///     This method lists the <c>host:port</c> entries of a connection string.
    /// </summary>
    /// <param name="engine">The engine the string is for.</param>
    /// <param name="connectionString">The connection string; null or empty gives an empty list.</param>
    /// <returns>
    ///     The entries in the order the string gives them. SQLite has none (a file, not a server).
    ///     SQL Server gives its <c>Data Source</c> as written, without a <c>tcp:</c> prefix.
    /// </returns>
    public static IReadOnlyList<string> List(StorageEngineEnum engine, string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            return [];

        try
        {
            return engine switch
            {
                StorageEngineEnum.PostgreSql => ListPostgreSql(connectionString),
                StorageEngineEnum.MySql => ListMySql(connectionString),
                StorageEngineEnum.SqlServer =>
                [
                    StripTcpPrefix(new SqlConnectionStringBuilder(connectionString).DataSource),
                ],
                StorageEngineEnum.MongoDb =>
                [
                    .. MongoUrl
                        .Create(connectionString)
                        .Servers.Select(server => $"{server.Host}:{server.Port}"),
                ],
                _ => [],
            };
        }
        catch (Exception exception)
            when (exception is ArgumentException or FormatException or MongoConfigurationException)
        {
            return [];
        }
    }

    /// <summary>
    ///     This method lists the hosts of an Npgsql connection string.
    /// </summary>
    private static List<string> ListPostgreSql(string connectionString)
    {
        NpgsqlConnectionStringBuilder builder = new(connectionString);
        return WithPort(builder.Host, builder.Port);
    }

    /// <summary>
    ///     This method lists the hosts of a MySqlConnector connection string.
    /// </summary>
    private static List<string> ListMySql(string connectionString)
    {
        MySqlConnectionStringBuilder builder = new(connectionString);
        return WithPort(builder.Server, (int)builder.Port);
    }

    /// <summary>
    ///     This method splits a comma-separated host list and gives each entry a port: the one it
    ///     names (<c>host:port</c>) or the connection string's default port.
    /// </summary>
    private static List<string> WithPort(string hosts, int defaultPort)
    {
        List<string> result = [];
        foreach (string entry in (hosts ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string host = entry.Trim();

            // One colon is "host:port". More than one is an IPv6 address without a port, and a
            // bracketed address carries its own port after the closing bracket.
            bool hasPort =
                host.Count(character => character == ':') == 1
                || (host.StartsWith('[') && host.Contains("]:", StringComparison.Ordinal));
            result.Add(hasPort ? host : $"{host}:{defaultPort}");
        }

        return result;
    }

    /// <summary>
    ///     This method removes the <c>tcp:</c>, <c>np:</c> or <c>lpc:</c> protocol prefix of a SQL
    ///     Server data source.
    /// </summary>
    private static string StripTcpPrefix(string dataSource)
    {
        int colon = dataSource.IndexOf(':');
        if (colon > 0 && dataSource[..colon] is "tcp" or "np" or "lpc")
            return dataSource[(colon + 1)..];

        return dataSource;
    }
}
