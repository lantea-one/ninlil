using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Server.Core.Rewrites;
using Ninlil.Dns.Server.Rewrites;
using Ninlil.Dns.Server.Rewrites.Extension;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class measures <see cref="IDnsRewriteEngine.TryBuildResponseAsync" /> against the
///     seeded rewrites (200 at the large scale): an A query that a rewrite answers and one that
///     none does, from an unidentified client and from an identified one. Each call is the
///     rewrite work of one DNS query.
/// </summary>
[Config(typeof(StorageConfig))]
public class RewriteEngineBenchmarks : DnsPathBenchmarkBase
{
    private IDnsRewriteEngine _engine;
    private DnsMessage _rewrittenRequest;
    private DnsMessage _plainRequest;

    /// <inheritdoc />
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    )
    {
        base.ConfigureServices(services, configuration);
        services.AddNinlilDnsServerRewrites(configuration);
        services.Configure<DnsRewriteOptions>(options => options.Enable = true);
    }

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        _engine = Database.Services.GetRequiredService<IDnsRewriteEngine>();
        _rewrittenRequest = Request("rewrite3.home.test");
        _plainRequest = Request("www.example.org");

        foreach (
            IPAddress client in new[]
            {
                BenchmarkClientIdentityResolver.Unidentified,
                BenchmarkClientIdentityResolver.InTwoGroups,
            }
        )
        {
            DnsMessage answer = await _engine.TryBuildResponseAsync(_rewrittenRequest, client);
            Expect(
                answer is { Answers.Count: 1 }
                    && answer
                        .Answers[0]
                        .Data.AsSpan()
                        .SequenceEqual(IPAddress.Parse("192.0.2.4").GetAddressBytes()),
                "the rewritten name is answered with 192.0.2.4"
            );
            Expect(
                await _engine.TryBuildResponseAsync(_plainRequest, client) is null,
                "a name without a rewrite is not answered"
            );
        }
    }

    /// <summary>A rewritten name, unidentified client.</summary>
    [Benchmark]
    public async Task<bool> MatchUnidentified() =>
        await _engine.TryBuildResponseAsync(
            _rewrittenRequest,
            BenchmarkClientIdentityResolver.Unidentified
        )
            is not null;

    /// <summary>A rewritten name, identified client.</summary>
    [Benchmark]
    public async Task<bool> MatchIdentified() =>
        await _engine.TryBuildResponseAsync(
            _rewrittenRequest,
            BenchmarkClientIdentityResolver.InTwoGroups
        )
            is not null;

    /// <summary>A name without a rewrite, unidentified client.</summary>
    [Benchmark]
    public async Task<bool> NoMatchUnidentified() =>
        await _engine.TryBuildResponseAsync(
            _plainRequest,
            BenchmarkClientIdentityResolver.Unidentified
        )
            is not null;

    /// <summary>A name without a rewrite, identified client.</summary>
    [Benchmark]
    public async Task<bool> NoMatchIdentified() =>
        await _engine.TryBuildResponseAsync(
            _plainRequest,
            BenchmarkClientIdentityResolver.InTwoGroups
        )
            is not null;

    private static DnsMessage Request(string name) =>
        new()
        {
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse(name),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };
}
