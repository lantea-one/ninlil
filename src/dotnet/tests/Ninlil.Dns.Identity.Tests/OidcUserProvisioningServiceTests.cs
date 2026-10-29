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
///     This class tests <see cref="OidcUserProvisioningService" /> against a SQLite database
///     through the Entity Framework Core <see cref="Ninlil.Dns.Data.Abstractions.Repository.IUserRepository" />,
///     so accounts are written and read back for real: first sign-in, missing claims, repeat
///     sign-in and user-name collisions.
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

        TemporaryFiles.DeleteSqliteDatabase(_databasePath);
    }

    /// <summary>
    ///     This method builds the <see cref="ClaimsPrincipal" /> that
    ///     <c>OpenIdConnectOptions.Events.OnTokenValidated</c> passes to
    ///     <see cref="IOidcUserProvisioningService.FindOrProvisionUserAsync" />, with claims under
    ///     their OIDC short names because <c>MapInboundClaims = false</c> (see
    ///     <c>ServiceCollectionAuthenticationExtension</c>).
    /// </summary>
    private static ClaimsPrincipal BuildPrincipal(
        string subject,
        string preferredUsername = null,
        string name = null,
        string email = null,
        string emailVerified = null
    )
    {
        List<Claim> claims = [new Claim(JwtRegisteredClaimNames.Sub, subject)];

        if (preferredUsername is not null)
            claims.Add(new Claim("preferred_username", preferredUsername));
        if (name is not null)
            claims.Add(new Claim("name", name));
        if (email is not null)
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, email));
        if (emailVerified is not null)
            claims.Add(new Claim("email_verified", emailVerified));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "oidc"));
    }

    [Fact]
    public async Task FirstSignInProvisionsANewAccountWithNoRolesAndNoPasswordHash()
    {
        ClaimsPrincipal principal = BuildPrincipal(
            "subject-1",
            preferredUsername: "alice",
            name: "Alice Example",
            email: "alice@example.com",
            emailVerified: "true"
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

        // A new account has no roles or claims, checked in the database rather than on the
        // returned object.
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

        // No 'name' claim either, so the display name falls back to the user name.
        Assert.Equal("subject-bare", user.DisplayName);
    }

    [Fact]
    public async Task SecondSignInMatchesTheExistingAccountBySubjectEvenIfTheEmailClaimChanged()
    {
        ClaimsPrincipal firstPrincipal = BuildPrincipal(
            "subject-2",
            preferredUsername: "carol",
            email: "carol@old-example.com",
            emailVerified: "true"
        );
        UserEntity firstResult = await _service.FindOrProvisionUserAsync(firstPrincipal);
        await _context.SaveChangesAsync();

        // A new context and repository (a new request) make sure the second lookup reads the
        // database rather than tracked in-memory state.
        DbContextOptionsBuilder<NinlilDnsContext> freshOptionsBuilder = new();
        freshOptionsBuilder.UseNinlilDnsSqlite($"Data Source={_databasePath}");
        await using NinlilDnsContext freshContext = new(freshOptionsBuilder.Options);
        UserRepository freshRepository = new(freshContext);
        EntityFrameworkUnitOfWork freshUnitOfWork = new(freshContext);
        OidcUserProvisioningService freshService = new(freshRepository, freshUnitOfWork);

        // The provider now reports a different e-mail for the same subject: it must resolve to
        // the same account and must not overwrite the stored fields.
        ClaimsPrincipal secondPrincipal = BuildPrincipal(
            "subject-2",
            preferredUsername: "carol",
            email: "carol@new-example.com"
        );
        UserEntity secondResult = await freshService.FindOrProvisionUserAsync(secondPrincipal);

        Assert.Equal(firstResult.Id, secondResult.Id);
        Assert.Equal("carol@old-example.com", secondResult.Email);

        // No second account was created.
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

    private async Task<UserEntity> AddLocalUserAsync(
        string userName,
        string email,
        Action<UserEntity> change = null
    )
    {
        UserEntity user = new()
        {
            Id = Guid.NewGuid(),
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UserName = userName,
            NormalizedUserName = userName.ToUpperInvariant(),
            DisplayName = userName,
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            SecurityStamp = Guid.NewGuid().ToString("N"),
            ConcurrencyStamp = Guid.NewGuid().ToString(),
        };
        change?.Invoke(user);
        await _userRepository.AddAsync(user);
        await _context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task ADisabledLinkedAccountIsRefused()
    {
        await _service.FindOrProvisionUserAsync(BuildPrincipal("subject-disabled"));
        UserEntity user = await _userRepository.FindByExternalSubjectAsync(
            OidcUserProvisioningService.ProviderName,
            "subject-disabled"
        );
        user.IsDisabled = true;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<OidcSignInRefusedException>(() =>
            _service.FindOrProvisionUserAsync(BuildPrincipal("subject-disabled"))
        );
    }

    [Fact]
    public async Task ALockedOutLinkedAccountIsRefusedUntilTheLockoutEnds()
    {
        await _service.FindOrProvisionUserAsync(BuildPrincipal("subject-locked"));
        UserEntity user = await _userRepository.FindByExternalSubjectAsync(
            OidcUserProvisioningService.ProviderName,
            "subject-locked"
        );
        user.LockoutEnabled = true;
        user.LockoutEndUtc = DateTimeOffset.UtcNow.AddHours(1);
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<OidcSignInRefusedException>(() =>
            _service.FindOrProvisionUserAsync(BuildPrincipal("subject-locked"))
        );

        user.LockoutEndUtc = DateTimeOffset.UtcNow.AddMinutes(-1);
        await _context.SaveChangesAsync();

        UserEntity allowed = await _service.FindOrProvisionUserAsync(
            BuildPrincipal("subject-locked")
        );
        Assert.Equal(user.Id, allowed.Id);
    }

    [Fact]
    public async Task AVerifiedEmailOfAnExistingLocalAccountIsRefusedAndNothingIsCreated()
    {
        await AddLocalUserAsync("erin", "erin@example.com");

        await Assert.ThrowsAsync<OidcSignInRefusedException>(() =>
            _service.FindOrProvisionUserAsync(
                BuildPrincipal(
                    "subject-erin",
                    preferredUsername: "erin-sso",
                    email: "Erin@Example.com",
                    emailVerified: "true"
                )
            )
        );

        Assert.Single(await _userRepository.ListAsync());
    }

    [Theory]
    [InlineData("false")]
    [InlineData(null)]
    public async Task AnUnverifiedEmailNeverLinksToOrCopiesAnExistingAccount(string emailVerified)
    {
        UserEntity local = await AddLocalUserAsync("frank", "frank@example.com");

        UserEntity provisioned = await _service.FindOrProvisionUserAsync(
            BuildPrincipal(
                "subject-frank",
                preferredUsername: "frank-sso",
                email: "frank@example.com",
                emailVerified: emailVerified
            )
        );
        await _context.SaveChangesAsync();

        Assert.NotEqual(local.Id, provisioned.Id);
        Assert.Equal("subject-frank@sso.invalid", provisioned.Email);
        Assert.False(provisioned.EmailConfirmed);
        Assert.Equal("frank@example.com", local.Email);
        Assert.Null(local.ExternalSubjectId);
    }

    [Fact]
    public async Task AUserNameOfAnExistingLocalAccountGetsASuffixInsteadOfADuplicate()
    {
        await AddLocalUserAsync("grace", "grace@example.com");

        UserEntity provisioned = await _service.FindOrProvisionUserAsync(
            BuildPrincipal("subject-grace", preferredUsername: "Grace")
        );

        Assert.Equal("Grace-2", provisioned.UserName);
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
