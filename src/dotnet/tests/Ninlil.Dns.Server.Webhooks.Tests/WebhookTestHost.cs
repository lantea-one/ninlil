using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Webhooks.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests;

/// <summary>
///     This class builds a dependency injection host with the same registrations
///     <c>Ninlil.Web</c> uses for webhooks: SQLite-backed EF Core repositories
///     (<c>AddNinlilDnsEntityFrameworkCore</c>) and
///     <see cref="Ninlil.Dns.Server.Webhooks.Extension.ServiceCollectionWebhooksExtension.AddNinlilDnsServerWebhooks" />,
///     which registers <see cref="WebhookDeliveryRetrySweepService" /> as a hosted service.
///     <see cref="WebhookDeliveryRetrySweepServiceTests" /> uses it to test the sweep against
///     saved rows, where <see cref="WebhookDeliveryServiceTests" /> uses in-memory fakes.
///     <para>
///         Unlike <c>QueryLogTestHost</c> in <c>Ninlil.Dns.Server.QueryLog.Tests</c>, it takes a
///         database path, so two hosts could share one file to simulate a restart; the current
///         tests build only one host per test.
///     </para>
/// </summary>
public sealed class WebhookTestHost : IAsyncDisposable
{
    /// <summary>
    ///     This property contains the host whose services the tests create scopes from.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This method builds (but does not start) the host against <paramref name="databasePath" />,
    ///     creating the schema with <c>EnsureCreatedAsync</c> instead of the migrations, as other
    ///     storage-backed service tests do. <c>EnsureCreatedAsync</c> does nothing when the
    ///     database already exists, so a second host on the same file keeps what the first
    ///     wrote.
    /// </summary>
    /// <param name="databasePath">The SQLite database file this host reads from and writes to.</param>
    /// <param name="initialRetryDelay">The configured initial retry delay.</param>
    /// <param name="maxRetryDelay">The configured maximum retry delay.</param>
    /// <param name="sweepInterval">How often <see cref="WebhookDeliveryRetrySweepService" /> looks for a due retry.</param>
    /// <param name="attemptRetentionPeriod">How long a terminal delivery is kept before the retention sweep removes it.</param>
    public async Task InitializeAsync(
        string databasePath,
        TimeSpan initialRetryDelay,
        TimeSpan maxRetryDelay,
        TimeSpan sweepInterval,
        TimeSpan? attemptRetentionPeriod = null
    )
    {
        HostApplicationBuilder builder =
            Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Configuration["Storage:ConnectionString"] = $"Data Source={databasePath}";
        builder.Configuration["DnsServer:Webhooks:Delivery:InitialRetryDelay"] =
            initialRetryDelay.ToString();
        builder.Configuration["DnsServer:Webhooks:Delivery:MaxRetryDelay"] =
            maxRetryDelay.ToString();
        builder.Configuration["DnsServer:Webhooks:Delivery:RequestTimeout"] = TimeSpan
            .FromSeconds(5)
            .ToString();
        builder.Configuration["DnsServer:Webhooks:Delivery:SweepInterval"] =
            sweepInterval.ToString();
        builder.Configuration["DnsServer:Webhooks:Delivery:AttemptRetentionPeriod"] = (
            attemptRetentionPeriod ?? TimeSpan.FromDays(30)
        ).ToString();

        builder.Services.AddNinlilDnsEntityFrameworkCore(builder.Configuration);
        builder.Services.AddNinlilDnsServerWebhooks(builder.Configuration);

        Host = builder.Build();

        await using AsyncServiceScope scope = Host.Services.CreateAsyncScope();
        NinlilDnsContext context = scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
        await context.Database.EnsureCreatedAsync();
    }

    /// <summary>
    ///     This method disposes the host, like a clean shutdown. A test simulating an abrupt kill
    ///     would instead drop <see cref="Host" /> without calling
    ///     <see cref="IHost.StopAsync(CancellationToken)" /> or this method.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Host?.Dispose();
        return ValueTask.CompletedTask;
    }
}
