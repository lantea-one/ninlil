using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;
using Ninlil.Dns.Data.EntityFrameworkCore.Repository;
using Ninlil.Dns.Identity.Sso;

// Define our namespace.
namespace Ninlil.Dns.Identity.Tests;

/// <summary>
///     This class proves <see cref="OidcUserProvisioningService" /> actually round-trips a
///     <see cref="UserEntity" /> against a real SQLite database -- not just "the code compiles
///     against <see cref="Ninlil.Dns.Data.Abstractions.Repository.IUserRepository" />'s own
///     generic shape" -- the same real-database standard
///     <c>Server.AccessControl.Tests.DnsAccessControlRuleRepositoryTests</c> already establishes.
/// </summary>
public sealed class OidcUserProvisioningServiceTests : IDisposable
{
    private readonly string _databasePath = Path.Combine(
        Path.GetTempPath(),
        $"ninlil-dns-oidc-provisioning-tests-{Guid.NewGuid():N}.sqlite3"
    );

    private readonly NinlilDnsContext _context;
    private readonly UserRepository _userRepository;
    private readonly EntityFrameworkUnitOfWork _unitOfWork;
    private readonly OidcUserProvisioningService _service;

    public OidcUserProvisioningServiceTests()
    {
        DbContextOptionsBuilder<NinlilDnsContext> optionsBuilder = new();
        optionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");

        _context = new NinlilDnsContext(optionsBuilder.Options);
        _context.Database.EnsureCreated();

        _userRepository = new UserRepository(_context);
        _unitOfWork = new EntityFrameworkUnitOfWork(_context);
        _service = new OidcUserProvisioningService(_userRepository, _unitOfWork);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _context.Dispose();

        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }

    /// <summary>
    ///     This method builds the <see cref="ClaimsPrincipal" /> shape
    ///     <c>OpenIdConnectOptions.Events.OnTokenValidated</c> would hand
    ///     <see cref="IOidcUserProvisioningService.FindOrProvisionUserAsync" /> after a real
    ///     handshake -- claims under their own real OIDC short names, matching
    ///     <c>MapInboundClaims = false</c> (see <c>ServiceCollectionAuthenticationExtension</c>'s
    ///     own doc comment).
    /// </summary>
    private static ClaimsPrincipal BuildPrincipal(
        string subject,
        string preferredUsername = null,
        string name = null,
        string email = null
    )
    {
        List<Claim> claims = [new Claim(JwtRegisteredClaimNames.Sub, subject)];

        if (preferredUsername is not null)
            claims.Add(new Claim("preferred_username", preferredUsername));
        if (name is not null)
            claims.Add(new Claim("name", name));
        if (email is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc"));
    }

    [Fact]
    public async Task FirstSignInProvisionsANewAccountWithNoRolesAndNoPasswordHash()
    {
        ClaimsPrincipal principal = BuildPrincipal(
            "subject-1",
            preferredUsername: "alice",
            name: "Alice Example",
            email: "alice@example.com"
        );

        UserEntity user = await _service.FindOrProvisionUserAsync(principal);
        await _context.SaveChangesAsync();

        Assert.Equal("alice", user.UserName);
        Assert.Equal("ALICE", user.NormalizedUserName);
        Assert.Equal("Alice Example", user.DisplayName);
        Assert.Equal("alice@example.com", user.Email);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PasswordHash);
        Assert.Equal(OidcUserProvisioningService.ProviderName, user.ExternalIdentityProvider);
        Assert.Equal("subject-1", user.ExternalSubjectId);

        // A freshly-provisioned account has been granted absolutely nothing yet -- confirmed
        // against the real repository, not just against the in-memory object this method
        // returned.
        IReadOnlyList<RoleEntity> roles = await _userRepository.ListRolesAsync(user.Id);
        IReadOnlyList<UserClaimEntity> directClaims = await _userRepository.ListClaimsAsync(
            user.Id
        );
        Assert.Empty(roles);
        Assert.Empty(directClaims);
    }

