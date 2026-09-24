using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Acme.Caa;
using Ninlil.Dns.Server.Authoritative.Cache;
using Ninlil.Dns.Server.Authoritative.Journal;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Dns.Server.Core.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme.Tests;

/// <summary>
///     This class covers <see cref="AcmeCertificateRenewalFailedEvent" />, confirming it actually
///     fires from <see cref="AcmeCertificateRenewalService" />'s own real trigger point (the
///     <c>catch</c> block around <c>AcmeCertificateIssuer.IssueOrRenewAsync</c>, which also
///     publishes the event). Deliberately does not attempt to exercise
///     <see cref="AcmeCertificateRenewalSucceededEvent" /> the same way: a genuine success
///     requires <c>AcmeCertificateIssuer</c> to complete a real RFC 8555 order/challenge/finalize
///     round trip against a real or fake CA -- exactly the same verification boundary
///     documented as a known limitation (the actual Certes-driven CA order/challenge/finalize
///     flow needs a real CA and a publicly-resolvable domain).
///     <see cref="AcmeCertificateRenewalSucceededEvent" />'s own publish call sits immediately
///     after that same, already-unverifiable success path and needs no new machinery of its own
///     to reason about; it is verified by direct code inspection instead (see
///     <see cref="AcmeCertificateRenewalService" />'s own source), not by a new executed test.
///     <para>
///         The deterministic failure this test drives is a real network failure, not a stub: this
///         resolver's own <c>AcmeOptions.DirectoryUri</c> points at a closed loopback port, so
///         Certes's very first HTTP call (registering/reusing the ACME account) fails fast with a
///         real connection-refused error -- the same "unreachable address, assert failure within a
///         few seconds" technique <c>Server.Events.ActiveMq.Tests</c>' own regression test for a
///         real client-library bug already established.
///     </para>
/// </summary>
public sealed class AcmeCertificateRenewalServiceEventTests : IAsyncLifetime
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-acme-renewal-event-tests-{Guid.NewGuid()}.sqlite3"
    );

    private IHost _host;
    private RecordingEventPublisher _publisher;

    public async Task InitializeAsync()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={_databasePath}";
        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerCore(builder.Configuration);

        // Overrides AddNinlilDnsServerCore's own real IDnsServerEventPublisher registration --
        // this test only cares about capturing what gets published directly, not about the real
        // background dispatcher delivering it to any subscriber.
        _publisher = new RecordingEventPublisher();
        builder.Services.AddSingleton<IDnsServerEventPublisher>(_publisher);

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
            // register/reuse an account) fails immediately with a real connection-refused error,
            // rather than hanging until a DNS/TCP timeout the way a routable-but-unreachable
            // address would.
            options.DirectoryUri = new Uri("http://127.0.0.1:1/directory");
            options.Domains = ["example.invalid"];
            options.AccountStateFilePath = Path.Combine(
                Path.GetTempPath(),
                $"ninlil-dns-acme-renewal-event-tests-{Guid.NewGuid()}-account.json"
            );
        });
        builder.Services.Configure<CaaAutoManagementOptions>(_ => { });

        builder.Services.AddSingleton<IAcmeAccountStore, FileAcmeAccountStore>();
        builder.Services.AddScoped<AcmeDnsChallengeResponder>();
        builder.Services.AddScoped<CaaRecordReconciliationService>();
        builder.Services.AddScoped<AcmeCertificateIssuer>();

        // Registered directly (not via AddHostedService, which would only expose it through
        // IHostedService) so this test can resolve and drive it by its own concrete type, the
        // same pattern CloudDnsZoneSyncServiceTests already establishes for a different
        // BackgroundService.
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

    [Fact]
    public async Task ARealRenewalFailureAgainstAnUnreachableCaPublishesTheFailedEvent()
    {
        // AcmeCertificateRenewalService.ExecuteAsync runs RenewDueDomainsAsync synchronously
        // before its own first Task.Delay, but BackgroundService still only schedules that
        // continuation rather than running it fully to completion before StartAsync returns --
        // the same real, empirically-confirmed scheduling gap CloudDnsZoneSyncServiceTests'
        // own RunOneSyncPassAsync already documents for a different background service. Poll for
        // the expected side effect instead of assuming it has already happened.
        AcmeCertificateRenewalService renewalService =
            _host.Services.GetRequiredService<AcmeCertificateRenewalService>();

        await renewalService.StartAsync(CancellationToken.None);

        for (int attempt = 0; attempt < 100 && _publisher.Published.Count == 0; attempt++)
            await Task.Delay(100);

        await renewalService.StopAsync(CancellationToken.None);

        AcmeCertificateRenewalFailedEvent failedEvent = Assert.Single(
            _publisher.Published.OfType<AcmeCertificateRenewalFailedEvent>()
        );
        Assert.Equal("example.invalid", failedEvent.Domain);
        Assert.False(string.IsNullOrWhiteSpace(failedEvent.ErrorMessage));

        Assert.Empty(_publisher.Published.OfType<AcmeCertificateRenewalSucceededEvent>());

        // The same failure this event carries must also be durably recorded, since nothing
        // subscribes to this event type to persist it anywhere a later REST read could find it
        // -- see AcmeDomainRenewalFailure's own doc comment for why this persistence point
        // exists at all.
        IAcmeAccountStore accountStore = _host.Services.GetRequiredService<IAcmeAccountStore>();
        Assert.True(
            accountStore.Failures.TryGetValue(
                "example.invalid",
                out AcmeDomainRenewalFailure recordedFailure
            )
        );
        Assert.Equal(failedEvent.ErrorMessage, recordedFailure.ErrorMessage);
    }
}
