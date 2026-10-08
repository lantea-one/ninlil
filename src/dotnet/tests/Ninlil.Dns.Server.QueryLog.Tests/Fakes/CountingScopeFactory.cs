using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.Abstractions.Repository;

// Define our namespace.
namespace Ninlil.Dns.Server.QueryLog.Tests.Fakes;

/// <summary>
///     This class wraps an <see cref="IServiceScopeFactory" /> so that the
///     <see cref="IUnitOfWork" /> of every scope it creates counts its
///     <see cref="IUnitOfWork.SaveChangesAsync" /> calls in <see cref="SaveChangesCount" />, then
///     saves through the real unit of work. The writer tests use it to show how many batches were
///     saved.
/// </summary>
/// <param name="inner">The factory that creates the real scopes.</param>
public sealed class CountingScopeFactory(IServiceScopeFactory inner) : IServiceScopeFactory
{
    private int _saveChangesCount;

    /// <summary>
    ///     This property contains how many saves went through scopes of this factory.
    /// </summary>
    public int SaveChangesCount => Volatile.Read(ref _saveChangesCount);

    /// <inheritdoc />
    public IServiceScope CreateScope() => new CountingScope(inner.CreateAsyncScope(), this);

    private sealed class CountingScope(AsyncServiceScope inner, CountingScopeFactory owner)
        : IServiceScope,
            IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } =
            new CountingProvider(inner.ServiceProvider, owner);

        public void Dispose() => inner.Dispose();

        public ValueTask DisposeAsync() => inner.DisposeAsync();
    }

    private sealed class CountingProvider(IServiceProvider inner, CountingScopeFactory owner)
        : IServiceProvider
    {
        public object GetService(Type serviceType) =>
            serviceType == typeof(IUnitOfWork)
                ? new CountingUnitOfWork(inner.GetRequiredService<IUnitOfWork>(), owner)
                : inner.GetService(serviceType);
    }

    private sealed class CountingUnitOfWork(IUnitOfWork inner, CountingScopeFactory owner)
        : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken stoppingToken = default)
        {
            Interlocked.Increment(ref owner._saveChangesCount);
            return inner.SaveChangesAsync(stoppingToken);
        }
    }
}
