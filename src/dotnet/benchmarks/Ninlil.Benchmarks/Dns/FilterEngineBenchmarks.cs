using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Benchmarks.Seeding;
using Ninlil.Benchmarks.Storage;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Common.Wire.Records;
using Ninlil.Dns.Server.Core.Filtering;
using Ninlil.Dns.Server.Filtering;
using Ninlil.Dns.Server.Filtering.Extension;

// Define our namespace.
namespace Ninlil.Benchmarks.Dns;

/// <summary>
///     This class measures <see cref="IDnsFilterEngine" /> on the seeded data set: three
///     subscribed domain lists of 100,000 entries each (a thousand at the small scale), six
///     client groups, and the client registry. Every call is one DNS query's worth of filter
///     work, so the mean is what filtering adds to each query. Domain and response checks run
///     for an unidentified client (default group: lists 0 and 1) and for client 2, which falls
///     into two groups. Blocked names stop at a list match, allowed names scan every entry of
///     every applicable list, so allowed is the worst case.
/// </summary>
[Config(typeof(StorageConfig))]
public class FilterEngineBenchmarks : DnsPathBenchmarkBase
{
    private static readonly DnsName BlockedName = DnsName.Parse(
        BenchmarkDataSet.FilterEntryValue(0, 5)
    );
    private static readonly DnsName AllowedName = DnsName.Parse("www.example.org");

    private IDnsFilterEngine _engine;
    private DnsMessage _blockedCnameResponse;
    private DnsMessage _allowedCnameResponse;

    /// <inheritdoc />
    protected override void ConfigureServices(
        IServiceCollection services,
        IConfiguration configuration
    )
    {
        base.ConfigureServices(services, configuration);
        services.AddNinlilDnsServerFiltering(configuration);
        services.Configure<DnsFilterOptions>(options => options.Enable = true);
    }

    /// <inheritdoc />
    protected override async Task OnSetupAsync()
    {
        _engine = Database.Services.GetRequiredService<IDnsFilterEngine>();
        _blockedCnameResponse = CnameResponse(BenchmarkDataSet.FilterEntryValue(0, 5));
        _allowedCnameResponse = CnameResponse("cdn.clean.example.org");

        Expect(
            await DomainBlockedUnidentified(),
            "a listed name is denied for an unidentified client"
        );
        Expect(
            !await DomainAllowedUnidentified(),
            "an unlisted name is allowed for an unidentified client"
        );
        Expect(
            await DomainBlockedInTwoGroups(),
            "a listed name is denied for a client in two groups"
        );
        Expect(
            !await DomainAllowedInTwoGroups(),
            "an unlisted name is allowed for a client in two groups"
        );
        Expect(!await ClientUnidentified(), "an unidentified source address is allowed");
        Expect(!await ClientInTwoGroups(), "a source address of a client in two groups is allowed");
        Expect(
            await ResponseBlockedUnidentified(),
            "a CNAME to a listed name is denied for an unidentified client"
        );
        Expect(
            !await ResponseAllowedUnidentified(),
            "a CNAME to an unlisted name is allowed for an unidentified client"
        );
        Expect(
            await ResponseBlockedInTwoGroups(),
            "a CNAME to a listed name is denied for a client in two groups"
        );
        Expect(
            !await ResponseAllowedInTwoGroups(),
            "a CNAME to an unlisted name is allowed for a client in two groups"
        );
    }

    /// <summary>A listed name, unidentified client.</summary>
    [Benchmark]
    public Task<bool> DomainBlockedUnidentified() =>
        Domain(BlockedName, BenchmarkClientIdentityResolver.Unidentified);

    /// <summary>An unlisted name, unidentified client.</summary>
    [Benchmark]
    public Task<bool> DomainAllowedUnidentified() =>
        Domain(AllowedName, BenchmarkClientIdentityResolver.Unidentified);

    /// <summary>A listed name, client in two groups.</summary>
    [Benchmark]
    public Task<bool> DomainBlockedInTwoGroups() =>
        Domain(BlockedName, BenchmarkClientIdentityResolver.InTwoGroups);

    /// <summary>An unlisted name, client in two groups.</summary>
    [Benchmark]
    public Task<bool> DomainAllowedInTwoGroups() =>
        Domain(AllowedName, BenchmarkClientIdentityResolver.InTwoGroups);

    /// <summary>The client-source check (no client-source lists are seeded), unidentified client.</summary>
    [Benchmark]
    public Task<bool> ClientUnidentified() => Client(BenchmarkClientIdentityResolver.Unidentified);

    /// <summary>The client-source check, client in two groups.</summary>
    [Benchmark]
    public Task<bool> ClientInTwoGroups() => Client(BenchmarkClientIdentityResolver.InTwoGroups);

    /// <summary>A CNAME to a listed name, unidentified client.</summary>
    [Benchmark]
    public Task<bool> ResponseBlockedUnidentified() =>
        Response(_blockedCnameResponse, BenchmarkClientIdentityResolver.Unidentified);

    /// <summary>A CNAME to an unlisted name, unidentified client.</summary>
    [Benchmark]
    public Task<bool> ResponseAllowedUnidentified() =>
        Response(_allowedCnameResponse, BenchmarkClientIdentityResolver.Unidentified);

    /// <summary>A CNAME to a listed name, client in two groups.</summary>
    [Benchmark]
    public Task<bool> ResponseBlockedInTwoGroups() =>
        Response(_blockedCnameResponse, BenchmarkClientIdentityResolver.InTwoGroups);

    /// <summary>A CNAME to an unlisted name, client in two groups.</summary>
    [Benchmark]
    public Task<bool> ResponseAllowedInTwoGroups() =>
        Response(_allowedCnameResponse, BenchmarkClientIdentityResolver.InTwoGroups);

    private async Task<bool> Domain(DnsName name, IPAddress client) =>
        (await _engine.EvaluateDomainAsync(name, client)).IsDenied;

    private async Task<bool> Client(IPAddress client) =>
        (await _engine.EvaluateClientAsync(client)).IsDenied;

    private async Task<bool> Response(DnsMessage response, IPAddress client) =>
        (await _engine.EvaluateResponseAsync(response, client)).IsDenied;

    private static DnsMessage CnameResponse(string target)
    {
        DnsMessage response = new()
        {
            IsResponse = true,
            ResponseCode = DnsResponseCodeEnum.NoError,
            Questions =
            {
                new DnsQuestion(
                    DnsName.Parse("www.shop.example"),
                    DnsResourceRecordTypeEnum.A,
                    DnsClassEnum.Internet
                ),
            },
        };
        response.Answers.Add(
            new DnsResourceRecord(
                DnsName.Parse("www.shop.example"),
                DnsResourceRecordTypeEnum.Cname,
                DnsClassEnum.Internet,
                300,
                DnsRecordDataCodecRegistry.Encode(
                    DnsResourceRecordTypeEnum.Cname,
                    new CnameRecordData(DnsName.Parse(target))
                )
            )
        );
        return response;
    }
}
