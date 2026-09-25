using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql;

/// <summary>
///     This class lets the <c>dotnet ef</c> tool create a <see cref="NinlilDnsContext" />
///     configured for MySQL or MariaDB at design time, so <c>dotnet ef migrations add</c> run in
///     this project works against the MySQL migration history only. The connection string is
///     a placeholder; generating a migration only needs the model, not a database.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NinlilDnsContext>
{
    /// <inheritdoc />
    public NinlilDnsContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<NinlilDnsContext> builder = new();

        // An explicit server version skips Pomelo's version detection, which would need a
        // running server.
        builder.UseNinlilDnsMySql(
            "Server=localhost;Database=ninlil;User=ninlil;Password=design-time;",
            new MySqlServerVersion(new Version(8, 0, 36))
        );

        return new NinlilDnsContext(builder.Options);
    }
}
