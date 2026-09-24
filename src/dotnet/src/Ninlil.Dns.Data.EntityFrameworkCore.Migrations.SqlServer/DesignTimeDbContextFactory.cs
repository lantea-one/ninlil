using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer;

/// <summary>
///     This class lets the <c>dotnet ef</c> command-line tool construct a
///     <see cref="NinlilDnsContext" /> configured for Microsoft SQL Server at design time, so
///     that running <c>dotnet ef migrations add</c> from within this project only ever sees, and
///     only ever scaffolds against, SQL Server's own migration history.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NinlilDnsContext>
{
    /// <inheritdoc />
    public NinlilDnsContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<NinlilDnsContext> builder = new();

        // Design-time migration generation only needs to know the model's shape, not a real
        // database to write to.
        builder.UseNinlilDnsSqlServer(
            "Server=localhost;Database=ninlil;User Id=ninlil;Password=design-time;TrustServerCertificate=True;"
        );

        return new NinlilDnsContext(builder.Options);
    }
}
