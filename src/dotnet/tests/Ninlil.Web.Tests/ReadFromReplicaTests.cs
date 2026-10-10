using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Routing;
using Ninlil.Dns.Identity.Token;
using Ninlil.Web.Controllers;
using Ninlil.Web.Middleware;
using Ninlil.Web.Models;
using Ninlil.Web.Routing;

// Define our namespace.
namespace Ninlil.Web.Tests;

/// <summary>
///     These tests cover the read-from-replica plumbing of the host: the middleware that sets the
///     storage read intent and the principal (on a bare <see cref="DefaultHttpContext" /> and a
///     real router with a clock the test moves), the startup check that allows the attribute only
///     on GET actions (on the real action list and on made-up actions), the list of controllers
///     that must never carry the attribute, and two requests on the shared host that prove
///     authentication leaves the request scope free for the middleware. The shared host has no
///     replica, so these requests also prove that nothing changes without one. Reading a real
///     replica is covered in <c>Ninlil.Dns.Data.Integration.Tests</c>.
/// </summary>
[Collection(WebTestCollection.Name)]
public class ReadFromReplicaTests(NinlilDnsWebApplicationFactory factory)
{
    private sealed class MovableClock : TimeProvider
    {
        private long milliseconds = 1_000;

        public override long TimestampFrequency => 1_000;

        public override long GetTimestamp() => Interlocked.Read(ref milliseconds);

        public void Advance(TimeSpan by) =>
            Interlocked.Add(ref milliseconds, (long)by.TotalMilliseconds);
    }

    private sealed class Probe
    {
        public Probe(TimeSpan window)
        {
            Clock = new MovableClock();
            Router = new StorageReadRouter(true, window, Clock);
            Router.Enable();
            Router.ReportReplicaHealth(
                new ReplicaHealthStatus(true, 0, true, DateTimeOffset.UtcNow, "")
            );

            ServiceCollection services = new();
            services.AddSingleton<IStorageReadRouter>(Router);
            services.AddScoped<StorageReadIntent>();
            services.AddScoped<StoragePrincipal>();
            Provider = services.BuildServiceProvider(validateScopes: true);
        }

        public MovableClock Clock { get; }

        public StorageReadRouter Router { get; }

        public ServiceProvider Provider { get; }

        public async Task<(StorageReadIntent Intent, StoragePrincipal Principal)> RunAsync(
            string method,
            bool attributed,
            Guid? userId,
            Func<IServiceProvider, Task> endpoint = null
        )
        {
            await using AsyncServiceScope scope = Provider.CreateAsyncScope();
            DefaultHttpContext context = new() { RequestServices = scope.ServiceProvider };
            context.Request.Method = method;
            if (userId.HasValue)
                context.User = new ClaimsPrincipal(
                    new ClaimsIdentity(
                        [new Claim(JwtRegisteredClaimNames.Sub, userId.Value.ToString())],
                        "test"
                    )
                );

            EndpointMetadataCollection metadata = new(
                attributed ? [new ReadFromReplicaAttribute()] : Array.Empty<object>()
            );
            context.SetEndpoint(new Endpoint(_ => Task.CompletedTask, metadata, "probe"));

            StorageReadIntentMiddleware middleware = new(_ =>
                endpoint is null ? Task.CompletedTask : endpoint(scope.ServiceProvider)
            );
            StorageReadIntent intent =
                scope.ServiceProvider.GetRequiredService<StorageReadIntent>();
            StoragePrincipal principal =
                scope.ServiceProvider.GetRequiredService<StoragePrincipal>();
            await middleware.InvokeAsync(context, intent, principal);
            return (intent, principal);
        }
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("HEAD")]
    public async Task Invoke_ForAnAttributedReadRequest_AsksForTheReplica(string method)
    {
        Probe probe = new(TimeSpan.FromSeconds(10));

        (StorageReadIntent intent, _) = await probe.RunAsync(method, true, Guid.NewGuid());

        Assert.Equal(StorageReadTargetEnum.Replica, intent.Target);
    }

    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Invoke_ForAWriteMethodEvenWithTheAttribute_StaysOnThePrimary(string method)
    {
        Probe probe = new(TimeSpan.FromSeconds(10));

        (StorageReadIntent intent, _) = await probe.RunAsync(method, true, Guid.NewGuid());

        Assert.Equal(StorageReadTargetEnum.Primary, intent.Target);
    }

    [Fact]
    public async Task Invoke_ForAnEndpointWithoutTheAttribute_StaysOnThePrimary()
    {
        Probe probe = new(TimeSpan.FromSeconds(10));

        (StorageReadIntent intent, _) = await probe.RunAsync("GET", false, Guid.NewGuid());

        Assert.Equal(StorageReadTargetEnum.Primary, intent.Target);
    }

