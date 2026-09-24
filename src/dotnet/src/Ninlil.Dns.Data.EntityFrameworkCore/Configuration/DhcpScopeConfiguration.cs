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
        // Map this entity onto the NinlilDhcpScopes table.
        builder.ToTable("NinlilDhcpScopes");

        // The Id property is the primary key.
        builder.HasKey(scope => scope.Id);

        // A scope's name is required and unique -- two scopes cannot share the same name.
        builder.Property(scope => scope.Name).HasMaxLength(256).IsRequired();
        builder.HasIndex(scope => scope.Name).IsUnique();

        builder.Property(scope => scope.RangeStartAddress).HasMaxLength(45).IsRequired();
        builder.Property(scope => scope.RangeEndAddress).HasMaxLength(45).IsRequired();
        builder.Property(scope => scope.Comments).HasMaxLength(4096);

        // RFC 2131 §2 caps the "file" header field at 128 octets; NextServerAddress is an
        // ordinary IP address, the same 45-character cap every other address field here uses.
        builder.Property(scope => scope.BootFileName).HasMaxLength(128);
        builder.Property(scope => scope.NextServerAddress).HasMaxLength(45);

        // Store the protocol version and scope kind using their short, human-readable names
        // rather than their numeric values, matching every other enum column in this data layer.
        builder.Property(scope => scope.ProtocolVersion).HasConversion<string>().HasMaxLength(16);
        builder.Property(scope => scope.Kind).HasConversion<string>().HasMaxLength(32);

        // Store the two option-delivery address lists as JSON, since none of the four relational
        // providers Ninlil supports offer a portable native array column type -- the same
        // approach ZoneConfiguration already established for ZoneEntity's own address lists.
        builder
            .Property(scope => scope.GatewayAddresses)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());

        builder
            .Property(scope => scope.DnsServerAddresses)
            .HasConversion(new JsonStringListValueConverter(), new JsonStringListValueComparer());
    }
}
