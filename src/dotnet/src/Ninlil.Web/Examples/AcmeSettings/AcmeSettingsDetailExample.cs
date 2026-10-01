using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AcmeSettings;

/// <summary>
///     This class supplies the Swagger example for <see cref="AcmeSettingsDetail" />, as returned
///     by <see cref="Controllers.AcmeSettingsController.Get" />.
/// </summary>
public class AcmeSettingsDetailExample : IExamplesProvider<AcmeSettingsDetail>
{
    /// <summary>
    ///     This method returns an enabled configuration against the Let's Encrypt production
    ///     directory with one contact address and two domains. The renewal margin (30 days) and
    ///     poll interval (12 hours) are the defaults, and server-wide CAA auto-management is on.
    /// </summary>
    public AcmeSettingsDetail GetExamples() =>
        new(
            Enable: true,
            DirectoryUri: new Uri("https://acme-v02.api.letsencrypt.org/directory"),
            ContactEmails: ["admin@example.com"],
            Domains: ["dns.example.com", "mail.example.com"],
            RenewBeforeExpiration: TimeSpan.FromDays(30),
            PollInterval: TimeSpan.FromHours(12),
            AccountStateFilePath: "/var/lib/ninlil/data/acme-account.json",
            CertificateDirectoryPath: "/var/lib/ninlil/data/acme-certificates",
            CsrOrganization: "Example Homelab",
            CaaAutoManagement: new CaaAutoManagementSettingsDetail(
                DefaultEnabled: true,
                DefaultIssuerDomain: "letsencrypt.org"
            )
        );
}
