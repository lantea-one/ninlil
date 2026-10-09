using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Wire;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;
using Ninlil.Dns.Server.Core.Resolution;
using Ninlil.Dns.Server.Forwarding.Caching;
using Ninlil.Dns.Server.Forwarding.Tests.Fakes;
using Ninlil.Dns.Server.Recursive;

// Define our namespace.
namespace Ninlil.Dns.Server.Forwarding.Tests;

/// <summary>
///     This part of <see cref="ForwarderQueryResolverTests" /> tests private zones with global
///     DNSSEC validation on: <c>corp.internal</c> and <c>corp.lan</c>, which the signed root of
///     <see cref="Ninlil.Dns.Server.Recursive.Tests.Support.SignedHierarchy" /> proves do not
///     exist, are served by the fake upstream. The tests cover the Insecure domain list, the
///     per-zone validation switch and the forwarded-response cache. They run the real validator
///     over the shared hierarchy, like the DNSSEC part.
/// </summary>
public sealed partial class ForwarderQueryResolverTests
{
    [Fact]
    public async Task Private_ACorpInternalNameThroughAConditionalZone_IsServedWithoutAd()
    {
        DnssecEnvironment environment = new();
        await SeedForwarderZoneAsync("corp.internal", "192.0.2.53");
        ForwarderQueryResolver resolver = CreateResolver(
            recursiveOptions: environment.Options,
            validator: environment.Validator,
            forwarderClients: [environment.Upstream],
            recursiveMonitor: environment.Monitor,
            insecureDomains: environment.InsecureDomains
        );

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("www.corp.internal", dnssecOk: true)
        );

