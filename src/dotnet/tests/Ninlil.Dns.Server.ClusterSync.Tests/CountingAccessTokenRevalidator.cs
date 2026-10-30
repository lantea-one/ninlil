using System.Security.Claims;
using Ninlil.Dns.Identity.Authentication;

// Define our namespace.
namespace Ninlil.Dns.Server.ClusterSync.Tests;

/// <summary>
///     This class is a fake <see cref="IAccessTokenRevalidator" /> that only counts how often the
///     access-token cache was flushed, so the identity applier tests can check that an applied
///     envelope flushes it. It never revalidates anything.
/// </summary>
public sealed class CountingAccessTokenRevalidator : IAccessTokenRevalidator
{
    /// <summary>
    ///     This property contains how many times <see cref="Invalidate" /> was called.
    /// </summary>
    public int InvalidateCount { get; private set; }

    /// <inheritdoc />
    public Task<bool> RevalidateAsync(
        ClaimsPrincipal principal,
        CancellationToken stoppingToken = default
    ) => Task.FromResult(true);

    /// <inheritdoc />
    public void Invalidate() => InvalidateCount++;
}
