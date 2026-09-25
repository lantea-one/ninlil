using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ninlil.Dns.Data.Abstractions.Entity;

// Define our namespace.
namespace Ninlil.Dns.Data.EntityFrameworkCore.Configuration;

/// <summary>
///     This class maps <see cref="CloudDnsProviderCredentialEntity" /> onto the
///     <c>NinlilDnsCloudDnsProviderCredentials</c> table.
/// </summary>
public class CloudDnsProviderCredentialConfiguration
    : IEntityTypeConfiguration<CloudDnsProviderCredentialEntity>
{
    /// <summary>
    ///     This method configures the entity type.
    /// </summary>
    /// <param name="builder">The builder used to configure the entity type.</param>
    public void Configure(EntityTypeBuilder<CloudDnsProviderCredentialEntity> builder)
    {
        builder.ToTable("NinlilDnsCloudDnsProviderCredentials");

        builder.HasKey(credential => credential.Id);

        // Operators pick credential sets by name, so names are unique.
        builder.Property(credential => credential.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(credential => credential.Name).IsUnique();

        builder
            .Property(credential => credential.ProviderType)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(credential => credential.CredentialsEncrypted).IsRequired();
        builder.Property(credential => credential.Comments).HasMaxLength(4096);
    }
}
