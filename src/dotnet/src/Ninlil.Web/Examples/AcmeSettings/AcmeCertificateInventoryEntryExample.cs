using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AcmeSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for a single
///     <see cref="AcmeCertificateInventoryEntry" />, referenced by
///     <see cref="Controllers.AcmeSettingsController.IssueOrRenewNow" />'s own response.
/// </summary>
public class AcmeCertificateInventoryEntryExample : IExamplesProvider<AcmeCertificateInventoryEntry>
{
    /// <summary>
    ///     This method returns a domain whose manual "issue now" request just succeeded: a fresh
    ///     certificate, not due for renewal, with no recorded failure.
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
