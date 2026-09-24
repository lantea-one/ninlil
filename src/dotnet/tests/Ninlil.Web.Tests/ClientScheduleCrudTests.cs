using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Ninlil.Web.Controllers;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the time-of-day parental-control schedule REST endpoints end to end,
///     against a real, in-process host and a real SQLite database (see
///     <see cref="NinlilDnsWebApplicationFactory" />) -- a full lifecycle for both
///     <see cref="ClientSchedulesController" /> (<c>/api/clients/{clientId}/schedules</c>) and
///     <see cref="ClientGroupSchedulesController" /> (<c>/api/client-groups/{groupId}/schedules</c>),
///     plus the validation rules (<c>daysOfWeek</c> non-zero, a recognized <c>timeZoneId</c>,
///     <c>startTime</c> differing from <c>endTime</c>) both controllers share.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ClientScheduleCrudTests(NinlilDnsWebApplicationFactory factory)
{
    private async Task<HttpClient> CreateAuthenticatedClientAsync()
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            login.AccessToken
        );

        return client;
    }

    private async Task<Guid> CreateClientAsync(HttpClient client)
    {
        string identifier = $"AA:11:{Guid.NewGuid().ToString("N")[..8].ToUpperInvariant()}";
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/clients",
            new { clientIdentifier = identifier, protocolVersion = "IPv4" }
        );
        response.EnsureSuccessStatusCode();
        ClientDetail created = await response.Content.ReadFromJsonAsync<ClientDetail>();
        return created.Id;
    }

    private async Task<Guid> CreateClientGroupAsync(HttpClient client)
    {
        HttpResponseMessage response = await client.PostAsJsonAsync(
            "/api/client-groups",
            new { name = $"group-{Guid.NewGuid():N}" }
        );
        response.EnsureSuccessStatusCode();
        ClientGroupDetail created = await response.Content.ReadFromJsonAsync<ClientGroupDetail>();
        return created.Id;
    }

    [Fact]
    public async Task ClientScheduleLifecycle_CreateReplaceAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid clientId = await CreateClientAsync(client);

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            $"/api/clients/{clientId}/schedules",
            new
            {
                daysOfWeek = "Sunday, Monday, Tuesday, Wednesday, Thursday",
                startTime = "21:00:00",
                endTime = "07:00:00",
                timeZoneId = "America/New_York",
                comments = "school nights",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ClientScheduleDetail created =
            await createResponse.Content.ReadFromJsonAsync<ClientScheduleDetail>();
        Assert.Equal(clientId, created.ClientId);
        Assert.Equal(new TimeOnly(21, 0), created.StartTime);
        Assert.Equal(new TimeOnly(7, 0), created.EndTime);
        Assert.Equal("America/New_York", created.TimeZoneId);
        Assert.False(created.IsDisabled);

        // Listing shows the new schedule.
        HttpResponseMessage listResponse = await client.GetAsync(
            $"/api/clients/{clientId}/schedules?skip=0&take=50"
        );
        listResponse.EnsureSuccessStatusCode();
        PagedResult<ClientScheduleDetail> list = await listResponse.Content.ReadFromJsonAsync<
            PagedResult<ClientScheduleDetail>
        >();
        Assert.Contains(list.Items, schedule => schedule.Id == created.Id);

        // A single-item GET returns the same schedule.
        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/clients/{clientId}/schedules/{created.Id}"
        );
        getResponse.EnsureSuccessStatusCode();
        ClientScheduleDetail fetched =
            await getResponse.Content.ReadFromJsonAsync<ClientScheduleDetail>();
        Assert.Equal(created.Id, fetched.Id);

        // PUT fully replaces the schedule's own fields.
        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/clients/{clientId}/schedules/{created.Id}",
            new
            {
                daysOfWeek = "Saturday",
                startTime = "08:00:00",
                endTime = "09:00:00",
                timeZoneId = "Europe/London",
                isDisabled = true,
                comments = "weekend quiet time",
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage afterReplaceResponse = await client.GetAsync(
            $"/api/clients/{clientId}/schedules/{created.Id}"
        );
        ClientScheduleDetail replaced =
            await afterReplaceResponse.Content.ReadFromJsonAsync<ClientScheduleDetail>();
        Assert.Equal("Saturday", replaced.DaysOfWeek.ToString());
        Assert.Equal(new TimeOnly(8, 0), replaced.StartTime);
        Assert.Equal("Europe/London", replaced.TimeZoneId);
        Assert.True(replaced.IsDisabled);
        Assert.Equal("weekend quiet time", replaced.Comments);
        Assert.NotNull(replaced.UpdatedAtUtc);

        // Deleting works and is reflected immediately.
        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/clients/{clientId}/schedules/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/clients/{clientId}/schedules/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task CreateClientSchedule_ForANonexistentClient_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/clients/{Guid.NewGuid()}/schedules",
            new
            {
                daysOfWeek = "Monday",
                startTime = "09:00:00",
                endTime = "17:00:00",
                timeZoneId = "UTC",
            }
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateClientSchedule_WithNoDaysOfWeek_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid clientId = await CreateClientAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/clients/{clientId}/schedules",
            new
            {
                daysOfWeek = "None",
                startTime = "09:00:00",
                endTime = "17:00:00",
                timeZoneId = "UTC",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateClientSchedule_WithAnUnrecognizedTimeZone_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid clientId = await CreateClientAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/clients/{clientId}/schedules",
            new
            {
                daysOfWeek = "Monday",
                startTime = "09:00:00",
                endTime = "17:00:00",
                timeZoneId = "Not/A_Real_Zone",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateClientSchedule_WithEqualStartAndEndTimes_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid clientId = await CreateClientAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/clients/{clientId}/schedules",
            new
            {
                daysOfWeek = "Monday",
                startTime = "09:00:00",
                endTime = "09:00:00",
                timeZoneId = "UTC",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    ///     This test proves a midnight-wrapping window (<c>endTime</c> earlier than
    ///     <c>startTime</c>) is genuinely accepted, not rejected as some kind of "invalid range" --
    ///     see <c>Ninlil.Dns.Server.Core.ClientSchedule.DnsScheduleWindowEvaluator</c>'s own doc
    ///     comment for why this shape is real and deliberately supported.
    /// </summary>
    [Fact]
    public async Task CreateClientSchedule_WithAMidnightWrappingWindow_Succeeds()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid clientId = await CreateClientAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/clients/{clientId}/schedules",
            new
            {
                daysOfWeek = "Monday",
                startTime = "21:00:00",
                endTime = "07:00:00",
                timeZoneId = "UTC",
            }
        );

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ClientScheduleEndpoints_WithoutAToken_ReturnUnauthorized()
    {
        HttpClient client = factory.CreateClient();

        HttpResponseMessage response = await client.GetAsync(
            $"/api/clients/{Guid.NewGuid()}/schedules"
        );

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ClientGroupScheduleLifecycle_CreateReplaceAndDelete_WorksEndToEnd()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid groupId = await CreateClientGroupAsync(client);

        HttpResponseMessage createResponse = await client.PostAsJsonAsync(
            $"/api/client-groups/{groupId}/schedules",
            new
            {
                daysOfWeek = "Sunday, Monday, Tuesday, Wednesday, Thursday, Friday, Saturday",
                startTime = "22:00:00",
                endTime = "06:30:00",
                timeZoneId = "Europe/London",
            }
        );
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        ClientGroupScheduleDetail created =
            await createResponse.Content.ReadFromJsonAsync<ClientGroupScheduleDetail>();
        Assert.Equal(groupId, created.ClientGroupId);

        HttpResponseMessage listResponse = await client.GetAsync(
            $"/api/client-groups/{groupId}/schedules?skip=0&take=50"
        );
        listResponse.EnsureSuccessStatusCode();
        PagedResult<ClientGroupScheduleDetail> list = await listResponse.Content.ReadFromJsonAsync<
            PagedResult<ClientGroupScheduleDetail>
        >();
        Assert.Contains(list.Items, schedule => schedule.Id == created.Id);

        HttpResponseMessage replaceResponse = await client.PutAsJsonAsync(
            $"/api/client-groups/{groupId}/schedules/{created.Id}",
            new
            {
                daysOfWeek = "Friday",
                startTime = "20:00:00",
                endTime = "21:00:00",
                timeZoneId = "UTC",
                isDisabled = true,
            }
        );
        Assert.Equal(HttpStatusCode.NoContent, replaceResponse.StatusCode);

        HttpResponseMessage getResponse = await client.GetAsync(
            $"/api/client-groups/{groupId}/schedules/{created.Id}"
        );
        ClientGroupScheduleDetail replaced =
            await getResponse.Content.ReadFromJsonAsync<ClientGroupScheduleDetail>();
        Assert.Equal("Friday", replaced.DaysOfWeek.ToString());
        Assert.True(replaced.IsDisabled);

        HttpResponseMessage deleteResponse = await client.DeleteAsync(
            $"/api/client-groups/{groupId}/schedules/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        HttpResponseMessage afterDeleteResponse = await client.GetAsync(
            $"/api/client-groups/{groupId}/schedules/{created.Id}"
        );
        Assert.Equal(HttpStatusCode.NotFound, afterDeleteResponse.StatusCode);
    }

    [Fact]
    public async Task CreateClientGroupSchedule_ForANonexistentGroup_ReturnsNotFound()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/client-groups/{Guid.NewGuid()}/schedules",
            new
            {
                daysOfWeek = "Monday",
                startTime = "09:00:00",
                endTime = "17:00:00",
                timeZoneId = "UTC",
            }
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateClientGroupSchedule_WithNoDaysOfWeek_ReturnsBadRequest()
    {
        HttpClient client = await CreateAuthenticatedClientAsync();
        Guid groupId = await CreateClientGroupAsync(client);

        HttpResponseMessage response = await client.PostAsJsonAsync(
            $"/api/client-groups/{groupId}/schedules",
            new
            {
                daysOfWeek = "None",
                startTime = "09:00:00",
                endTime = "17:00:00",
                timeZoneId = "UTC",
            }
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
