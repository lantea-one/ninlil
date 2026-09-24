using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Ninlil.Dns.Identity;
using Ninlil.Dns.Identity.Authorization;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

public class PermissionAuthorizationHandlerTests
{
    private static async Task<bool> EvaluateAsync(ClaimsPrincipal user, string requiredPermission)
    {
        PermissionRequirement requirement = new(requiredPermission);
        AuthorizationHandlerContext context = new([requirement], user, null);

        PermissionAuthorizationHandler handler = new();
        await handler.HandleAsync(context);

        return context.HasSucceeded;
    }

    private static ClaimsPrincipal MakeUser(params string[] permissions)
    {
        ClaimsIdentity identity = new("Test");
        foreach (string permission in permissions)
            identity.AddClaim(new Claim(NinlilDnsClaimTypes.Permission, permission));

        return new ClaimsPrincipal(identity);
    }

    [Fact]
    public async Task SucceedsWhenTheCallerHoldsTheExactPermission()
    {
        ClaimsPrincipal user = MakeUser("zones:write");

        Assert.True(await EvaluateAsync(user, "zones:write"));
    }

    [Fact]
    public async Task FailsWhenTheCallerHoldsADifferentPermission()
    {
        ClaimsPrincipal user = MakeUser("zones:read");

        Assert.False(await EvaluateAsync(user, "zones:write"));
    }

    [Fact]
    public async Task SucceedsWhenTheCallerHoldsTheWildcardPermission()
    {
        ClaimsPrincipal user = MakeUser(PermissionAuthorizationHandler.WildcardPermission);

        Assert.True(await EvaluateAsync(user, "zones:write"));
        Assert.True(await EvaluateAsync(user, "roles:delete"));
    }

    [Fact]
    public async Task FailsWhenTheCallerHoldsNoPermissionsAtAll()
    {
        ClaimsPrincipal user = MakeUser();

        Assert.False(await EvaluateAsync(user, "zones:write"));
    }
}