        Assert.True(resolution.IsHandled);
        Assert.Equal(DnsResponseCodeEnum.NoError, resolution.Response.ResponseCode);
        Assert.Contains(resolution.Response.Answers, record => record.Type == A);
        Assert.False(resolution.Response.AuthenticData);
    }

    [Fact]
    public async Task Private_ACorpInternalNameThroughTheGlobalForwarders_IsServedWithoutAd()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("www.corp.internal", dnssecOk: true)
        );

        Assert.True(resolution.IsHandled);
        Assert.Contains(resolution.Response.Answers, record => record.Type == A);
        Assert.False(resolution.Response.AuthenticData);
    }

    [Fact]
    public async Task Private_APublicSignedNameWithTheDefaultList_IsStillSecure()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.True(response.AuthenticData);
    }

    [Fact]
    public async Task Private_AForgedAnswerForAPublicSignedName_IsStillRejected()
    {
        DnssecEnvironment environment = new();
        environment.Upstream.Tamper = message => CorruptSignature(message, A);
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task Private_ANameOutsideTheList_IsBogusAndNotHandled()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("www.corp.lan", dnssecOk: true)
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task Private_AnEntryRemovedFromTheList_MakesTheNameBogusAgain()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);
        Assert.True((await AskAsync(resolver, BuildClientQuery("www.corp.internal"))).IsHandled);

        environment.SetInsecureDomains("home.arpa");

        Assert.False((await AskAsync(resolver, BuildClientQuery("www.corp.internal"))).IsHandled);
    }

    [Fact]
    public async Task Private_AnEntryAddedToTheList_MakesTheNameInsecure()
    {
        DnssecEnvironment environment = new();
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(environment);
        Assert.False((await AskAsync(resolver, BuildClientQuery("www.corp.lan"))).IsHandled);

        environment.SetInsecureDomains("corp.lan");

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.corp.lan", dnssecOk: true)
        );
        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task Private_AnEntryRemovedFromTheList_IsNotServedFromTheResponseCache()
    {
        DnssecEnvironment environment = new();
        ForwardedResponseCache cache = new(
            new StaticOptionsMonitor<ForwardingOptions>(
                new ForwardingOptions { EnableCache = true }
            ),
            environment.Monitor,
            new RecordingEventPublisher(),
            environment.Clock
        );
        ForwarderQueryResolver resolver = await CreateDnssecResolverAsync(
            environment,
            responseCache: cache,
            forwardingOptions: new ForwardingOptions
            {
                Enable = true,
                EnableCache = true,
                PerForwarderTimeout = TimeSpan.FromSeconds(2),
            }
        );
        Assert.True((await AskAsync(resolver, BuildClientQuery("www.corp.internal"))).IsHandled);
        Assert.True((await AskAsync(resolver, BuildClientQuery("www.corp.internal"))).IsHandled);

        environment.SetInsecureDomains("home.arpa");

        Assert.False((await AskAsync(resolver, BuildClientQuery("www.corp.internal"))).IsHandled);
    }

    [Fact]
    public async Task Private_AZoneSwitchedOn_ValidatesAndAnswersASignedNameSecure()
    {
        DnssecEnvironment environment = new();
        await SeedForwarderZoneAsync("example.com", validateDnssec: true, "192.0.2.53");
        ForwarderQueryResolver resolver = CreateZoneResolver(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.True(response.AuthenticData);
    }

    [Fact]
    public async Task Private_AZoneSwitchedOn_RejectsAnUnsignedPrivateNameUnderARootProof()
    {
        DnssecEnvironment environment = new();
        await SeedForwarderZoneAsync("corp.lan", validateDnssec: true, "192.0.2.53");
        ForwarderQueryResolver resolver = CreateZoneResolver(environment);

        DnsQueryResolution resolution = await AskAsync(
            resolver,
            BuildClientQuery("www.corp.lan", dnssecOk: true)
        );

        Assert.False(resolution.IsHandled);
    }

    [Fact]
    public async Task Private_AZoneSwitchedOn_StillSkipsAListedName()
    {
        DnssecEnvironment environment = new();
        await SeedForwarderZoneAsync("corp.internal", validateDnssec: true, "192.0.2.53");
        ForwarderQueryResolver resolver = CreateZoneResolver(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.corp.internal", dnssecOk: true)
        );

        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task Private_AZoneSwitchedOff_ServesAnUnlistedPrivateNameWithoutValidation()
    {
        DnssecEnvironment environment = new();
        await SeedForwarderZoneAsync("corp.lan", validateDnssec: false, "192.0.2.53");
        ForwarderQueryResolver resolver = CreateZoneResolver(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.corp.lan", dnssecOk: true)
        );

        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task Private_AZoneSwitchedOff_NeverSetsAdEvenForASignedName()
    {
        DnssecEnvironment environment = new();
        await SeedForwarderZoneAsync("example.com", validateDnssec: false, "192.0.2.53");
        ForwarderQueryResolver resolver = CreateZoneResolver(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.example.com", dnssecOk: true)
        );

        Assert.False(response.AuthenticData);
    }

    [Fact]
    public async Task Private_AZoneSwitchedOn_WithGlobalValidationOff_DoesNotValidate()
    {
        DnssecEnvironment environment = new();
        environment.Options.EnableDnssecValidation = false;
        await SeedForwarderZoneAsync("corp.lan", validateDnssec: true, "192.0.2.53");
        ForwarderQueryResolver resolver = CreateZoneResolver(environment);

        DnsMessage response = await AskHandledAsync(
            resolver,
            BuildClientQuery("www.corp.lan", dnssecOk: true)
        );

        Assert.False(response.AuthenticData);
    }

    private ForwarderQueryResolver CreateZoneResolver(DnssecEnvironment environment) =>
        CreateResolver(
            recursiveOptions: environment.Options,
            validator: environment.Validator,
            forwarderClients: [environment.Upstream],
            recursiveMonitor: environment.Monitor,
            insecureDomains: environment.InsecureDomains
        );

    private async Task SeedForwarderZoneAsync(
        string zoneName,
        bool validateDnssec,
        params string[] forwarderAddresses
    )
    {
        using IServiceScope scope = _provider.CreateScope();
        IZoneRepository repository = scope.ServiceProvider.GetRequiredService<IZoneRepository>();

        await repository.AddAsync(
            new ZoneEntity
            {
                Id = Guid.NewGuid(),
                Name = zoneName,
                Type = ZoneTypeEnum.Forwarder,
                ForwarderAddresses = [.. forwarderAddresses],
                ValidateDnssec = validateDnssec,
                CreatedAtUtc = DateTimeOffset.UtcNow,
            }
        );

        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
    }
}
