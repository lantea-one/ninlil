using Ninlil.Web.Models;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Examples.AcmeSettings;

/// <summary>
///     This class supplies the hand-authored example Swagger UI/ReDoc render for
///     <see cref="AcmeSettingsDetail" />, referenced by
///     <see cref="Controllers.AcmeSettingsController.Get" />.
/// </summary>
public class AcmeSettingsDetailExample : IExamplesProvider<AcmeSettingsDetail>
{
    /// <summary>
    ///     This method returns a realistic, deliberately-opted-in configuration: ACME enabled
    ///     against Let's Encrypt's real production directory, one contact address, two enrolled
    ///     domains, this class's own compiled-in default renewal margin/poll interval, and global
    ///     CAA auto-management enabled with a real issuer domain configured.
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
