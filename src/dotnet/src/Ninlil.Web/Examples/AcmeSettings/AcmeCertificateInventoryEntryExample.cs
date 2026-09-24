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
            IssuedAtUtc: DateTimeOffset.UtcNow,
            ExpiresAtUtc: DateTimeOffset.UtcNow.AddDays(90),
            IsDueForRenewal: false,
            LastFailureAtUtc: null,
            LastFailureMessage: null
        );
}
