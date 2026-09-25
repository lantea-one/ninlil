using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer;

/// <summary>
///     This class lets the <c>dotnet ef</c> tool create a <see cref="NinlilDnsContext" />
///     configured for Microsoft SQL Server at design time, so <c>dotnet ef migrations add</c> run in
///     this project works against the SQL Server migration history only. The connection string is
///     a placeholder; generating a migration only needs the model, not a database.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NinlilDnsContext>
{
    /// <inheritdoc />
    public NinlilDnsContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<NinlilDnsContext> builder = new();

        builder.UseNinlilDnsSqlServer(
            "Server=localhost;Database=ninlil;User Id=ninlil;Password=design-time;TrustServerCertificate=True;"
        );

        return new NinlilDnsContext(builder.Options);
    }
}
