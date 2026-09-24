using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AcmeSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="Controllers.AcmeSettingsController.Certificates" />'s own response -- three
///     domains, each in a genuinely different state, so a reader of this example can see every
///     field this endpoint actually distinguishes at a glance.
/// </summary>
public class AcmeCertificateInventoryListExample
    : IExamplesProvider<IReadOnlyList<AcmeCertificateInventoryEntry>>
{
    /// <summary>
    ///     This method returns one domain with a healthy, current certificate; one domain that
    ///     has never had a certificate issued at all (a real, distinct state, not silently
    ///     indistinguishable from "not due yet"); and one domain whose most recent renewal
    ///     attempt failed, with its own failure message and timestamp still visible.
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
