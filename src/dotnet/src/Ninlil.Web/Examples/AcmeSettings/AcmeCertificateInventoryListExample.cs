using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AcmeSettings;

/// <summary>
///     This class supplies the Swagger example for the certificate inventory returned by
///     <see cref="Controllers.AcmeSettingsController.Certificates" />, with one domain in each
///     state the endpoint distinguishes.
/// </summary>
public class AcmeCertificateInventoryListExample
    : IExamplesProvider<IReadOnlyList<AcmeCertificateInventoryEntry>>
{
    /// <summary>
    ///     This method returns a domain with a current certificate, a domain that has never been
    ///     issued one (due, but no failure), and a domain whose last attempt failed, with its
    ///     failure time and message.
    /// </summary>
    public IReadOnlyList<AcmeCertificateInventoryEntry> GetExamples() =>
        [
            new AcmeCertificateInventoryEntry(
                Domain: "dns.example.com",
                HasCertificate: true,
                // Fixed dates, not the clock, so the committed OpenAPI snapshot is the same on every regeneration.
                IssuedAtUtc: DateTimeOffset.Parse("2026-06-05T10:00:00+00:00"),
                ExpiresAtUtc: DateTimeOffset.Parse("2026-09-03T10:00:00+00:00"),
                IsDueForRenewal: false,
                LastFailureAtUtc: null,
                LastFailureMessage: null
            ),
            new AcmeCertificateInventoryEntry(
                Domain: "new.example.com",
                HasCertificate: false,
                IssuedAtUtc: null,
                ExpiresAtUtc: null,
                IsDueForRenewal: true,
                LastFailureAtUtc: null,
                LastFailureMessage: null
            ),
            new AcmeCertificateInventoryEntry(
                Domain: "stuck.example.com",
                HasCertificate: false,
                IssuedAtUtc: null,
                ExpiresAtUtc: null,
                IsDueForRenewal: true,
                LastFailureAtUtc: DateTimeOffset.Parse("2026-06-15T08:00:00+00:00"),
                LastFailureMessage: "The ACME DNS-01 challenge was reported invalid by the CA: DNS problem: NXDOMAIN"
            ),
        ];
}
