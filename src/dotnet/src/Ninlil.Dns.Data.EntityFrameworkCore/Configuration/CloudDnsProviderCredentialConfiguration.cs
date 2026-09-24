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
        // Map this entity onto the NinlilDnsCloudDnsProviderCredentials table.
        builder.ToTable("NinlilDnsCloudDnsProviderCredentials");

        // The Id property is the primary key.
        builder.HasKey(credential => credential.Id);

        // A credential set's name is required and unique -- an operator picks one, distinct
        // name per credential set, the same way a TSIG key's name must be unique.
        builder.Property(credential => credential.Name).HasMaxLength(255).IsRequired();
        builder.HasIndex(credential => credential.Name).IsUnique();

        // Store the provider type using its short, human-readable name rather than its numeric
        // value.
        builder
            .Property(credential => credential.ProviderType)
            .HasConversion<string>()
            .HasMaxLength(32);

        builder.Property(credential => credential.CredentialsEncrypted).IsRequired();
        builder.Property(credential => credential.Comments).HasMaxLength(4096);
    }
}
