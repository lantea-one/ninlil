using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the "available disk space" health check -- flagging a
///     homelab box running low on space before it starts failing writes. Checks the volume behind this host's own <c>data</c> directory (which, by
///     default, already holds the SQLite database, the Data Protection key ring, the JWT signing
///     key, the self-signed TLS certificate, and the DNSCrypt certificate store -- every one of
///     those paths defaults to somewhere under <c>AppContext.BaseDirectory/data</c>), plus the
///     volume behind a SQLite database file an operator has explicitly pointed somewhere else via
///     <see cref="StorageOptions.ConnectionString" />, if that differs.
///     <para>
///         <b>A known, deliberate scope limit</b>: this does not individually re-derive every
///         possible configurable path (an operator-overridden Data Protection key path, a
///         non-default DNSCrypt certificate store path, and so on) -- just the ones this
///         project's own defaults and <see cref="StorageOptions" /> together already establish.
///     </para>
/// </summary>
/// <param name="storageOptions">This host's own configured storage engine and connection string.</param>
public sealed class DiskSpaceHealthCheck(IOptions<StorageOptions> storageOptions) : IHealthCheck
{
    /// <summary>
    ///     This constant contains the fraction of a volume's total space that must remain free
    ///     for this check to report <see cref="HealthStatus.Healthy" />.
    /// </summary>
    private const double HealthyFreeFraction = 0.10;

    /// <summary>
    ///     This constant contains the fraction of a volume's total space that must remain free
    ///     for this check to report <see cref="HealthStatus.Degraded" /> rather than
    ///     <see cref="HealthStatus.Unhealthy" />.
    /// </summary>
    private const double DegradedFreeFraction = 0.05;

    /// <inheritdoc />
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    )
    {
        List<string> pathsToCheck = [Path.Combine(AppContext.BaseDirectory, "data")];

        if (
            storageOptions.Value.Engine == StorageEngineEnum.Sqlite
            && TryExtractSqliteDataSource(
                storageOptions.Value.ConnectionString,
                out string sqlitePath
            )
        )
        {
            string sqliteDirectory = Path.GetDirectoryName(Path.GetFullPath(sqlitePath));

            if (!string.IsNullOrWhiteSpace(sqliteDirectory))
                pathsToCheck.Add(sqliteDirectory);
        }

        List<string> problems = [];
        List<string> descriptions = [];

        foreach (string path in pathsToCheck.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            DriveInfo drive;

            try
            {
                Directory.CreateDirectory(path);
                drive = new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)) ?? path);
            }
            catch (Exception exception)
            {
                problems.Add($"{path}: could not determine free space ({exception.Message}).");
                continue;
            }

            if (!drive.IsReady)
            {
                problems.Add($"{path}: drive '{drive.Name}' is not ready.");
                continue;
            }

            double freeFraction = (double)drive.AvailableFreeSpace / drive.TotalSize;
            string description =
                $"{drive.Name} ({path}): {drive.AvailableFreeSpace / (1024.0 * 1024 * 1024):F1} GiB free of {drive.TotalSize / (1024.0 * 1024 * 1024):F1} GiB ({freeFraction:P1}).";
            descriptions.Add(description);

            if (freeFraction < DegradedFreeFraction)
                problems.Add($"CRITICAL - {description}");
            else if (freeFraction < HealthyFreeFraction)
                problems.Add($"LOW - {description}");
        }

        if (problems.Any(problem => problem.StartsWith("CRITICAL", StringComparison.Ordinal)))
            return Task.FromResult(HealthCheckResult.Unhealthy(string.Join(" ", problems)));

        if (problems.Count > 0)
            return Task.FromResult(HealthCheckResult.Degraded(string.Join(" ", problems)));

        return Task.FromResult(HealthCheckResult.Healthy(string.Join(" ", descriptions)));
    }

    /// <summary>
    ///     This method extracts a SQLite connection string's own <c>Data Source</c> value, the
    ///     minimal parsing this check needs without pulling in a full ADO.NET connection-string
    ///     builder dependency just for this.
    /// </summary>
    private static bool TryExtractSqliteDataSource(string connectionString, out string dataSource)
    {
        dataSource = null;

        if (string.IsNullOrWhiteSpace(connectionString))
            return false;

        foreach (string part in connectionString.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] keyValue = part.Split('=', 2);

            if (keyValue.Length != 2)
                continue;

            if (!keyValue[0].Trim().Equals("Data Source", StringComparison.OrdinalIgnoreCase))
                continue;

            dataSource = keyValue[1].Trim();
            return !string.IsNullOrWhiteSpace(dataSource);
        }

        return false;
    }
}
