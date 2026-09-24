using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Server.Webhooks.Extension;

// Define our namespace.
namespace Ninlil.Dns.Server.Webhooks.Tests;

/// <summary>
///     This class builds a real dependency injection host, wired exactly the way
///     <c>Ninlil.Web</c>'s own composition root wires the outbound webhooks feature (real
///     SQLite-backed repositories via <c>AddNinlilDnsEntityFrameworkCore</c>, plus
///     <see cref="Ninlil.Dns.Server.Webhooks.Extension.ServiceCollectionWebhooksExtension.AddNinlilDnsServerWebhooks" />
///     itself, which registers <see cref="WebhookDeliveryRetrySweepService" /> as a real hosted
///     service) -- unlike <see cref="WebhookDeliveryServiceTests" />'s own fake, in-memory
///     repositories (deliberately used there to keep that class's own tests about HTTP delivery
///     behavior, not database wiring), this host exists specifically to prove the real,
///     persisted, cross-restart durability story end to end.
///     <para>
///         Unlike <c>Ninlil.Dns.Server.QueryLog.Tests</c>'s own <c>QueryLogTestHost</c>, this
///         class accepts an existing SQLite database path rather than always generating its own
///         fresh one -- the one property a genuine "kill one process, start a fresh one pointed at
///         the same database" proof needs: two independently constructed hosts, sharing no
///         in-memory state whatsoever, reading and writing the exact same on-disk file.
///     </para>
/// </summary>
public sealed class WebhookTestHost : IAsyncDisposable
{
    /// <summary>
    ///     This property contains the real dependency injection container this test's own
    ///     scopes are created against.
    /// </summary>
    public IHost Host { get; private set; }

    /// <summary>
    ///     This method builds (but does not start) the host against <paramref name="databasePath" />,
    ///     applying the entity model directly (via <c>EnsureCreatedAsync</c>) rather than the real
    ///     relational migrations -- the same shortcut this project's own comparable write-path
    ///     service tests already take -- and only if the database does not already exist, so a
    ///     second host built against an existing file (the actual "restart" scenario this class
    ///     exists for) never wipes out what the first host already wrote.
    /// </summary>
    /// <param name="databasePath">The SQLite database file this host reads from and writes to.</param>
    /// <param name="initialRetryDelay">The delivery engine's own configured initial retry delay.</param>
    /// <param name="maxRetryDelay">The delivery engine's own configured maximum retry delay.</param>
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
    ///     This method disposes this host the ordinary, graceful way (equivalent to a real
    ///     process shutting down cleanly) -- a test proving genuine restart durability must
    ///     instead simulate an abrupt kill by simply discarding <see cref="Host" /> without ever
    ///     calling <see cref="IHost.StopAsync(CancellationToken)" /> at all, then never calling
    ///     this method on that same instance.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        Host?.Dispose();
        return ValueTask.CompletedTask;
    }
}
