using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Data.Abstractions.Entity;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.Tests.Repository;

/// <summary>
///     This class tests the paging of <see cref="Ninlil.Dns.Data.EntityFrameworkCore.Repository.CloudDnsProviderCredentialRepository" /> (<c>SearchAsync</c>, <c>CountAsync</c>) against a
///     temporary SQLite database (<see cref="RepositorySearchTestHost" />): the page is cut by the
///     database with <c>ORDER BY</c>/<c>OFFSET</c>/<c>LIMIT</c>, as <c>CloudProviderCredentialsController</c>'s listing needs, rather
///     than in memory.
/// </summary>
public sealed class CloudDnsProviderCredentialRepositorySearchTests : IAsyncLifetime
{
    private readonly RepositorySearchTestHost _testHost = new();

    public Task InitializeAsync() => _testHost.InitializeAsync();

    public Task DisposeAsync() => _testHost.DisposeAsync().AsTask();

    /// <summary>
    ///     This method seeds five credential sets with names out of alphabetical
    ///     creation order, so a passing ordering assertion can only be explained by a real
    ///     <c>ORDER BY</c> translated at the database, not by creation/insertion order happening
    ///     to already match.
    /// </summary>
    private async Task SeedAsync()
    {
        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        ICloudDnsProviderCredentialRepository repository =
            scope.ServiceProvider.GetRequiredService<ICloudDnsProviderCredentialRepository>();
        IUnitOfWork unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        string[] namesInSeedOrder =
        [
            "delta-account",
            "bravo-account",
            "echo-account",
            "alpha-account",
            "charlie-account",
        ];

        foreach (string name in namesInSeedOrder)
            await repository.AddAsync(
                new CloudDnsProviderCredentialEntity
                {
                    Id = Guid.NewGuid(),
                    CreatedAtUtc = DateTimeOffset.UtcNow,
                    Name = name,
                    ProviderType = CloudDnsProviderTypeEnum.Cloudflare,
                    CredentialsEncrypted = "encrypted-credentials",
                }
            );

        await unitOfWork.SaveChangesAsync();
    }

    [Fact]
    public async Task SearchAsync_ReturnsEveryCredentialSetOrderedByNameRegardlessOfSeedOrder()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        ICloudDnsProviderCredentialRepository repository =
            scope.ServiceProvider.GetRequiredService<ICloudDnsProviderCredentialRepository>();

        IReadOnlyList<CloudDnsProviderCredentialEntity> results = await repository.SearchAsync(
            skip: 0,
            take: 50
        );

        Assert.Equal(5, results.Count);
        Assert.Equal(
            ["alpha-account", "bravo-account", "charlie-account", "delta-account", "echo-account"],
            [.. results.Select(credential => credential.Name)]
        );
    }

    [Fact]
    public async Task SearchAsync_Paginated_ReturnsTheCorrectSliceForEachPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        ICloudDnsProviderCredentialRepository repository =
            scope.ServiceProvider.GetRequiredService<ICloudDnsProviderCredentialRepository>();

        IReadOnlyList<CloudDnsProviderCredentialEntity> firstPage = await repository.SearchAsync(
            skip: 0,
            take: 2
        );
        IReadOnlyList<CloudDnsProviderCredentialEntity> thirdPage = await repository.SearchAsync(
            skip: 4,
            take: 2
        );

        Assert.Equal(["alpha-account", "bravo-account"], [.. firstPage.Select(c => c.Name)]);
        Assert.Equal(["echo-account"], [.. thirdPage.Select(c => c.Name)]);
        Assert.DoesNotContain(
            thirdPage,
            credential => firstPage.Any(first => first.Id == credential.Id)
        );
    }

    [Fact]
    public async Task CountAsync_ReflectsTheFullCredentialCountRegardlessOfPageSize()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        ICloudDnsProviderCredentialRepository repository =
            scope.ServiceProvider.GetRequiredService<ICloudDnsProviderCredentialRepository>();

        int count = await repository.CountAsync();
        IReadOnlyList<CloudDnsProviderCredentialEntity> onePage = await repository.SearchAsync(
            skip: 0,
            take: 2
        );

        Assert.Equal(5, count);
        Assert.Equal(2, onePage.Count);
    }

    [Fact]
    public async Task SearchAsync_WithSkipBeyondTheEnd_ReturnsAnEmptyPage()
    {
        await SeedAsync();

        await using AsyncServiceScope scope = _testHost.Host.Services.CreateAsyncScope();
        ICloudDnsProviderCredentialRepository repository =
            scope.ServiceProvider.GetRequiredService<ICloudDnsProviderCredentialRepository>();

        IReadOnlyList<CloudDnsProviderCredentialEntity> results = await repository.SearchAsync(
            skip: 100,
            take: 50
        );

        Assert.Empty(results);
    }
}
