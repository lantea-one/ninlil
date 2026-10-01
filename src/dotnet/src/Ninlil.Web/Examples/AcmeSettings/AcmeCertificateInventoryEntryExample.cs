using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AcmeSettings;

/// <summary>
///     This class supplies the Swagger example for one
///     <see cref="AcmeCertificateInventoryEntry" />, as returned by
///     <see cref="Controllers.AcmeSettingsController.IssueOrRenewNow" />.
/// </summary>
public class AcmeCertificateInventoryEntryExample : IExamplesProvider<AcmeCertificateInventoryEntry>
{
    /// <summary>
    ///     This method returns a domain whose "issue now" request has just succeeded: a new
    ///     certificate, not due for renewal, with no failure recorded.
    /// </summary>
    public AcmeCertificateInventoryEntry GetExamples() =>
        new(
            Domain: "dns.example.com",
            HasCertificate: true,
            // Fixed dates, not the clock, so the committed OpenAPI snapshot is the same on every regeneration.
            IssuedAtUtc: DateTimeOffset.Parse("2026-06-15T10:00:00+00:00"),
            ExpiresAtUtc: DateTimeOffset.Parse("2026-09-13T10:00:00+00:00"),
            IsDueForRenewal: false,
            LastFailureAtUtc: null,
            LastFailureMessage: null
        );
}
