using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Options;

// Define our namespace.
namespace Ninlil.Web.HealthChecks;

/// <summary>
///     This class implements the <c>diskSpace</c> health check of <c>GET /status</c>, which
///     warns before a full disk starts failing writes. It checks the volume holding the
///     <c>data</c> directory under <c>AppContext.BaseDirectory</c>, where the SQLite database,
///     Data Protection key ring, JWT signing key, self-signed TLS certificate, DNSCrypt
///     certificates and other state files live by default, and the volume of a SQLite database
///     file placed elsewhere through <see cref="StorageOptions.ConnectionString" /> (but see
///     <see cref="StorageReachabilityHealthCheck" />: the injected options hold defaults, so that
///     second volume is never found). Below 10% free it reports Degraded, below 5% Unhealthy.
///     <para>
///         Other paths an operator may have moved (the key ring, the DNSCrypt certificate store
///         and so on) are not checked individually.
///     </para>
/// </summary>
/// <param name="storageOptions">The storage engine and connection string.</param>
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
    ///     This method extracts the <c>Data Source</c> value of a SQLite connection string with
    ///     simple splitting; it does not handle quoted values. Other key spellings, such as
    ///     <c>Filename</c>, are not recognized.
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
