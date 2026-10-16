using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.IO;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Vendor;

/// <summary>
///     This class keeps the vendor table current by periodically re-downloading the three IEEE
///     registry files, and runs the same refresh on demand for the settings REST surface.
///     <para>
///         <b>A refresh is all-or-nothing, and never makes things worse.</b> Each registry is
///         requested conditionally (<c>If-None-Match</c>/<c>If-Modified-Since</c>, from the
///         validators the loaded table was downloaded with), so an unchanged file costs one empty
///         <c>304</c>. Every changed file must parse with IEEE's exact header, have at most
///         <see cref="MaximumMalformedRowFraction" /> of its rows malformed, and hold at least
///         <see cref="GetMinimumEntryCount" /> entries and at least
///         <see cref="MinimumRetainedFraction" /> of the entry count already loaded for that
///         registry. If any registry fails a check, or its download fails, the whole refresh is
///         abandoned and the loaded table stays as it was, so an HTML error page, a truncated
///         transfer or a registry that suddenly shrank cannot replace good data. Only a refresh in
///         which every registry passed or was confirmed unchanged builds a new table, which is
///         written to disk first (through a temporary file and a rename) and then swapped in.
///     </para>
///     <para>
///         <b>Scheduling.</b> A check is due once <see cref="MacVendorLookupOptions.RefreshInterval" />
///         has passed since the last successful check or, if there has never been one, since the
///         loaded data was generated, so a fresh install whose embedded snapshot is still
///         recent does not download anything yet. After a failure, the next attempt waits
///         <see cref="FailureRetryInterval" /> (or the refresh interval, if shorter) instead. The
///         bookkeeping behind that is persisted beside the data file, so a restart never triggers
///         a download on its own. The loop wakes every <see cref="TickInterval" /> and re-reads the
///         live options, so a settings change applies without a restart.
///     </para>
/// </summary>
/// <param name="httpClientFactory">The factory the registry downloads go through.</param>
/// <param name="options">The live vendor lookup options.</param>
/// <param name="lookupService">The lookup service whose table a successful refresh replaces.</param>
/// <param name="logger">The logger refresh outcomes are reported to.</param>
public sealed class MacVendorRefreshService(
    IHttpClientFactory httpClientFactory,
    IOptionsMonitor<MacVendorLookupOptions> options,
    MacVendorLookupService lookupService,
    ILogger<MacVendorRefreshService> logger
) : BackgroundService
{
    /// <summary>
    ///     This constant contains the name this service's <see cref="HttpClient" /> is registered
    ///     under.
    /// </summary>
    public const string HttpClientName = "Ninlil.Dns.Server.ClientIdentity.MacVendorRefresh";

    /// <summary>
    ///     This constant contains the largest fraction of a registry file's rows that may be
    ///     malformed before the whole file is distrusted.
    /// </summary>
    public const double MaximumMalformedRowFraction = 0.01;

    /// <summary>
    ///     This constant contains the smallest fraction of the currently loaded entry count a
    ///     registry's new download must keep, since a registry never legitimately halves between
    ///     checks.
    /// </summary>
    public const double MinimumRetainedFraction = 0.5;

    /// <summary>
    ///     This field contains how long to wait before retrying after a failed refresh.
    /// </summary>
    public static readonly TimeSpan FailureRetryInterval = TimeSpan.FromHours(6);

    /// <summary>
    ///     This field contains how often the loop wakes to decide whether a refresh is due.
    /// </summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    ///     This field contains how long after startup the first due-check waits, keeping the
    ///     download off the busy boot path.
    /// </summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    /// <summary>The serializer settings used for the persisted bookkeeping file.</summary>
    private static readonly JsonSerializerOptions StateSerializerOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>Serializes refreshes, so an on-demand refresh never overlaps a scheduled one.</summary>
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    /// <summary>The current bookkeeping; loaded lazily from disk on first use.</summary>
    private MacVendorRefreshStatus _status;

    /// <summary>
    ///     This property contains the refresh bookkeeping as of the last attempt.
    /// </summary>
    public MacVendorRefreshStatus Status => _status ??= LoadStatus();

    /// <summary>
    ///     This method returns the minimum plausible entry count for a registry: well under each
    ///     registry's size (tens of thousands of MA-L blocks, several thousand of each smaller
    ///     kind), but far above what a truncated or substituted file would hold.
    /// </summary>
    /// <param name="registry">The registry.</param>
    /// <returns>The minimum entry count a download must reach.</returns>
    public static int GetMinimumEntryCount(MacVendorRegistryEnum registry) =>
        registry == MacVendorRegistryEnum.MaL ? 10_000 : 1_000;

    /// <summary>
    ///     This method computes when the next automatic refresh is due (see the class summary for
    ///     the rule).
    /// </summary>
    /// <returns>The instant the next refresh is due; possibly already in the past.</returns>
    public DateTimeOffset GetNextRefreshDueUtc()
    {
        MacVendorRefreshStatus status = Status;
        TimeSpan interval = options.CurrentValue.RefreshInterval;
        DateTimeOffset lastCheck =
            status.LastSuccessfulCheckUtc ?? lookupService.Current.GeneratedAtUtc;

        if (
            status.LastResult == MacVendorRefreshResultEnum.Failed
            && status.LastAttemptUtc is { } lastAttempt
            && lastAttempt > lastCheck
        )
            return lastAttempt
                + (interval < FailureRetryInterval ? interval : FailureRetryInterval);

        return lastCheck + interval;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                if (
                    options.CurrentValue.EnableAutomaticRefresh
                    && DateTimeOffset.UtcNow >= GetNextRefreshDueUtc()
                )
                    await RefreshNowAsync(stoppingToken);

                await Task.Delay(TickInterval, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during a graceful shutdown.
        }
    }

    /// <summary>
    ///     This method runs one refresh immediately, regardless of schedule or of whether
    ///     automatic refresh is enabled, and waits for it to finish. A refresh already in progress
    ///     is waited for rather than overlapped.
    /// </summary>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    /// <returns>The bookkeeping as updated by this refresh.</returns>
    public async Task<MacVendorRefreshStatus> RefreshNowAsync(
        CancellationToken stoppingToken = default
    )
    {
        await _refreshLock.WaitAsync(stoppingToken);
        try
        {
            DateTimeOffset startedAtUtc = DateTimeOffset.UtcNow;
            MacVendorRefreshStatus previous = Status;

            try
            {
                MacVendorRefreshResultEnum result = await RefreshCoreAsync(stoppingToken);

                logger.LogInformation(
                    "MAC vendor registry refresh finished: {Result}; {Count} prefixes loaded.",
                    result,
                    lookupService.Current.TotalCount
                );

                return SaveStatus(
                    new MacVendorRefreshStatus(startedAtUtc, result, null, startedAtUtc)
                );
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Nothing was replaced: the loaded table stays as it was.
                logger.LogWarning(
                    exception,
                    "MAC vendor registry refresh failed; keeping the currently loaded data."
                );

                return SaveStatus(
                    previous with
                    {
                        LastAttemptUtc = startedAtUtc,
                        LastResult = MacVendorRefreshResultEnum.Failed,
                        LastError = exception.Message,
                    }
                );
            }
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <summary>
    ///     This method downloads and validates every registry, then builds, persists, and loads a
    ///     new table when anything changed. Any failure throws, leaving everything untouched.
    /// </summary>
    private async Task<MacVendorRefreshResultEnum> RefreshCoreAsync(CancellationToken stoppingToken)
    {
        MacVendorLookupOptions currentOptions = options.CurrentValue;
        MacVendorTable current = lookupService.Current;
        HttpClient client = httpClientFactory.CreateClient(HttpClientName);

        List<MacVendorAssignment> assignments = [];
        Dictionary<MacVendorRegistryEnum, MacVendorRegistryValidator> validators = [];
        bool anyChanged = false;

        foreach (MacVendorRegistryEnum registry in MacVendorRegistryExtension.All)
        {
            string url = currentOptions.GetRegistryUrl(registry);
            current.Validators.TryGetValue(registry, out MacVendorRegistryValidator validator);

            using HttpRequestMessage request = new(HttpMethod.Get, url);

            // Ask conditionally, using the validators this registry's loaded data was served with.
            if (!string.IsNullOrEmpty(validator?.ETag))
                request.Headers.TryAddWithoutValidation("If-None-Match", validator.ETag);
            if (!string.IsNullOrEmpty(validator?.LastModified))
                request.Headers.TryAddWithoutValidation(
                    "If-Modified-Since",
                    validator.LastModified
                );

            using HttpResponseMessage response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                stoppingToken
            );

            if (response.StatusCode == HttpStatusCode.NotModified)
            {
                // Unchanged: carry this registry's loaded data and validators forward as-is.
                assignments.AddRange(current.GetAssignments(registry));
                if (validator is not null)
                    validators[registry] = validator;
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"Downloading the {registry.IeeeName} registry from '{url}' failed with HTTP {(int)response.StatusCode}.",
                    null,
                    response.StatusCode
                );

            await using Stream body = await response.Content.ReadAsStreamAsync(stoppingToken);
            using StreamReader reader = new(body, Encoding.UTF8);
            MacVendorRegistryParseResult parsed = MacVendorRegistryCsvParser.Parse(
                reader,
                registry
            );

            Validate(registry, parsed, current.Count(registry));

            assignments.AddRange(parsed.Assignments);
            validators[registry] = new MacVendorRegistryValidator(
                response.Headers.ETag?.ToString(),
                response.Content.Headers.LastModified?.ToString("R")
            );
            anyChanged = true;
        }

        if (!anyChanged)
            return MacVendorRefreshResultEnum.NotModified;

        MacVendorTable table = new(
            assignments,
            DateTimeOffset.UtcNow,
            MacVendorDataSourceEnum.Downloaded,
            validators
        );

        // Persist before swapping in, so a table this server is using is always one a restart
        // will load again.
        Persist(table, currentOptions.DataFilePath);
        lookupService.Replace(table);

        return MacVendorRefreshResultEnum.Updated;
    }

    /// <summary>
    ///     This method rejects a parsed registry download that does not look like a registry file.
    /// </summary>
    /// <param name="registry">The registry that was downloaded.</param>
    /// <param name="parsed">The parse outcome.</param>
    /// <param name="currentCount">How many entries that registry currently has loaded.</param>
    /// <exception cref="InvalidDataException">Thrown when the download is implausible.</exception>
    public static void Validate(
        MacVendorRegistryEnum registry,
        MacVendorRegistryParseResult parsed,
        int currentCount
    )
    {
        if (
            parsed.RowCount == 0
            || parsed.MalformedRowCount > parsed.RowCount * MaximumMalformedRowFraction
        )
            throw new InvalidDataException(
                $"The {registry.IeeeName} registry download had {parsed.MalformedRowCount} malformed rows out of {parsed.RowCount}."
            );

        int count = parsed.Assignments.Count;
        int minimum = Math.Max(
            GetMinimumEntryCount(registry),
            (int)(currentCount * MinimumRetainedFraction)
        );

        if (count < minimum)
            throw new InvalidDataException(
                $"The {registry.IeeeName} registry download held only {count} entries (at least {minimum} expected)."
            );
    }

    /// <summary>
    ///     This method writes a table through <see cref="AtomicFile" />: a uniquely named
    ///     temporary file beside the target is renamed over it, so a process that stops mid-write
    ///     leaves the previous file in place rather than a truncated one, and two writers never
    ///     share a temporary file. An existing file keeps its mode.
    /// </summary>
    private static void Persist(MacVendorTable table, string path)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using MemoryStream stream = new();
        MacVendorSnapshotCodec.Write(stream, table);
        AtomicFile.WriteAllBytes(path, stream.ToArray(), AtomicFilePermissions.PreserveExisting);
    }

    /// <summary>
    ///     This property contains the bookkeeping file's path, beside the data file.
    /// </summary>
    private string StatePath => options.CurrentValue.DataFilePath + ".state.json";

    /// <summary>
    ///     This method records new bookkeeping in memory and on disk. A failure to write the file
    ///     is logged but never fails the refresh itself.
    /// </summary>
    private MacVendorRefreshStatus SaveStatus(MacVendorRefreshStatus status)
    {
        _status = status;

        try
        {
            string path = StatePath;
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Atomic, so a crash mid-write cannot leave a truncated file that LoadStatus would
            // discard; an existing file keeps its mode.
            AtomicFile.WriteAllBytes(
                path,
                Encoding.UTF8.GetBytes(JsonSerializer.Serialize(status, StateSerializerOptions)),
                AtomicFilePermissions.PreserveExisting
            );
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Could not persist the MAC vendor refresh status.");
        }

        return status;
    }

    /// <summary>
    ///     This method loads persisted bookkeeping, or returns <see cref="MacVendorRefreshStatus.Never" />
    ///     when there is none or it cannot be read.
    /// </summary>
    private MacVendorRefreshStatus LoadStatus()
    {
        try
        {
            string path = StatePath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<MacVendorRefreshStatus>(
                        File.ReadAllText(path),
                        StateSerializerOptions
                    ) ?? MacVendorRefreshStatus.Never;
        }
        catch (Exception exception)
            when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            logger.LogWarning(exception, "Ignoring an unreadable MAC vendor refresh status file.");
        }

        return MacVendorRefreshStatus.Never;
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        _refreshLock.Dispose();
        base.Dispose();
    }
}
