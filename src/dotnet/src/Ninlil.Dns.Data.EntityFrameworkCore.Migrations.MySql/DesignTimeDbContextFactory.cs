using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Ninlil.Dns.Data.EntityFrameworkCore;
using Ninlil.Dns.Data.EntityFrameworkCore.Extension;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql;

/// <summary>
///     This class lets the <c>dotnet ef</c> command-line tool construct a
///     <see cref="NinlilDnsContext" /> configured for MySQL/MariaDB at design time, so that
///     running <c>dotnet ef migrations add</c> from within this project only ever sees, and only
///     ever scaffolds against, MySQL/MariaDB's own migration history.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<NinlilDnsContext>
{
    /// <inheritdoc />
    public NinlilDnsContext CreateDbContext(string[] args)
    {
        DbContextOptionsBuilder<NinlilDnsContext> builder = new();

        // Design-time migration generation only needs to know the model's shape, not a real
        // database to write to, so an explicit server version sidesteps Pomelo's usual
        // auto-detection round trip to a live server.
        builder.UseNinlilDnsMySql(
            "Server=localhost;Database=ninlil;User=ninlil;Password=design-time;",
            new MySqlServerVersion(new Version(8, 0, 36))
        );

        return new NinlilDnsContext(builder.Options);
    }
}
