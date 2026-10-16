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
///     This class tests that <see cref="AcmeCertificateRenewalService" /> publishes
///     <see cref="AcmeCertificateRenewalFailedEvent" /> and records the failure when issuance
///     fails. It runs the service in a host with SQLite storage and a recording event publisher.
///     <see cref="AcmeCertificateRenewalSucceededEvent" /> is not tested: a success needs a full
///     RFC 8555 order with a CA and a publicly resolvable domain.
///     <para>
///         The failure is a real network error: <c>AcmeOptions.DirectoryUri</c> points at a closed
///         loopback port, so the first Certes HTTP call fails at once with connection refused.
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

        // Replaces the IDnsServerEventPublisher from AddNinlilDnsServerCore, so published events
        // are captured instead of dispatched.
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

            // A closed loopback port: the first Certes HTTP call (fetching the directory) is
            // refused at once instead of waiting for a timeout, as an unreachable address would.
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
        builder.Services.AddScoped<AcmeChallengePropagationWaiter>();
        builder.Services.AddSingleton<IAcmeChallengeProbe, UdpAcmeChallengeProbe>();
        builder.Services.AddSingleton<
            IAcmeNameServerAddressResolver,
            SystemAcmeNameServerAddressResolver
        >();
        builder.Services.AddScoped<CaaRecordReconciliationService>();
        builder.Services.AddScoped<AcmeCertificateIssuer>();

        // Registered as its concrete type (AddHostedService would expose it only as
        // IHostedService) so the test can resolve and start it, as CloudDnsZoneSyncServiceTests
        // does.
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
        // StartAsync returns at the first await inside the renewal pass, before it has finished,
        // so poll for the event instead of assuming it has been published.
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

        // The failure must also be recorded in the store, which the ACME settings API reads (see
        // AcmeDomainRenewalFailure).
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