    [Fact]
    public async Task FirstSignInWithNoEmailClaimSynthesizesAnUnconfirmedPlaceholderAddress()
    {
        ClaimsPrincipal principal = BuildPrincipal("subject-no-email", preferredUsername: "bob");

        UserEntity user = await _service.FindOrProvisionUserAsync(principal);
        await _context.SaveChangesAsync();

        Assert.Equal("subject-no-email@sso.invalid", user.Email);
        Assert.False(user.EmailConfirmed);
    }

    [Fact]
    public async Task FirstSignInWithNoPreferredUsernameClaimFallsBackToTheSubject()
    {
        ClaimsPrincipal principal = BuildPrincipal("subject-bare");

        UserEntity user = await _service.FindOrProvisionUserAsync(principal);
        await _context.SaveChangesAsync();

        Assert.Equal("subject-bare", user.UserName);

        // No 'name' claim was supplied either -- the display name falls back to the user name.
        Assert.Equal("subject-bare", user.DisplayName);
    }

    [Fact]
    public async Task SecondSignInMatchesTheExistingAccountBySubjectEvenIfTheEmailClaimChanged()
    {
        ClaimsPrincipal firstPrincipal = BuildPrincipal(
            "subject-2",
            preferredUsername: "carol",
            email: "carol@old-example.com"
        );
        UserEntity firstResult = await _service.FindOrProvisionUserAsync(firstPrincipal);
        await _context.SaveChangesAsync();

        // A fresh context/repository, standing in for a fresh scope/request, confirms this
        // second lookup is a real database read, not merely still-tracked in-memory state (the
        // codebase's own bug catalog, entry 3).
        DbContextOptionsBuilder<NinlilDnsContext> freshOptionsBuilder = new();
        freshOptionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");
        await using NinlilDnsContext freshContext = new(freshOptionsBuilder.Options);
        UserRepository freshRepository = new(freshContext);
        EntityFrameworkUnitOfWork freshUnitOfWork = new(freshContext);
        OidcUserProvisioningService freshService = new(freshRepository, freshUnitOfWork);

        // The identity provider now reports a different e-mail address for the exact same
        // subject -- this must still resolve to the same local account, and must not silently
        // overwrite the operator-visible fields already on file for it.
        ClaimsPrincipal secondPrincipal = BuildPrincipal(
            "subject-2",
            preferredUsername: "carol",
            email: "carol@new-example.com"
        );
        UserEntity secondResult = await freshService.FindOrProvisionUserAsync(secondPrincipal);

        Assert.Equal(firstResult.Id, secondResult.Id);
        Assert.Equal("carol@old-example.com", secondResult.Email);

        // No second account was ever created for this subject.
        IReadOnlyList<UserEntity> allUsers = await freshRepository.ListAsync();
        Assert.Single(allUsers);
    }

    [Fact]
    public async Task TwoDifferentSubjectsRequestingTheSameUserNameGetSuffixedApart()
    {
        ClaimsPrincipal firstPrincipal = BuildPrincipal(
            "subject-dave-1",
            preferredUsername: "dave"
        );
        UserEntity firstUser = await _service.FindOrProvisionUserAsync(firstPrincipal);
        await _context.SaveChangesAsync();

        ClaimsPrincipal secondPrincipal = BuildPrincipal(
            "subject-dave-2",
            preferredUsername: "dave"
        );
        UserEntity secondUser = await _service.FindOrProvisionUserAsync(secondPrincipal);
        await _context.SaveChangesAsync();

        Assert.Equal("dave", firstUser.UserName);
        Assert.Equal("dave-2", secondUser.UserName);
        Assert.NotEqual(firstUser.Id, secondUser.Id);
    }

    [Fact]
    public async Task AMissingSubClaimThrows()
    {
        ClaimsPrincipal principal = new(new ClaimsIdentity([new Claim("name", "No Subject")]));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.FindOrProvisionUserAsync(principal)
        );
    }
}
