using Ninlil.Dns.Common.Enum;
using Ninlil.Dns.Common.Exceptions;

// Define our namespace.
namespace Ninlil.Dns.Server.Authoritative.CloudSync;

/// <summary>
///     This class checks that a credential set has the fields its
///     <see cref="CloudDnsProviderTypeEnum" /> needs. Each <see cref="ICloudDnsProvider" /> checks
///     again at sync time; checking here lets the REST API reject a bad set at once instead of
///     <see cref="CloudDnsZoneSyncService" /> failing later.
/// </summary>
public static class CloudCredentialValidator
{
    /// <summary>
    ///     This method throws a <see cref="ValidationException" /> naming the missing or empty field,
    ///     or returns when every required field is present.
    /// </summary>
    /// <param name="providerType">The provider the credential set is for.</param>
    /// <param name="credentials">The credential fields to validate.</param>
    /// <exception cref="ValidationException">
    ///     Thrown when <paramref name="credentials" /> is empty or lacks a required field, or
    ///     <paramref name="providerType" /> has no connector.
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

            // Route 53 needs an AWS access key ID and secret access key (SigV4).
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
