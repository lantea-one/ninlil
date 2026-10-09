using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Server.ClientSchedule.Extension;
using Ninlil.Dns.Server.Core.ClientSchedule;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class measures <see cref="IDnsClientScheduleEngine.EvaluateAsync" /> against the
///     seeded clients and schedules: an unidentified client (no database read), a client
///     without a schedule, and a client with a daily 22:00 to 23:00 UTC schedule, checked inside
///     and outside the window. The time is fixed, so the answer never changes.
/// </summary>
[Config(typeof(StorageConfig))]
public class ScheduleEngineBenchmarks : DnsPathBenchmarkBase
{
    private static readonly DateTimeOffset InsideWindow = new(
        2026,
        10,
        9,
        22,
        30,
        0,
        TimeSpan.Zero
    );
    private static readonly DateTimeOffset OutsideWindow = new(
        2026,
        10,
        9,
        12,
        0,
        0,
        TimeSpan.Zero
    );

    private IDnsClientScheduleEngine _engine;

    /// <inheritdoc />
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    )
    {
        base.ConfigureServices(services, configuration);
        services.AddNinlilDnsServerClientSchedule();
    }

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        _engine = Database.Services.GetRequiredService<IDnsClientScheduleEngine>();

        Expect(!await UnidentifiedClient(), "an unidentified client is not blocked");
        Expect(!await ClientWithoutSchedule(), "a client without a schedule is not blocked");
        Expect(
            await ClientInsideScheduleWindow(),
            "a client inside its schedule window is blocked"
        );
        Expect(
            !await ClientOutsideScheduleWindow(),
            "a client outside its schedule window is not blocked"
        );
    }

    /// <summary>An address no client owns.</summary>
    [Benchmark]
    public Task<bool> UnidentifiedClient() =>
        Blocked(BenchmarkClientIdentityResolver.Unidentified, InsideWindow);

    /// <summary>A registered client with no schedule.</summary>
    [Benchmark]
    public Task<bool> ClientWithoutSchedule() =>
        Blocked(BenchmarkClientIdentityResolver.Unscheduled, InsideWindow);

    /// <summary>A client with a schedule, inside its window.</summary>
    [Benchmark]
    public Task<bool> ClientInsideScheduleWindow() =>
        Blocked(BenchmarkClientIdentityResolver.Scheduled, InsideWindow);

    /// <summary>A client with a schedule, outside its window.</summary>
    [Benchmark]
    public Task<bool> ClientOutsideScheduleWindow() =>
        Blocked(BenchmarkClientIdentityResolver.Scheduled, OutsideWindow);

    private async Task<bool> Blocked(IPAddress client, DateTimeOffset now) =>
        (await _engine.EvaluateAsync(client, now)).IsBlocked;
}
