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
///     This class covers the certificate inventory (<see cref="AcmeSettingsController.Certificates" />)
///     and the manual issue-or-renew trigger (<see cref="AcmeSettingsController.IssueOrRenewNow" />)
///     of <see cref="AcmeSettingsController" />. It constructs the controller directly instead of
///     calling it over HTTP as <see cref="AcmeSettingsControllerTests" /> does for <c>Get</c> and
///     <c>Patch</c>.
///     <para>
///         Both actions read <see cref="AcmeOptions" /> through <see cref="IOptionsMonitor{TOptions}" />,
///         and each test needs a specific value in place (a set of domains, an unreachable
///         <see cref="AcmeOptions.DirectoryUri" />). A <c>PATCH /api/acme-settings</c> on the
///         shared <see cref="NinlilDnsWebApplicationFactory" /> host cannot set that up: its
///         runtime settings file is not registered as a configuration source, so the monitor never
///         sees the write. Passing a fixed value to the controller avoids that and tests the
///         actions' logic directly.
///     </para>
///     <para>
///         A full order, challenge and finalize round trip with a certificate authority needs a
///         publicly resolvable domain, which the test environment does not have.
///         <see cref="IssueOrRenewNow_AgainstAnUnreachableCa_RecordsAndReturnsTheRealFailure" />
///         instead points <c>directoryUri</c> at a closed loopback port, as
///         <c>AcmeCertificateRenewalServiceEventTests</c> does, so the failure is recorded and
///         returned without the CA call ever succeeding.
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
    ///     This class is a fake <see cref="IOptionsMonitor{TOptions}" /> that always reports the
    ///     same <see cref="CurrentValue" /> and never fires <see cref="OnChange" />, like
    ///     <see cref="Fakes.StaticOptionsMonitor{TOptions}" /> and the copies in the other test
    ///     projects.
    /// </summary>
    private sealed class StaticOptionsMonitor<TOptions>(TOptions value) : IOptionsMonitor<TOptions>
    {
        public TOptions CurrentValue { get; } = value;

        public TOptions Get(string name) => CurrentValue;

        public IDisposable OnChange(Action<TOptions, string> listener) => null;
    }

    /// <summary>
    ///     This class is an in-memory <see cref="IAcmeAccountStore" />.
    ///     <see cref="Certificates_ReflectsRealAccountStoreStateForEveryConfiguredDomain" /> uses it
    ///     to seed per-domain state without a file or an <see cref="IDataProtector" />, since that
    ///     test never attempts an issuance.
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
    ///     This method builds a minimal host with a working <see cref="AcmeCertificateIssuer" />
    ///     and <see cref="AcmeCertificateRenewalService" /> on a temporary SQLite database, as
    ///     <c>AcmeCertificateRenewalServiceEventTests</c> does. The host is built but not started.
    ///     Only <see cref="IssueOrRenewNow_AgainstAnUnreachableCa_RecordsAndReturnsTheRealFailure" />
    ///     uses it, to make a network attempt through
    ///     <see cref="AcmeCertificateRenewalService.RenewOneDomainAsync" /> that fails.
    /// </summary>
    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);

        // Unlike AcmeCertificateRenewalServiceEventTests, no recording event publisher is added:
        // these tests check the account store and the returned inventory entry, not events.
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

            // A closed loopback port, so the first Certes call (fetching the directory) fails at
            // once with connection refused.
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

        // A domain never issued a certificate whose last attempt failed must be distinguishable
        // both from an active one and from one never attempted (neverIssued has no failure).
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

        // The failure must also be recorded in the account store, so a later Certificates() read
        // shows it.
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
