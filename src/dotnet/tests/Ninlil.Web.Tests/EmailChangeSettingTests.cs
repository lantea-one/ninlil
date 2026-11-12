using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Ninlil.Web.Models;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     This class covers the <c>requireEmailChangeConfirmation</c> field of
///     <c>/api/identity-settings</c> (<see cref="Controllers.IdentitySettingsController" />): its
///     default, the permission a write needs, and a patch followed by a read. The shared host does
///     not read the runtime settings file, so the read-back runs on a second host
///     (<see cref="NinlilDnsWebApplicationFactory.CreateSecondHost" />) that loads the file as a
///     reloading configuration source, as production does. The test puts the file back to the
///     default in a <c>finally</c> block, because the file belongs to the shared host.
/// </summary>
[Collection(WebTestCollection.Name)]
public class EmailChangeSettingTests(NinlilDnsWebApplicationFactory factory)
{
    private static async Task<HttpClient> SignInAsync(
        WebApplicationFactory<Program> host,
        string userName,
        string password
    )
    {
        HttpClient client = host.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(userName, password)
        );
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await login.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );
        return client;
    }

    private static HttpRequestMessage Patch(bool value) =>
        new(HttpMethod.Patch, "/api/identity-settings")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { requireEmailChangeConfirmation = value }),
                Encoding.UTF8,
                "application/json"
            ),
        };

    [Fact]
    public async Task Get_WithNothingConfigured_ReportsConfirmationRequired()
    {
        HttpClient admin = await SignInAsync(
            factory,
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );

        IdentitySettingsDetail detail = await admin.GetFromJsonAsync<IdentitySettingsDetail>(
            "/api/identity-settings"
        );

        Assert.True(detail.RequireEmailChangeConfirmation);
    }

    [Fact]
    public async Task Patch_WithoutTheWritePermission_ReturnsForbidden()
    {
        HttpClient admin = await SignInAsync(
            factory,
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );
        string userName = $"setting-{Guid.NewGuid():N}";
        (
            await admin.PostAsJsonAsync(
                "/api/users",
                new CreateUserRequest(
                    userName,
                    "Original-Password-1234!",
                    $"{userName}@localhost.local",
                    "No Permissions"
                )
            )
        ).EnsureSuccessStatusCode();
        HttpClient ordinary = await SignInAsync(factory, userName, "Original-Password-1234!");

        HttpResponseMessage response = await ordinary.SendAsync(Patch(false));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Patch_ToFalse_ThenGet_ReturnsFalse()
    {
        using WebApplicationFactory<Program> host = factory.CreateSecondHost(builder =>
            builder.ConfigureAppConfiguration(
                (_, configuration) =>
                    configuration.AddJsonFile(
                        factory.RuntimeConfigurationFilePath,
                        optional: true,
                        reloadOnChange: true
                    )
            )
        );
        HttpClient admin = await SignInAsync(
            host,
            NinlilDnsWebApplicationFactory.AdminUserName,
            NinlilDnsWebApplicationFactory.AdminPassword
        );

        try
        {
            (await admin.SendAsync(Patch(false))).EnsureSuccessStatusCode();

            // The configuration file reloads on a file-system event, a moment after the write.
            bool? value = null;
            for (int attempt = 0; attempt < 100 && value != false; attempt++)
            {
                value = (
                    await admin.GetFromJsonAsync<IdentitySettingsDetail>("/api/identity-settings")
                ).RequireEmailChangeConfirmation;
                if (value != false)
                    await Task.Delay(100);
            }

            Assert.False(value);
        }
        finally
        {
            // The file is shared with the other classes' hosts; put the default back.
            (await admin.SendAsync(Patch(true))).EnsureSuccessStatusCode();
        }
    }
}
