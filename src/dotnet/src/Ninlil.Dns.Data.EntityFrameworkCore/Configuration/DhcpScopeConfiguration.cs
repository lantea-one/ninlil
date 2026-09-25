using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dhcp.Data.Abstractions.Entity;
using Ninlil.Dns.Data.EntityFrameworkCore.Conversion;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="DhcpScopeEntity" /> onto the <c>NinlilDhcpScopes</c> table.
/// </summary>
public class DhcpScopeConfiguration : IEntityTypeConfiguration<DhcpScopeEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<DhcpScopeEntity> builder)
    {
        builder.ToTable("NinlilDhcpScopes");

        builder.HasKey(scope => scope.Id);

        builder.Property(scope => scope.Name).HasMaxLength(256).IsRequired();
        builder.HasIndex(scope => scope.Name).IsUnique();

        builder.Property(scope => scope.RangeStartAddress).HasMaxLength(45).IsRequired();
        builder.Property(scope => scope.RangeEndAddress).HasMaxLength(45).IsRequired();
        builder.Property(scope => scope.Comments).HasMaxLength(4096);

        // RFC 2131 §2 caps the "file" header field at 128 octets. NextServerAddress is an IP
        // address, capped at 45 characters like the other address fields.
        builder.Property(scope => scope.BootFileName).HasMaxLength(128);
        builder.Property(scope => scope.NextServerAddress).HasMaxLength(45);

        builder.Property(scope => scope.ProtocolVersion).HasConversion<string>().HasMaxLength(16);
        builder.Property(scope => scope.Kind).HasConversion<string>().HasMaxLength(32);

        builder
            .Property(scope => scope.GatewayAddresses)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());

        builder
            .Property(scope => scope.DnsServerAddresses)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());
    }
}
