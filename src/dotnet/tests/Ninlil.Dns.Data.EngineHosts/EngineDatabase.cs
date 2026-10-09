using Microsoft.Extensions.DependencyInjection;
using Ninlil.Dns.Data.EntityFrameworkCore;

// Define our namespace.
namespace Ninlil.Dns.Data.EngineHosts;

/// <summary>
///     This class is one database on a container started by an
///     <see cref="EngineHost" />, together with the service provider that talks to it. A
///     caller creates one with <see cref="EngineHost.CreateDatabaseAsync" />, uses
///     <see cref="Services" /> (or <see cref="CreateContext" /> on a relational engine) and
///     disposes it, which disposes every scope handed out.
/// </summary>
public sealed class EngineDatabase(ServiceProvider services, string connectionString)
    : IAsyncDisposable
{
    private readonly List<AsyncServiceScope> _scopes = [];

    /// <summary>
    ///     This property contains the service provider built with the storage extensions for the
    ///     engine, pointed at this database.
    /// </summary>
    public ServiceProvider Services => services;

    /// <summary>
    ///     This property contains the connection string of this database.
    /// </summary>
    public string ConnectionString => connectionString;

    /// <summary>
    ///     This method resolves a <see cref="NinlilDnsContext" /> from a new scope that lives
    ///     until the database is disposed. It throws on MongoDB, which has no EF context.
    /// </summary>
    public NinlilDnsContext CreateContext()
    {
        AsyncServiceScope scope = services.CreateAsyncScope();
        _scopes.Add(scope);
        return scope.ServiceProvider.GetRequiredService<NinlilDnsContext>();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        foreach (AsyncServiceScope scope in _scopes)
            await scope.DisposeAsync();

        await services.DisposeAsync();
    }
}
