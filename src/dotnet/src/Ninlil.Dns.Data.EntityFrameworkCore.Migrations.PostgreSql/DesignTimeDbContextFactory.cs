using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql;

/// <summary>
///     This class lets the <c>dotnet ef</c> tool create a <see cref="NinlilDnsContext" />
///     configured for PostgreSQL at design time, so <c>dotnet ef migrations add</c> run in
///     this project works against the PostgreSQL migration history only. The connection string is
///     a placeholder; generating a migration only needs the model, not a database.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NinlilDnsContext>
{
    /// <inheritdoc />
    public NinlilDnsContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<NinlilDnsContext> builder = new();

        builder.UseNinlilDnsPostgreSql(
            "Host=localhost;Database=ninlil;Username=ninlil;Password=design-time"
        );

        return new NinlilDnsContext(builder.Options);
    }
}
