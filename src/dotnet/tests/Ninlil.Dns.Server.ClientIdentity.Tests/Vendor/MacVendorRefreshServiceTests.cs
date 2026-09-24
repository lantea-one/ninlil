using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Server.ClientIdentity.Vendor;
using static Ninlil.Dns.Server.ClientIdentity.Tests.Vendor.MacVendorTestSupport;

// Define our namespace.
namespace Ninlil.Dns.Server.ClientIdentity.Tests.Vendor;

/// <summary>
///     This class covers <see cref="MacVendorRefreshService" /> against a scripted HTTP handler:
///     a successful refresh swapping in and persisting a new table, a conditional follow-up
///     answered <c>304</c>, and every way a bad download must leave the loaded data untouched --
///     an error status, an HTML page, a truncated registry, a registry that shrank, and a refresh
///     where only some registries succeeded.
/// </summary>
public sealed class MacVendorRefreshServiceTests : IDisposable
{
    /// <summary>The valid MA-L content every scenario starts from.</summary>
    private static readonly string ValidMaL = BuildRegistryCsv(
        MacVendorRegistryEnum.MaL,
        MacVendorRefreshService.GetMinimumEntryCount(MacVendorRegistryEnum.MaL),
        0x100000,
        "Large Holder"
    );

    /// <summary>The valid MA-M content every scenario starts from.</summary>
    private static readonly string ValidMaM = BuildRegistryCsv(
        MacVendorRegistryEnum.MaM,
        MacVendorRefreshService.GetMinimumEntryCount(MacVendorRegistryEnum.MaM),
        0x2000000,
        "Medium Holder"
    );

    /// <summary>The valid MA-S content every scenario starts from.</summary>
    private static readonly string ValidMaS = BuildRegistryCsv(
        MacVendorRegistryEnum.MaS,
        MacVendorRefreshService.GetMinimumEntryCount(MacVendorRegistryEnum.MaS),
        0x300000000,
        "Small Holder"
    );

