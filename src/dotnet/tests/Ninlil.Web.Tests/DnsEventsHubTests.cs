using System.Net.Http.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Server.Core.Events;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

[Collection(WebTestCollection.Name)]
public class DnsEventsHubTests(NinlilDnsWebApplicationFactory factory)
{
    [Fact]
    public async Task AnAuthenticatedClientReceivesAnEventPublishedThroughServerCoresOwnEventBus()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage loginResponse = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        loginResponse.EnsureSuccessStatusCode();
        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        // Long polling, not the default (WebSockets first), since TestServer's own in-memory
        // transport speaks plain HTTP request/response, not a real WebSocket upgrade -- the same
        // reason this is a real HTTP round trip through the actual hub pipeline (auth included),
        // not a mock.
        await using HubConnection connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "/hubs/events"),
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling,
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.AccessTokenProvider = () => Task.FromResult(login.AccessToken);
                }
            )
            .Build();

        TaskCompletionSource<DnsZoneChangedEvent> received = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        connection.On<DnsZoneChangedEvent>(
            "DnsZoneChanged",
            domainEvent => received.TrySetResult(domainEvent)
        );

        await connection.StartAsync();

        DnsZoneChangedEvent publishedEvent = new(
            DateTimeOffset.UtcNow,
            Guid.NewGuid(),
            "example.test"
        );

        using (IServiceScope scope = factory.Services.CreateScope())
        {
            IDnsServerEventPublisher publisher =
                scope.ServiceProvider.GetRequiredService<IDnsServerEventPublisher>();
            publisher.Publish(publishedEvent);
        }

        DnsZoneChangedEvent receivedEvent = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(publishedEvent.ZoneId, receivedEvent.ZoneId);
        Assert.Equal(publishedEvent.ZoneName, receivedEvent.ZoneName);
    }

    [Fact]
    public async Task ConnectingToTheHubWithoutAuthenticationFails()
    {
        await using HubConnection connection = new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "/hubs/events"),
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling,
                options => options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler()
            )
            .Build();

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());
    }
}
