using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Identity.Authorization;
using Ninlil.Dns.Server.Acme;
using Ninlil.Dns.Server.Presets;
using Ninlil.Web.Examples.AcmeSettings;
using Ninlil.Web.Examples.Settings;
using Ninlil.Web.Extension;
using Ninlil.Web.Models;
using Ninlil.Web.Options;
using Swashbuckle.AspNetCore.Filters;

// Define our namespace.
namespace Ninlil.Web.Controllers;

/// <summary>
///     This controller reads and writes <see cref="AcmeOptions" /> (RFC 8555 ACME certificate
///     issuance and renewal) and <see cref="CaaAutoManagementOptions" /> (the server-wide default
///     for CAA record management; the per-zone override is on <c>ZonesController</c>), lists the
///     certificate state of each configured domain, and can issue or renew one on demand.
///     <para>
///         Permissions come in three tiers. <see cref="Get" /> and <see cref="Certificates" />
///         reveal only non-secret settings and state and need <c>acmesettings:read</c>;
///         <see cref="Patch" /> needs <c>acmesettings:write</c>. <see cref="IssueOrRenewNow" />
///         needs a separate <c>acmesettings:manage</c>, as <c>ServicesController</c> does for its
///         actions, because it sends a request to an external, rate-limited certificate
///         authority, and broad settings-write access should not include that.
///     </para>
///     <para>
///         <b>Every field except <see cref="AcmeSettingsDetail.AccountStateFilePath" /> takes
///         effect without a restart.</b> <c>AcmeCertificateRenewalService</c> and
///         <c>AcmeCertificateIssuer</c> read <see cref="AcmeOptions" /> through
///         <see cref="IOptionsMonitor{TOptions}" />, and <c>CaaRecordReconciliationService</c>
///         reads <see cref="CaaAutoManagementOptions" /> the same way; a new poll interval
///         applies after the current wait ends. <c>FileAcmeAccountStore</c> reads the account
///         state path through <see cref="IOptions{TOptions}" />, so changing it needs a restart.
///         <see cref="Patch" /> returns the matching note.
///     </para>
/// </summary>
/// <param name="acmeOptions">The current ACME settings.</param>
/// <param name="caaOptions">The current server-wide CAA management defaults.</param>
/// <param name="accountStore">Supplies each domain's certificate state and latest failure.</param>
/// <param name="renewalService">Issues or renews one domain's certificate for <see cref="IssueOrRenewNow" />.</param>
/// <param name="runtimeConfigurationOptions">Names the JSON configuration file a write is merged into.</param>
[ApiController]
[Route("api/acme-settings")]
public class AcmeSettingsController(
    IOptionsMonitor<AcmeOptions> acmeOptions,
    IOptionsMonitor<CaaAutoManagementOptions> caaOptions,
    IAcmeAccountStore accountStore,
    AcmeCertificateRenewalService renewalService,
    RuntimeConfigurationOptions runtimeConfigurationOptions
) : ControllerBase
{
    /// <summary>
    ///     This action returns the current ACME and server-wide CAA management settings. Requires
    ///     <c>acmesettings:read</c>.
    /// </summary>
    [HttpGet]
    [RequirePermission("acmesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AcmeSettingsDetailExample))]
    public ActionResult<AcmeSettingsDetail> Get() =>
        Ok(ToDetail(acmeOptions.CurrentValue, caaOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Acme</c> and
    ///     <c>DnsServer:Acme:CaaAutoManagement</c> as an RFC 7396 merge patch, through
    ///     <see cref="PresetConfigurationFileApplier" />. A field absent from
    ///     <paramref name="patch" /> keeps its value. The response note says whether a restart is
    ///     needed. A domain must be a host name of letters, digits and hyphens with at least two
    ///     labels, optionally with a leading <c>*.</c> for a wildcard certificate. Returns 400 for
    ///     an invalid directory URL, domain, contact address or duration, a
    ///     non-positive interval, or an empty path or organization. Requires
    ///     <c>acmesettings:write</c>.
    /// </summary>
    [HttpPatch]
    [RequirePermission("acmesettings:write")]
    [SwaggerRequestExample(typeof(JsonElement), typeof(AcmeSettingsPatchRequestExample))]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(RuntimeSettingsWriteResultExample))]
    public async Task<ActionResult<RuntimeSettingsWriteResult>> Patch([FromBody] JsonElement patch)
    {
        List<PresetConfigurationSetting> settings = [];
        bool touchedHotReloadingField = false;
        bool touchedRestartRequiringField = false;

        if (patch.TryGetMergePatchBoolean("enable", out bool enable))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.Enable),
                    enable
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchString(
                "directoryUri",
                out string directoryUriText,
                allowNull: false
            )
        )
        {
            if (
                string.IsNullOrWhiteSpace(directoryUriText)
                || !Uri.TryCreate(directoryUriText, UriKind.Absolute, out Uri parsedDirectoryUri)
                || (
                    parsedDirectoryUri.Scheme != Uri.UriSchemeHttp
                    && parsedDirectoryUri.Scheme != Uri.UriSchemeHttps
                )
            )
                throw new ValidationException(
                    "'directoryUri' must be an absolute http:// or https:// URL."
                );

            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.DirectoryUri),
                    directoryUriText
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchStringList(
                "contactEmails",
                out List<string> contactEmails,
                allowNull: false
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.ContactEmails),
                    ValidateEntries(contactEmails, ValidateContactEmail)
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchStringList("domains", out List<string> domains, allowNull: false))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.Domains),
                    ValidateEntries(domains, ValidateDomain)
                )
            );
            touchedHotReloadingField = true;
        }

        // The renewal margin is subtracted from a timestamp; the poll interval feeds a timer.
        if (
            patch.TryGetMergePatchDuration(
                "renewBeforeExpiration",
                out TimeSpan renewBeforeExpiration,
                TimeSpan.FromMilliseconds(1),
                TimeSpan.FromDays(36500)
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.RenewBeforeExpiration),
                    renewBeforeExpiration.ToString()
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchDuration(
                "pollInterval",
                out TimeSpan pollInterval,
                TimeSpan.FromMilliseconds(1),
                JsonElementMergePatchExtension.TimerMaximum
            )
        )
        {
            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.PollInterval),
                    pollInterval.ToString()
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchString(
                "accountStateFilePath",
                out string accountStateFilePathText,
                allowNull: false
            )
        )
        {
            if (string.IsNullOrWhiteSpace(accountStateFilePathText))
                throw new ValidationException("'accountStateFilePath' must not be empty.");

            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.AccountStateFilePath),
                    accountStateFilePathText
                )
            );
            touchedRestartRequiringField = true;
        }

        if (
            patch.TryGetMergePatchString(
                "certificateDirectoryPath",
                out string certificateDirectoryPathText,
                allowNull: false
            )
        )
        {
            if (string.IsNullOrWhiteSpace(certificateDirectoryPathText))
                throw new ValidationException("'certificateDirectoryPath' must not be empty.");

            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.CertificateDirectoryPath),
                    certificateDirectoryPathText
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            patch.TryGetMergePatchString(
                "csrOrganization",
                out string csrOrganizationText,
                allowNull: false
            )
        )
        {
            if (string.IsNullOrWhiteSpace(csrOrganizationText))
                throw new ValidationException("'csrOrganization' must not be empty.");

            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.CsrOrganization),
                    csrOrganizationText
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchField("caaAutoManagement", out JsonElement caaAutoManagement))
        {
            if (caaAutoManagement.ValueKind != JsonValueKind.Object)
                throw new ValidationException("'caaAutoManagement' must be a JSON object.");

            if (
                caaAutoManagement.TryGetMergePatchBoolean(
                    "defaultEnabled",
                    out bool defaultEnabled,
                    "caaAutoManagement.defaultEnabled"
                )
            )
            {
                settings.Add(
                    new PresetConfigurationSetting(
                        CaaAutoManagementOptions.SectionName,
                        nameof(CaaAutoManagementOptions.DefaultEnabled),
                        defaultEnabled
                    )
                );
                touchedHotReloadingField = true;
            }

            if (
                caaAutoManagement.TryGetMergePatchString(
                    "defaultIssuerDomain",
                    out string defaultIssuerDomain,
                    allowNull: true,
                    "caaAutoManagement.defaultIssuerDomain"
                )
            )
            {
                settings.Add(
                    new PresetConfigurationSetting(
                        CaaAutoManagementOptions.SectionName,
                        nameof(CaaAutoManagementOptions.DefaultIssuerDomain),
                        defaultIssuerDomain ?? string.Empty
                    )
                );
                touchedHotReloadingField = true;
            }
        }

        if (settings.Count == 0)
            return Ok(new RuntimeSettingsWriteResult("Nothing to change -- the patch was empty."));

        await PresetConfigurationFileApplier.ApplyAsync(
            settings,
            runtimeConfigurationOptions.ConfigurationFilePath
        );

        string note = (touchedHotReloadingField, touchedRestartRequiringField) switch
        {
            (true, true) => RuntimeSettingsWriteResult.MixedReloadNote,
            (false, true) => RuntimeSettingsWriteResult.RestartRequiredNote,
            _ => RuntimeSettingsWriteResult.HotReloadedNote,
        };

        return Ok(new RuntimeSettingsWriteResult(note));
    }

    /// <summary>
    ///     This action lists every domain in <see cref="AcmeOptions.Domains" />, in configured
    ///     order, with what <see cref="IAcmeAccountStore" /> knows about it: whether a certificate
    ///     has ever been issued, its issue and expiry dates, whether it is due for renewal, and its
    ///     latest renewal failure, if any. Requires <c>acmesettings:read</c>.
    /// </summary>
    [HttpGet("certificates")]
    [RequirePermission("acmesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AcmeCertificateInventoryListExample))]
    public ActionResult<IReadOnlyList<AcmeCertificateInventoryEntry>> Certificates() =>
        Ok(
            acmeOptions
                .CurrentValue.Domains.Select(domain =>
                    ToInventoryEntry(domain, acmeOptions.CurrentValue.RenewBeforeExpiration)
                )
                .ToList()
        );

    /// <summary>
    ///     This action issues or renews one configured domain's certificate now, outside the
    ///     renewal service's schedule, through
    ///     <see cref="AcmeCertificateRenewalService.RenewOneDomainAsync" />, the same method the
    ///     background loop uses. A failed attempt (a certificate authority outage, a challenge that
    ///     did not validate) is not an HTTP error: it is logged and recorded, and the returned
    ///     entry shows it. Returns 404 when the domain is not configured. Requires
    ///     <c>acmesettings:manage</c>.
    /// </summary>
    /// <param name="domain">The configured domain to issue or renew a certificate for.</param>
    /// <param name="stoppingToken">The token used to observe cancellation of the operation.</param>
    [HttpPost("certificates/{domain}/issue")]
    [RequirePermission("acmesettings:manage")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AcmeCertificateInventoryEntryExample))]
    public async Task<ActionResult<AcmeCertificateInventoryEntry>> IssueOrRenewNow(
        string domain,
        CancellationToken stoppingToken
    )
    {
        if (!acmeOptions.CurrentValue.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase))
            throw new NotFoundException("AcmeDomain", domain);

        try
        {
            await renewalService.RenewOneDomainAsync(domain, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // RenewOneDomainAsync has already logged the failure and saved it through
            // IAcmeAccountStore.SaveDomainFailure, so the entry returned below shows it.
        }

        return Ok(ToInventoryEntry(domain, acmeOptions.CurrentValue.RenewBeforeExpiration));
    }

    /// <summary>
    ///     This method checks each entry of a string array field with
    ///     <paramref name="validateEntry" /> and returns the same list.
    /// </summary>
    private static List<string> ValidateEntries(List<string> values, Action<string> validateEntry)
    {
        foreach (string value in values)
            validateEntry(value);

        return values;
    }

    /// <summary>
    ///     This method checks that a <c>domains</c> entry is a certificate host name with
    ///     <see cref="AcmeDomainName.TryParse" />: letters, digits and hyphens, at least two
    ///     labels, optionally a leading <c>*.</c> for a wildcard.
    /// </summary>
    private static void ValidateDomain(string domain)
    {
        if (!AcmeDomainName.TryParse(domain, out _, out _, out string error))
            throw new ValidationException(error);
    }

    /// <summary>
    ///     This method applies a light check to a <c>contactEmails</c> entry to catch obviously
    ///     malformed values early. The certificate authority does the full validation when
    ///     <c>AcmeCertificateIssuer</c> registers an account with the address.
    /// </summary>
    private static void ValidateContactEmail(string email)
    {
        if (
            string.IsNullOrWhiteSpace(email)
            || !email.Contains('@')
            || email.StartsWith('@')
            || email.EndsWith('@')
        )
            throw new ValidationException($"'{email}' is not a valid email address.");
    }

    /// <summary>
    ///     This method builds <see cref="AcmeSettingsDetail" /> from the current
    ///     <see cref="AcmeOptions" /> and <see cref="CaaAutoManagementOptions" />.
    /// </summary>
    private static AcmeSettingsDetail ToDetail(AcmeOptions acme, CaaAutoManagementOptions caa) =>
        new(
            acme.Enable,
            acme.DirectoryUri,
            acme.ContactEmails,
            acme.Domains,
            acme.RenewBeforeExpiration,
            acme.PollInterval,
            acme.AccountStateFilePath,
            acme.CertificateDirectoryPath,
            acme.CsrOrganization,
            new CaaAutoManagementSettingsDetail(caa.DefaultEnabled, caa.DefaultIssuerDomain)
        );

    /// <summary>
    ///     This method builds one domain's <see cref="AcmeCertificateInventoryEntry" /> from what
    ///     <see cref="IAcmeAccountStore" /> currently knows.
    /// </summary>
    private AcmeCertificateInventoryEntry ToInventoryEntry(
        string domain,
        TimeSpan renewBeforeExpiration
    )
    {
        AcmeDomainCertificateState state = accountStore.DomainStates.GetValueOrDefault(domain);
        accountStore.Failures.TryGetValue(domain, out AcmeDomainRenewalFailure failure);

        return new AcmeCertificateInventoryEntry(
            domain,
            HasCertificate: state is not null,
            state?.IssuedAtUtc,
            state?.ExpiresAtUtc,
            IsDueForRenewal: AcmeCertificateRenewalService.IsDomainDue(
                state,
                DateTimeOffset.UtcNow,
                renewBeforeExpiration
            ),
            failure?.OccurredAtUtc,
            failure?.ErrorMessage
        );
    }
}
