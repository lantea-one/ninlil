using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class checks that a cloud DNS provider credential set's own secret-material fields
///     match what its declared <see cref="CloudDnsProviderTypeEnum" /> actually requires --
///     mirroring, but deliberately not reusing, the checks each <see cref="ICloudDnsProvider" />
///     implementation performs for itself at sync time, so a caller managing a credential set
///     through the REST API gets an immediate, specific rejection rather than only discovering a
///     misconfigured credential set once <see cref="CloudDnsZoneSyncService" /> next tries to use
///     it.
/// </summary>
public static class CloudCredentialValidator
{
    /// <summary>
    ///     This method throws a <see cref="ValidationException" /> naming exactly which field is
    ///     missing or empty for the given provider type, or does nothing at all when every field
    ///     that provider type requires is present and non-empty.
    /// </summary>
    /// <param name="providerType">The declared cloud DNS provider a credential set is for.</param>
    /// <param name="credentials">The credential set's own field-value pairs to validate.</param>
    /// <exception cref="ValidationException">
    ///     Thrown when <paramref name="credentials" /> is empty, is missing a field
    ///     <paramref name="providerType" /> requires, or <paramref name="providerType" /> itself
    ///     is not one this project implements a cloud DNS connector for.
    /// </exception>
    public static void ValidateOrThrow(
        CloudDnsProviderTypeEnum providerType,
        IReadOnlyDictionary<string, string> credentials
    )
    {
        if (credentials is null || credentials.Count == 0)
            throw new ValidationException("'credentials' is required.");

        switch (providerType)
        {
            // Cloudflare and DigitalOcean both authenticate a single bearer-style API token, so
            // they share one check.
            case CloudDnsProviderTypeEnum.Cloudflare:
            case CloudDnsProviderTypeEnum.DigitalOcean:
                if (
                    !credentials.TryGetValue("ApiToken", out string apiToken)
                    || string.IsNullOrWhiteSpace(apiToken)
                )
                    throw new ValidationException(
                        $"'credentials' must include a non-empty 'ApiToken' field for {providerType}."
                    );
                return;

            // Route 53 instead authenticates an AWS access key ID paired with its own secret
            // access key, mirroring AWS's own SigV4 credential shape.
            case CloudDnsProviderTypeEnum.Route53:
                if (
                    !credentials.TryGetValue("AccessKeyId", out string accessKeyId)
                    || string.IsNullOrWhiteSpace(accessKeyId)
                    || !credentials.TryGetValue("SecretAccessKey", out string secretAccessKey)
                    || string.IsNullOrWhiteSpace(secretAccessKey)
                )
                    throw new ValidationException(
                        "'credentials' must include non-empty 'AccessKeyId' and 'SecretAccessKey' fields for Route53."
                    );
                return;

            default:
                throw new ValidationException(
                    $"'{providerType}' is not a supported cloud DNS provider type."
                );
        }
    }
}