    /// <summary>A throwaway persisted-data path for this test.</summary>
    private readonly string _dataFilePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-mac-vendor-refresh-tests-{Guid.NewGuid():N}.snapshot.br"
    );

    /// <summary>The options every service in this test reads.</summary>
    private readonly MacVendorLookupOptions _options;

    /// <summary>The handler answering every download.</summary>
    private readonly ScriptedHandler _handler;

    /// <summary>The lookup service the refresh replaces tables in.</summary>
    private readonly MacVendorLookupService _lookup;

    /// <summary>The table loaded before any refresh; asserted unchanged after a failure.</summary>
    private readonly MacVendorTable _initialTable;

    /// <summary>
    ///     This constructor starts every test from a small, validator-free table, so the "must
    ///     retain half the loaded entries" rule measures against it rather than the full snapshot.
    /// </summary>
    public MacVendorRefreshServiceTests()
    {
        _options = new MacVendorLookupOptions
        {
            DataFilePath = _dataFilePath,
            MaLRegistryUrl = "https://registry.test/oui.csv",
            MaMRegistryUrl = "https://registry.test/mam.csv",
            MaSRegistryUrl = "https://registry.test/oui36.csv",
        };
        _handler = new ScriptedHandler(ServeValidRegistries);
        _lookup = new MacVendorLookupService(
            Options.Create(_options),
            NullLogger<MacVendorLookupService>.Instance
        );

        _initialTable = new MacVendorTable(
            [new(MacVendorRegistryEnum.MaL, 0xB827EB, "Initial Holder")],
            DateTimeOffset.UtcNow.AddDays(-30),
            MacVendorDataSourceEnum.Embedded
        );
        _lookup.Replace(_initialTable);
    }

    /// <summary>
    ///     This method answers every registry URL with its valid content, or <c>304</c> when the
    ///     request carries the matching <c>If-None-Match</c>.
    /// </summary>
    private static HttpResponseMessage ServeValidRegistries(HttpRequestMessage request)
    {
        (string content, string etag) = request.RequestUri!.AbsolutePath switch
        {
            "/oui.csv" => (ValidMaL, "\"large-1\""),
            "/mam.csv" => (ValidMaM, "\"medium-1\""),
            _ => (ValidMaS, "\"small-1\""),
        };

        if (request.Headers.IfNoneMatch.Any(tag => tag.Tag == etag))
            return new HttpResponseMessage(HttpStatusCode.NotModified);

        return Csv(content, etag);
    }

    /// <summary>
    ///     This method builds a refresh service over this test's fakes.
    /// </summary>
    private MacVendorRefreshService CreateService() =>
        new(
            new HandlerHttpClientFactory(_handler),
            new MutableOptionsMonitor<MacVendorLookupOptions>(_options),
            _lookup,
            NullLogger<MacVendorRefreshService>.Instance
        );

    /// <inheritdoc />
    public void Dispose()
    {
        File.Delete(_dataFilePath);
        File.Delete(_dataFilePath + ".state.json");
    }

    [Fact]
    public async Task RefreshNowAsync_WithValidDownloads_SwapsInAndPersistsTheNewTable()
    {
        MacVendorRefreshStatus status = await CreateService().RefreshNowAsync();

        Assert.Equal(MacVendorRefreshResultEnum.Updated, status.LastResult);
        Assert.NotNull(status.LastSuccessfulCheckUtc);
        Assert.Equal(MacVendorDataSourceEnum.Downloaded, _lookup.Current.Source);
        Assert.Equal(12_000, _lookup.Current.TotalCount);
        Assert.Equal(
            "Large Holder, Ltd.",
            _lookup.Current.Lookup(Convert.FromHexString("100005aabbcc"))
        );
        Assert.Equal("\"large-1\"", _lookup.Current.Validators[MacVendorRegistryEnum.MaL].ETag);

        // The persisted copy is what a restart would load, and it is the same table.
        MacVendorLookupService restarted = new(
            Options.Create(_options),
            NullLogger<MacVendorLookupService>.Instance
        );
        Assert.Equal(MacVendorDataSourceEnum.Downloaded, restarted.Current.Source);
        Assert.Equal(12_000, restarted.Current.TotalCount);
    }

    [Fact]
    public async Task RefreshNowAsync_WhenNothingChanged_SendsConditionalRequestsAndKeepsTheTable()
    {
        MacVendorRefreshService service = CreateService();
        await service.RefreshNowAsync();
        MacVendorTable afterFirst = _lookup.Current;
        _handler.Requests.Clear();

        MacVendorRefreshStatus status = await service.RefreshNowAsync();

        Assert.Equal(MacVendorRefreshResultEnum.NotModified, status.LastResult);
        Assert.Same(afterFirst, _lookup.Current);
        Assert.All(_handler.Requests, request => Assert.NotEmpty(request.Headers.IfNoneMatch));
        Assert.All(_handler.Requests, request => Assert.NotNull(request.Headers.IfModifiedSince));
    }

    [Fact]
    public async Task RefreshNowAsync_WhenOnlyOneRegistryChanged_CarriesTheOthersForward()
    {
        MacVendorRefreshService service = CreateService();
        await service.RefreshNowAsync();

        // MA-S now serves a new version; MA-L and MA-M stay unchanged (304).
        string newerSmall = BuildRegistryCsv(
            MacVendorRegistryEnum.MaS,
            1_500,
            0x300000000,
            "Newer Small"
        );
        _handler.Respond = request =>
            request.RequestUri!.AbsolutePath == "/oui36.csv"
                ? Csv(newerSmall, "\"small-2\"")
                : ServeValidRegistries(request);

        MacVendorRefreshStatus status = await service.RefreshNowAsync();

        Assert.Equal(MacVendorRefreshResultEnum.Updated, status.LastResult);
        Assert.Equal(10_000, _lookup.Current.Count(MacVendorRegistryEnum.MaL));
        Assert.Equal(1_500, _lookup.Current.Count(MacVendorRegistryEnum.MaS));
        Assert.Equal("\"large-1\"", _lookup.Current.Validators[MacVendorRegistryEnum.MaL].ETag);
        Assert.Equal("\"small-2\"", _lookup.Current.Validators[MacVendorRegistryEnum.MaS].ETag);
    }

    public static TheoryData<string, Func<HttpRequestMessage, HttpResponseMessage>> BadDownloads =>
        new()
        {
            { "server error", _ => new HttpResponseMessage(HttpStatusCode.InternalServerError) },
            {
                "HTML page served with 200",
                _ => Csv("<!DOCTYPE html><html><body>Maintenance</body></html>", "\"html\"")
            },
            {
                "truncated registry",
                request =>
                    request.RequestUri!.AbsolutePath == "/oui.csv"
                        ? Csv(ValidMaL[..(ValidMaL.Length / 10)], "\"large-cut\"")
                        : ServeValidRegistries(request)
            },
            {
                "mostly malformed rows",
                request =>
                    request.RequestUri!.AbsolutePath == "/mam.csv"
                        ? Csv(ValidMaM.Replace("MA-M,", "MA-X,"), "\"medium-bad\"")
                        : ServeValidRegistries(request)
            },
            {
                "only the last registry fails",
                request =>
                    request.RequestUri!.AbsolutePath == "/oui36.csv"
                        ? new HttpResponseMessage(HttpStatusCode.NotFound)
                        : ServeValidRegistries(request)
            },
            { "network failure", _ => throw new HttpRequestException("Connection refused") },
        };

    [Theory]
    [MemberData(nameof(BadDownloads))]
    public async Task RefreshNowAsync_WithABadDownload_KeepsTheLoadedTableAndWritesNothing(
        string scenario,
        Func<HttpRequestMessage, HttpResponseMessage> respond
    )
    {
        _handler.Respond = respond;

        MacVendorRefreshStatus status = await CreateService().RefreshNowAsync();

        Assert.True(status.LastResult == MacVendorRefreshResultEnum.Failed, scenario);
        Assert.NotNull(status.LastError);
        Assert.Null(status.LastSuccessfulCheckUtc);
        Assert.Same(_initialTable, _lookup.Current);
        Assert.False(File.Exists(_dataFilePath), scenario);
    }

    [Fact]
    public async Task RefreshNowAsync_RejectsARegistryThatShrankToUnderHalfOfWhatIsLoaded()
    {
        MacVendorRefreshService service = CreateService();
        await service.RefreshNowAsync();
        MacVendorTable good = _lookup.Current;

        // Load a table whose MA-L registry is three times what the server still serves, so the
        // "retain at least half" rule -- not the absolute minimum, which the download meets --
        // is what rejects the refresh.
        _lookup.Replace(
            new MacVendorTable(
                [
                    .. good.GetAssignments(MacVendorRegistryEnum.MaM),
                    .. good.GetAssignments(MacVendorRegistryEnum.MaS),
                    .. Enumerable
                        .Range(0, 30_000)
                        .Select(index => new MacVendorAssignment(
                            MacVendorRegistryEnum.MaL,
                            0x400000UL + (ulong)index,
                            "Big Holder"
                        )),
                ],
                DateTimeOffset.UtcNow,
                MacVendorDataSourceEnum.Downloaded
            )
        );
        MacVendorTable large = _lookup.Current;

        MacVendorRefreshStatus status = await service.RefreshNowAsync();

        Assert.Equal(MacVendorRefreshResultEnum.Failed, status.LastResult);
        Assert.Contains("MA-L", status.LastError);
        Assert.Same(large, _lookup.Current);
    }

    [Fact]
    public async Task Status_IsPersistedAndSchedulesARetryAfterAFailure()
    {
        _handler.Respond = _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
        MacVendorRefreshStatus failed = await CreateService().RefreshNowAsync();

        // A fresh service instance (a restart) reads the same bookkeeping back from disk.
        MacVendorRefreshService restarted = CreateService();
        Assert.Equal(MacVendorRefreshResultEnum.Failed, restarted.Status.LastResult);
        Assert.Equal(failed.LastAttemptUtc, restarted.Status.LastAttemptUtc);
        Assert.Equal(
            failed.LastAttemptUtc!.Value + MacVendorRefreshService.FailureRetryInterval,
            restarted.GetNextRefreshDueUtc()
        );
    }

    [Fact]
    public async Task GetNextRefreshDueUtc_CountsFromTheLastSuccessOrElseTheDataItself()
    {
        MacVendorRefreshService service = CreateService();

        // Never refreshed: due one interval after the loaded data was generated.
        Assert.Equal(
            _initialTable.GeneratedAtUtc + _options.RefreshInterval,
            service.GetNextRefreshDueUtc()
        );

        MacVendorRefreshStatus status = await service.RefreshNowAsync();

        Assert.Equal(
            status.LastSuccessfulCheckUtc!.Value + _options.RefreshInterval,
            service.GetNextRefreshDueUtc()
        );
    }
}