    [Fact]
    public async Task Invoke_ForAnAuthenticatedUser_SetsThePrincipalToTheUserKey()
    {
        Probe probe = new(TimeSpan.FromSeconds(10));
        Guid userId = Guid.NewGuid();

        (_, StoragePrincipal principal) = await probe.RunAsync("GET", true, userId);

        Assert.Equal(StoragePrincipal.KeyForUser(userId), principal.Key);
    }

    [Fact]
    public async Task Invoke_ForAnAnonymousRequest_LeavesThePrincipalEmpty()
    {
        Probe probe = new(TimeSpan.FromSeconds(10));

        (_, StoragePrincipal principal) = await probe.RunAsync("GET", true, null);

        Assert.Null(principal.Key);
    }

    [Fact]
    public async Task Invoke_AfterTheEndpointResolvedAContext_Throws()
    {
        // The contract the middleware relies on: if anything resolved a context before it ran
        // (authentication, say), the intent is fixed and the middleware must not hide that.
        Probe probe = new(TimeSpan.FromSeconds(10));
        await using AsyncServiceScope scope = probe.Provider.CreateAsyncScope();
        StorageReadIntent intent = scope.ServiceProvider.GetRequiredService<StorageReadIntent>();
        intent.ResolveUseReplica(probe.Router);
        DefaultHttpContext context = new() { RequestServices = scope.ServiceProvider };
        context.Request.Method = "GET";
        context.SetEndpoint(
            new Endpoint(
                _ => Task.CompletedTask,
                new EndpointMetadataCollection(new ReadFromReplicaAttribute()),
                "probe"
            )
        );

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new StorageReadIntentMiddleware(_ => Task.CompletedTask).InvokeAsync(
                context,
                intent,
                scope.ServiceProvider.GetRequiredService<StoragePrincipal>()
            )
        );
    }

    [Fact]
    public async Task Invoke_ARequestThatWrote_KeepsTheUserOnThePrimaryFromTheEndOfTheRequest()
    {
        Probe probe = new(TimeSpan.FromSeconds(10));
        Guid userId = Guid.NewGuid();
        string key = StoragePrincipal.KeyForUser(userId);

        // The write happens early in a request that then takes eight seconds.
        await probe.RunAsync(
            "PUT",
            false,
            userId,
            services =>
            {
                services.GetRequiredService<StoragePrincipal>().ReportWrite();
                probe.Clock.Advance(TimeSpan.FromSeconds(8));
                return Task.CompletedTask;
            }
        );

        probe.Clock.Advance(TimeSpan.FromSeconds(5));
        StorageReadIntent next = new();
        next.Set(StorageReadTargetEnum.Replica);
        Assert.False(next.ResolveUseReplica(probe.Router, key));

        probe.Clock.Advance(TimeSpan.FromSeconds(6));
        StorageReadIntent later = new();
        later.Set(StorageReadTargetEnum.Replica);
        Assert.True(later.ResolveUseReplica(probe.Router, key));
    }

    [Fact]
    public async Task Invoke_ForAReadAfterAnotherUsersWrite_StillReadsTheReplica()
    {
        Probe probe = new(TimeSpan.FromSeconds(10));
        Guid writer = Guid.NewGuid();
        Guid reader = Guid.NewGuid();
        await probe.RunAsync(
            "POST",
            false,
            writer,
            services =>
            {
                services.GetRequiredService<StoragePrincipal>().ReportWrite();
                return Task.CompletedTask;
            }
        );

        (StorageReadIntent writerIntent, StoragePrincipal writerPrincipal) = await probe.RunAsync(
            "GET",
            true,
            writer
        );
        (StorageReadIntent readerIntent, StoragePrincipal readerPrincipal) = await probe.RunAsync(
            "GET",
            true,
            reader
        );

        Assert.False(writerIntent.ResolveUseReplica(probe.Router, writerPrincipal.Key));
        Assert.True(readerIntent.ResolveUseReplica(probe.Router, readerPrincipal.Key));
    }

    private IReadOnlyList<ControllerActionDescriptor> Actions() =>
        [
            .. factory
                .Services.GetRequiredService<IActionDescriptorCollectionProvider>()
                .ActionDescriptors.Items.OfType<ControllerActionDescriptor>(),
        ];

    [Fact]
    public void FindMisplacedActions_OnTheRealActions_FindsNone()
    {
        Assert.Empty(ReadFromReplicaEndpointCheck.FindMisplacedActions(Actions()));
    }

    [Fact]
    public void RealActions_WithTheAttribute_AreAllGetActionsAndThereAreSome()
    {
        ControllerActionDescriptor[] marked =
        [
            .. Actions()
                .Where(action => action.EndpointMetadata.OfType<ReadFromReplicaAttribute>().Any()),
        ];

        Assert.NotEmpty(marked);
        Assert.All(
            marked,
            action =>
                Assert.All(
                    action
                        .EndpointMetadata.OfType<IHttpMethodMetadata>()
                        .SelectMany(m => m.HttpMethods),
                    method => Assert.True(HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
                )
        );
    }

    [Fact]
    public void RealActions_OfSignInBackupAndTheCurrentUser_NeverCarryTheAttribute()
    {
        // OD-B11-13-b: sign-in, session, token and backup work needs the current state.
        string[] primaryOnly =
        [
            nameof(AuthController),
            nameof(SsoController),
            nameof(TwoFactorController),
            nameof(BackupController),
            nameof(StorageController),
        ];

        ControllerActionDescriptor[] offenders =
        [
            .. Actions()
                .Where(action =>
                    action.EndpointMetadata.OfType<ReadFromReplicaAttribute>().Any()
                    && (
                        primaryOnly.Contains(action.ControllerTypeInfo.Name)
                        || (
                            action.ControllerTypeInfo.Name == nameof(UsersController)
                            && action.ActionName == nameof(UsersController.GetMine)
                        )
                    )
                ),
        ];

        Assert.Empty(offenders);
    }

    private static ControllerActionDescriptor Descriptor(params object[] metadata) =>
        new()
        {
            ActionName = "Probe",
            ControllerTypeInfo = typeof(ReadFromReplicaTests).GetTypeInfo(),
            EndpointMetadata = metadata,
        };

    [Fact]
    public void FindMisplacedActions_ForAttributedPostPutPatchDeleteOrUnlimitedActions_ReportsThem()
    {
        ControllerActionDescriptor[] actions =
        [
            Descriptor(new ReadFromReplicaAttribute(), new HttpMethodMetadata(["GET"])),
            Descriptor(new ReadFromReplicaAttribute(), new HttpMethodMetadata(["POST"])),
            Descriptor(new ReadFromReplicaAttribute(), new HttpMethodMetadata(["GET", "PUT"])),
            Descriptor(new ReadFromReplicaAttribute()),
            Descriptor(new HttpMethodMetadata(["DELETE"])),
        ];

        IReadOnlyList<string> misplaced = ReadFromReplicaEndpointCheck.FindMisplacedActions(
            actions
        );

        Assert.Equal(3, misplaced.Count);
        Assert.Contains("POST", misplaced[0]);
        Assert.Contains("GET, PUT", misplaced[1]);
        Assert.Contains("any method", misplaced[2]);
    }

    private async Task<HttpClient> AdminClientAsync()
    {
        HttpClient client = factory.CreateClient();
        HttpResponseMessage login = await client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginRequest(
                NinlilDnsWebApplicationFactory.AdminUserName,
                NinlilDnsWebApplicationFactory.AdminPassword
            )
        );
        login.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            (await login.Content.ReadFromJsonAsync<LoginResponse>()).AccessToken
        );
        return client;
    }

    /// <summary>
    ///     A JWT request to an attributed endpoint works: the JWT scheme reads no database before
    ///     the middleware sets the intent.
    /// </summary>
    [Fact]
    public async Task Get_AnAttributedEndpointWithAJwt_Succeeds()
    {
        HttpClient client = await AdminClientAsync();

        HttpResponseMessage response = await client.GetAsync("/api/dns-rewrites");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    ///     An API token request to an attributed endpoint works: the token scheme validates the
    ///     token, reads its owner and builds the claims in a scope of its own, so the request
    ///     scope has resolved no repository when the middleware sets the intent. If this fails
    ///     with a 500, authentication resolved a repository in the request scope.
    /// </summary>
    [Fact]
    public async Task Get_AnAttributedEndpointWithAnApiToken_Succeeds()
    {
        HttpClient admin = await AdminClientAsync();
        UserSummary adminUser = (
            await admin.GetFromJsonAsync<PagedResult<UserSummary>>(
                $"/api/users?search={NinlilDnsWebApplicationFactory.AdminUserName}"
            )
        ).Items.Single(user => user.UserName == NinlilDnsWebApplicationFactory.AdminUserName);

        string rawToken;
        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            rawToken = (
                await scope
                    .ServiceProvider.GetRequiredService<IApiTokenService>()
                    .CreateOneTimeTokenAsync(adminUser.Id, "replica-read", null)
            ).RawToken;

        using HttpRequestMessage request = new(HttpMethod.Get, "/api/dns-rewrites");
        request.Headers.TryAddWithoutValidation("Authorization", $"ApiToken {rawToken}");
        HttpResponseMessage response = await factory.CreateClient().SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
