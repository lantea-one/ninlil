using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="TsigKeyEntity" /> onto the <c>NinlilDnsTsigKeys</c> table.
/// </summary>
public class TsigKeyConfiguration : IEntityTypeConfiguration<TsigKeyEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<TsigKeyEntity> builder)
    {
        builder.ToTable("NinlilDnsTsigKeys");

        builder.HasKey(key => key.Id);

        // A signed message names one key, so key names are unique.
        builder.Property(key => key.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(key => key.Name).IsUnique();

        builder.Property(key => key.AlgorithmName).HasMaxLength(255).IsRequired();
        builder.Property(key => key.SecretEncrypted).IsRequired();
        builder.Property(key => key.Comments).HasMaxLength(4096);
    }
}
