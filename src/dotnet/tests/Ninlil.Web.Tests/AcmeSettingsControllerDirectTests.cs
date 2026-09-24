using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Acme;
using Ninlil.Dns.Server.Acme.Caa;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;
using Ninlil.Web.Options;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers <see cref="AcmeSettingsController" />'s real per-domain certificate
///     inventory (<see cref="AcmeSettingsController.Certificates" />) and manual "issue/renew now"
///     trigger (<see cref="AcmeSettingsController.IssueOrRenewNow" />), constructing the controller
///     directly rather than driving it through <c>WebApplicationFactory</c> over real HTTP the way
///     <see cref="AcmeSettingsControllerTests" /> does for <c>Get</c>/<c>Patch</c>.
///     <para>
///         <b>Why this class exists separately, not folded into <see cref="AcmeSettingsControllerTests" />:</b>
///         both actions read <see cref="AcmeOptions" /> live via
///         <see cref="IOptionsMonitor{TOptions}" />, and each test here needs it already reporting a
///         specific value (a particular set of configured domains; an unreachable
///         <see cref="AcmeOptions.DirectoryUri" />) the moment the action runs. Driving that through
///         a real <c>PATCH /api/acme-settings</c> against the shared
///         <see cref="NinlilDnsWebApplicationFactory" /> host does not work: that host's own
///         runtime configuration file is overridden to an isolated path via an environment
///         variable, which <c>Program.cs</c> never separately registers as its own
///         reload-on-change <see cref="Microsoft.Extensions.Configuration.IConfiguration" />
///         source (only the default <c>appsettings.json</c> gets that treatment automatically) --
///         so a write there is never actually observed by <see cref="IOptionsMonitor{TOptions}" />
///         within that specific host at all, not merely slowly. Constructing the controller
///         directly against an already-correct value sidesteps that gap entirely, and is a more
///         precise test of these two actions' own real logic regardless -- neither test is
///         actually about proving hot-reload (that is a separate, deliberate future concern; see
///         this class's own note below).
///     </para>
///     <para>
///         <b>What this class can and cannot verify, stated explicitly:</b> a real end-to-end
///         order/challenge/finalize round trip against Let's Encrypt (or any other real CA) needs a
///         real, publicly-resolvable domain this environment does not have -- a documented,
///         known verification boundary.
///         <see cref="IssueOrRenewNow_AgainstAnUnreachableCa_RecordsAndReturnsTheRealFailure" />
///         instead points <c>directoryUri</c> at a closed loopback port (the same
///         deterministic-real-network-failure technique
///         <c>AcmeCertificateRenewalServiceEventTests</c> already established), which proves the
///         entire DI/failure-recording mechanism this action depends on works end to end without
///         ever needing the CA call itself to succeed.
///     </para>
/// </summary>
public sealed class AcmeSettingsControllerDirectTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-acme-settings-direct-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;

    /// <summary>
    ///     This class stands in for a real, DI-built <see cref="IOptionsMonitor{TOptions}" />: it
    ///     always reports the same, fixed <see cref="CurrentValue" /> and never actually fires
    ///     <see cref="OnChange" /> -- mirroring every sibling test project's own identically-named
    ///     fake (for example <c>Ninlil.Dns.Server.Acme.Tests.Fakes.StaticOptionsMonitor{TOptions}</c>).
    /// </summary>
    private sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = value;

        public TOptions Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<TOptions, string> listener) => null;
    }

    /// <summary>
    ///     This class stands in for a real, durable <see cref="IAcmeAccountStore" /> with a plain
    ///     in-memory dictionary -- the minimal double
    ///     <see cref="Certificates_ReflectsRealAccountStoreStateForEveryConfiguredDomain" /> needs
    ///     to seed a specific, arbitrary combination of per-domain state without any real
    ///     filesystem/<see cref="IDataProtector" /> involvement, since that test never exercises a
    ///     real issuance/renewal attempt at all.
    /// </summary>
    private sealed class InMemoryAcmeAccountStore : IAcmeAccountStore
    {
        private readonly Dictionary<string, AcmeDomainCertificateState> _domainStates = [];
        private readonly Dictionary<string, AcmeDomainRenewalFailure> _failures = [];

        public string AccountKeyPem { get; private set; }

        public IReadOnlyDictionary<string, AcmeDomainCertificateState> DomainStates =>
            _domainStates;

        public IReadOnlyDictionary<string, AcmeDomainRenewalFailure> Failures => _failures;

        public void SaveAccountKey(string accountKeyPem) => AccountKeyPem = accountKeyPem;

        public void SaveDomainState(string domain, AcmeDomainCertificateState state) =>
            _domainStates[domain] = state;

        public void SaveDomainFailure(string domain, AcmeDomainRenewalFailure failure) =>
            _failures[domain] = failure;

        public void ClearDomainFailure(string domain) => _failures.Remove(domain);
    }

    /// <summary>
    ///     This method builds a real, minimal host providing a genuinely working
    ///     <see cref="AcmeCertificateIssuer" />/<see cref="AcmeCertificateRenewalService" /> --
    ///     the same recipe <c>AcmeCertificateRenewalServiceEventTests</c> already establishes,
    ///     needed here only for
    ///     <see cref="IssueOrRenewNow_AgainstAnUnreachableCa_RecordsAndReturnsTheRealFailure" />,
    ///     which drives a real (if deliberately unreachable) network attempt through
    ///     <see cref="AcmeCertificateRenewalService.RenewOneDomainAsync" />.
    /// </summary>
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);

        // No IDnsServerEventPublisher override here -- unlike AcmeCertificateRenewalServiceEventTests,
        // this class never asserts on a published event, only on IAcmeAccountStore's own durable
        // record and the controller's own returned inventory entry, so AddNinlilDnsServerCore's
        // real registration (or none at all, since AcmeCertificateRenewalService's own
        // eventPublisher parameter defaults to null) is sufficient either way.
        builder.Services.AddSingleton<IZoneWriteSerializer, ZoneWriteSerializer>();
        builder.Services.AddScoped<IZoneUpdateService, ZoneUpdateService>();
        builder.Services.AddSingleton<AuthoritativeZoneCache>();
        builder.Services.AddSingleton<IHostedService>(provider =>
            provider.GetRequiredService<AuthoritativeZoneCache>()
        );

        builder.Services.AddSingleton<IDataProtectionProvider>(
            new EphemeralDataProtectionProvider()
        );
        builder.Services.Configure<AcmeOptions>(options =>
        {
            options.Enable = true;

            // A closed loopback port -- Certes's own first HTTP call (fetching the directory to
            // register/reuse an account) fails immediately with a real connection-refused error.
            options.DirectoryUri = new Uri("http://127.0.0.1:1/directory");
            options.Domains = [UnreachableCaDomain];
            options.AccountStateFilePath = Path.Combine(
                Path.GetTempPath(),
                $"ninlil-dns-acme-settings-direct-tests-{Guid.NewGuid():N}-account.json"
            );
        });
        builder.Services.Configure<CaaAutoManagementOptions>(_ => { });

        builder.Services.AddSingleton<IAcmeAccountStore, FileAcmeAccountStore>();
        builder.Services.AddScoped<AcmeDnsChallengeResponder>();
        builder.Services.AddScoped<CaaRecordReconciliationService>();
        builder.Services.AddScoped<AcmeCertificateIssuer>();
        builder.Services.AddSingleton<AcmeCertificateRenewalService>();

        _host = builder.Build();

        await using AsyncServiceScope seedScope = _host.Services.CreateAsyncScope();
        NinlilDnsContext context = seedScope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    private const string UnreachableCaDomain =
        "acme-settings-direct-tests-manual-trigger.acme-settings-tests.invalid";

    [Fact]
    public void Certificates_ReflectsRealAccountStoreStateForEveryConfiguredDomain()
    {
        const string activeDomain = "acme-inventory-active.acme-settings-tests.invalid";
        const string neverIssuedDomain = "acme-inventory-never-issued.acme-settings-tests.invalid";
        const string failedDomain = "acme-inventory-failed.acme-settings-tests.invalid";

        InMemoryAcmeAccountStore accountStore = new();
        accountStore.SaveDomainState(
            activeDomain,
            new AcmeDomainCertificateState(
                DateTimeOffset.UtcNow.AddDays(-10),
                DateTimeOffset.UtcNow.AddDays(80),
                "/tmp/acme-inventory-active.pfx"
            )
        );
        accountStore.SaveDomainFailure(
            failedDomain,
            new AcmeDomainRenewalFailure(
                DateTimeOffset.UtcNow.AddHours(-2),
                "Simulated failure for AcmeSettingsControllerDirectTests."
            )
        );

        AcmeOptions acmeOptions = new()
        {
            Domains = [activeDomain, neverIssuedDomain, failedDomain],
        };

        using ServiceProvider minimalProvider = new ServiceCollection().BuildServiceProvider();
        AcmeCertificateRenewalService renewalService = new(
            minimalProvider.GetRequiredService<IServiceScopeFactory>(),
            accountStore,
            new StaticOptionsMonitor<AcmeOptions>(acmeOptions),
            NullLogger<AcmeCertificateRenewalService>.Instance
        );

        AcmeSettingsController controller = new(
            new StaticOptionsMonitor<AcmeOptions>(acmeOptions),
            new StaticOptionsMonitor<CaaAutoManagementOptions>(new CaaAutoManagementOptions()),
            accountStore,
            renewalService,
            new RuntimeConfigurationOptions
            {
                ConfigurationFilePath = Path.Combine(
                    Path.GetTempPath(),
                    $"ninlil-dns-acme-settings-direct-tests-unused-{Guid.NewGuid():N}.json"
                ),
            }
        );

        List<AcmeCertificateInventoryEntry> entries = [.. UnwrapOk(controller.Certificates())];

        Assert.Equal(3, entries.Count);

        AcmeCertificateInventoryEntry active = entries.Single(entry =>
            entry.Domain == activeDomain
        );
        Assert.True(active.HasCertificate);
        Assert.NotNull(active.IssuedAtUtc);
        Assert.NotNull(active.ExpiresAtUtc);
        Assert.False(active.IsDueForRenewal);
        Assert.Null(active.LastFailureAtUtc);
        Assert.Null(active.LastFailureMessage);

        AcmeCertificateInventoryEntry neverIssued = entries.Single(entry =>
            entry.Domain == neverIssuedDomain
        );
        Assert.False(neverIssued.HasCertificate);
        Assert.Null(neverIssued.IssuedAtUtc);
        Assert.True(neverIssued.IsDueForRenewal);
        Assert.Null(neverIssued.LastFailureAtUtc);

        // The genuinely distinct state this class's own coverage requires: a domain that
        // has never been successfully issued a certificate *and* whose most recent attempt is
        // known to have failed, distinguishable both from "active" and from "never attempted at
        // all" (neverIssued, above, which has no failure recorded either).
        AcmeCertificateInventoryEntry failed = entries.Single(entry =>
            entry.Domain == failedDomain
        );
        Assert.False(failed.HasCertificate);
        Assert.True(failed.IsDueForRenewal);
        Assert.NotNull(failed.LastFailureAtUtc);
        Assert.Equal(
            "Simulated failure for AcmeSettingsControllerDirectTests.",
            failed.LastFailureMessage
        );
    }

    [Fact]
    public async Task IssueOrRenewNow_AgainstAnUnreachableCa_RecordsAndReturnsTheRealFailure()
    {
        AcmeCertificateRenewalService renewalService =
            _host.Services.GetRequiredService<AcmeCertificateRenewalService>();
        IAcmeAccountStore accountStore = _host.Services.GetRequiredService<IAcmeAccountStore>();
        IOptionsMonitor<AcmeOptions> acmeOptionsMonitor = _host.Services.GetRequiredService<
            IOptionsMonitor<AcmeOptions>
        >();

        AcmeSettingsController controller = new(
            acmeOptionsMonitor,
            new StaticOptionsMonitor<CaaAutoManagementOptions>(new CaaAutoManagementOptions()),
            accountStore,
            renewalService,
            new RuntimeConfigurationOptions
            {
                ConfigurationFilePath = Path.Combine(
                    Path.GetTempPath(),
                    $"ninlil-dns-acme-settings-direct-tests-unused-{Guid.NewGuid():N}.json"
                ),
            }
        );

        ActionResult<AcmeCertificateInventoryEntry> result = await controller.IssueOrRenewNow(
            UnreachableCaDomain,
            CancellationToken.None
        );
        AcmeCertificateInventoryEntry entry = Assert.IsType<AcmeCertificateInventoryEntry>(
            Assert.IsType<OkObjectResult>(result.Result).Value
        );
        Assert.Equal(UnreachableCaDomain, entry.Domain);
        Assert.False(entry.HasCertificate);
        Assert.NotNull(entry.LastFailureAtUtc);
        Assert.False(string.IsNullOrWhiteSpace(entry.LastFailureMessage));

        // The same failure the response body already carries must also be durably recorded,
        // readable independently through a fresh Certificates() read -- not just returned once
        // and forgotten.
        List<AcmeCertificateInventoryEntry> entries = [.. UnwrapOk(controller.Certificates())];
        AcmeCertificateInventoryEntry persisted = Assert.Single(entries);
        Assert.Equal(entry.LastFailureMessage, persisted.LastFailureMessage);
    }

    private static IReadOnlyList<AcmeCertificateInventoryEntry> UnwrapOk(
        ActionResult<IReadOnlyList<AcmeCertificateInventoryEntry>> result
    ) =>
        Assert.IsAssignableFrom<IReadOnlyList<AcmeCertificateInventoryEntry>>(
            Assert.IsType<OkObjectResult>(result.Result).Value
        );
}
