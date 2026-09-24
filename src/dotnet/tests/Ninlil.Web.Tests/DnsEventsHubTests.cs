using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
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

    [Fact]
    public async Task AUserWithoutQueryLogReadReceivesNoQueryEvents()
    {
        // zones:read lets this user receive zone events, which the test uses to know the query
        // event it published before them has been dispatched (and withheld) too.
        string accessToken = await CreateUserAndSignInAsync("zones:read");
        await using HubConnection connection = CreateConnection(accessToken);

        string queriedName = $"{Guid.NewGuid():N}.hidden.test";
        List<string> receivedQueryNames = [];
        connection.On<JsonElement>(
            "DnsQueryReceived",
            payload =>
            {
                lock (receivedQueryNames)
                    receivedQueryNames.Add(payload.GetProperty("queriedName").GetString());
            }
        );

        Guid zoneId = Guid.NewGuid();
        TaskCompletionSource zoneEventReceived = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        connection.On<DnsZoneChangedEvent>(
            "DnsZoneChanged",
            domainEvent =>
            {
                if (domainEvent.ZoneId == zoneId)
                    zoneEventReceived.TrySetResult();
            }
        );

        await connection.StartAsync();

        Publish(CreateQueryReceivedEvent(queriedName));
        Publish(new DnsZoneChangedEvent(DateTimeOffset.UtcNow, zoneId, "visible.test"));

        await zoneEventReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The events are dispatched in order, so the query event has already been handled by the
        // time the zone event arrives; the grace period only covers delivery on the wire.
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        lock (receivedQueryNames)
            Assert.DoesNotContain(queriedName, receivedQueryNames);
    }

    [Fact]
    public async Task AUserWithQueryLogReadReceivesQueryEvents()
    {
        string accessToken = await CreateUserAndSignInAsync("querylog:read");
        await using HubConnection connection = CreateConnection(accessToken);

        string queriedName = $"{Guid.NewGuid():N}.visible.test";
        TaskCompletionSource queryEventReceived = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        connection.On<JsonElement>(
            "DnsQueryReceived",
            payload =>
            {
                if (payload.GetProperty("queriedName").GetString() == queriedName)
                    queryEventReceived.TrySetResult();
            }
        );

        await connection.StartAsync();

        Publish(CreateQueryReceivedEvent(queriedName));

        await queryEventReceived.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    /// <summary>
    ///     This method creates a user holding only the given permission, granted directly rather
    ///     than through a role, signs them in, and returns their access token.
    /// </summary>
    private async Task<string> CreateUserAndSignInAsync(string permission)
    {
        HttpClient adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            await SignInAsync(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );

        string userName = $"hub-test-user-{Guid.NewGuid():N}";
        const string password = "Hub-Test-Password-1234!";

        HttpResponseMessage createResponse = await adminClient.PostAsJsonAsync(
            "/api/users",
            new CreateUserRequest(userName, password, $"{userName}@localhost.local", "Hub Test")
        );
        createResponse.EnsureSuccessStatusCode();
        UserSummary user = await createResponse.Content.ReadFromJsonAsync<UserSummary>();

        HttpResponseMessage grantResponse = await adminClient.PostAsJsonAsync(
            $"/api/users/{user.Id}/permissions",
            new UserPermissionRequest(permission)
        );
        Assert.Equal(HttpStatusCode.NoContent, grantResponse.StatusCode);

        return await SignInAsync(userName, password);
    }

    private async Task<string> SignInAsync(string userName, string password)
    {
        HttpResponseMessage loginResponse = await factory
            .CreateClient()
            .PostAsJsonAsync("/api/auth/login", new LoginRequest(userName, password));
        loginResponse.EnsureSuccessStatusCode();
        LoginResponse login = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();

        return login.AccessToken;
    }

    /// <summary>
    ///     This method builds a long-polling hub connection that presents the given access token
    ///     (see the first test's own comment on why long polling).
    /// </summary>
    private HubConnection CreateConnection(string accessToken) =>
        new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "/hubs/events"),
                Microsoft.AspNetCore.Http.Connections.HttpTransportType.LongPolling,
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.AccessTokenProvider = () => Task.FromResult(accessToken);
                }
            )
            .Build();

    private void Publish(IDnsServerEvent domainEvent)
    {
        using IServiceScope scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IDnsServerEventPublisher>().Publish(domainEvent);
    }

    private static DnsQueryReceivedEvent CreateQueryReceivedEvent(string queriedName) =>
        new(
            DateTimeOffset.UtcNow,
            DnsTransportProtocolEnum.Udp,
            IPAddress.Parse("192.0.2.10"),
            queriedName,
            DnsResourceRecordTypeEnum.A
        );
}
