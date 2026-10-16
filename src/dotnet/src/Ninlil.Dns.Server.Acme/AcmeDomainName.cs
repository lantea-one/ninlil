using Ninlil.Dns.Common.Wire;

// Define our namespace.
namespace Ninlil.Dns.Server.Acme;

/// <summary>
///     This class holds the host-name rules for an ACME certificate domain. It is shared by
///     <c>AcmeSettingsController</c> (400 on write) and <see cref="AcmeCertificateIssuer" />
///     (refuses before ordering), so a domain from a hand-edited configuration file is checked
///     too. The rules are the RFC 1123 §2.1 letter, digit and hyphen labels, plus an optional
///     leading <c>*.</c> label for a wildcard certificate (RFC 8555 §7.1.3). A DNS name may carry
///     any octet (RFC 2181 §11), so <see cref="DnsName.TryParse(string,out DnsName)" /> alone is
///     too loose for a certificate name.
/// </summary>
public static class AcmeDomainName
{
    /// <summary>
    ///     This constant contains the longest domain name, in characters without a trailing dot
    ///     (RFC 1035 §2.3.4: 255 octets on the wire, 253 as text).
    /// </summary>
    private const int MaximumNameLength = 253;

    /// <summary>
    ///     This constant contains the longest label (RFC 1035 §2.3.4).
    /// </summary>
    private const int MaximumLabelLength = 63;

    /// <summary>
    ///     This method checks <paramref name="domain" /> and splits out the name the CA validates.
    /// </summary>
    /// <param name="domain">The configured domain; surrounding white space and one trailing dot are ignored.</param>
    /// <param name="normalized">The lowercase name without a trailing dot; a leading <c>*.</c> is kept.</param>
    /// <param name="challengeBase">
    ///     The name the CA validates: the base name of a wildcard (the name after <c>*.</c>),
    ///     otherwise the domain itself.
    /// </param>
    /// <param name="error">The reason for a refusal, suitable for a 400 response; <see langword="null" /> on success.</param>
    /// <returns><see langword="true" /> when the domain is a valid certificate name.</returns>
    public static bool TryParse(
        string domain,
        out string normalized,
        out DnsName challengeBase,
        out string error
    )
    {
        normalized = null;
        challengeBase = null;
        error = null;

        string name = domain?.Trim();

        if (string.IsNullOrEmpty(name))
        {
            error = "The domain name is empty.";
            return false;
        }

        if (name.EndsWith('.'))
            name = name[..^1];

        name = name.ToLowerInvariant();

        if (name.Length == 0 || name.Length > MaximumNameLength)
        {
            error =
                $"'{domain}' is not a valid domain name: it must be 1 to {MaximumNameLength} characters.";
            return false;
        }

        bool wildcard = name.StartsWith("*.", StringComparison.Ordinal);
        string baseName = wildcard ? name[2..] : name;
        string[] labels = baseName.Split('.');

        // A single label (or a wildcard directly under a top-level label) is never a certificate
        // name a CA issues.
        if (labels.Length < 2)
        {
            error = $"'{domain}' is not a valid domain name: it needs at least two labels.";
            return false;
        }

        foreach (string label in labels)
        {
            if (!IsValidLabel(label, out string labelError))
            {
                error = $"'{domain}' is not a valid domain name: {labelError}";
                return false;
            }
        }

        normalized = name;
        challengeBase = DnsName.Parse(baseName);
        return true;
    }

    /// <summary>
    ///     This method checks one label: 1 to 63 characters of <c>a-z 0-9 -</c> that neither
    ///     starts nor ends with a hyphen. An <c>xn--</c> A-label passes because it is plain ASCII.
    /// </summary>
    private static bool IsValidLabel(string label, out string error)
    {
        error = null;

        if (label.Length == 0)
        {
            error = "a label is empty.";
            return false;
        }

        if (label.Length > MaximumLabelLength)
        {
            error = $"a label is longer than {MaximumLabelLength} characters.";
            return false;
        }

        foreach (char character in label)
        {
            if (character > 127)
            {
                error = "a label has non-ASCII characters; use the xn-- form.";
                return false;
            }

            bool allowed = character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-';

            if (!allowed)
            {
                error =
                    $"'{character}' is not allowed; labels use letters, digits and hyphens only"
                    + " (a wildcard is only a leading '*.').";
                return false;
            }
        }

        if (label[0] == '-' || label[^1] == '-')
        {
            error = "a label starts or ends with a hyphen.";
            return false;
        }

        return true;
    }
}
