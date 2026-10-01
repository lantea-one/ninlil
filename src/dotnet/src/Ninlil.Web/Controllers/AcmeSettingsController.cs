using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Ninlil.Dns.Common.Exceptions;
using Ninlil.Dns.Common.Wire;
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
    ///     needed. Returns 400 for an invalid directory URL, domain, contact address or duration, a
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

        if (patch.TryGetMergePatchField("enable", out JsonElement enable))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.Enable),
                    enable.GetBoolean()
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchField("directoryUri", out JsonElement directoryUri))
        {
            string directoryUriText = directoryUri.GetString();

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

        if (patch.TryGetMergePatchField("contactEmails", out JsonElement contactEmails))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.ContactEmails),
                    ParseStringArray(contactEmails, "contactEmails", ValidateContactEmail)
                )
            );
            touchedHotReloadingField = true;
        }

        if (patch.TryGetMergePatchField("domains", out JsonElement domains))
        {
            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.Domains),
                    ParseStringArray(domains, "domains", ValidateDomain)
                )
            );
            touchedHotReloadingField = true;
        }

        if (
            TryGetDuration(
                patch,
                "renewBeforeExpiration",
                out TimeSpan renewBeforeExpiration,
                out bool renewBeforeExpirationPresent
            )
        )
        {
            if (renewBeforeExpiration <= TimeSpan.Zero)
                throw new ValidationException("'renewBeforeExpiration' must be greater than zero.");

            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.RenewBeforeExpiration),
                    renewBeforeExpiration.ToString()
                )
            );
            touchedHotReloadingField = true;
        }
        else if (renewBeforeExpirationPresent)
            throw new ValidationException(
                "'renewBeforeExpiration' must be a valid TimeSpan (for example \"30.00:00:00\")."
            );

        if (
            TryGetDuration(
                patch,
                "pollInterval",
                out TimeSpan pollInterval,
                out bool pollIntervalPresent
            )
        )
        {
            if (pollInterval <= TimeSpan.Zero)
                throw new ValidationException("'pollInterval' must be greater than zero.");

            settings.Add(
                new PresetConfigurationSetting(
                    AcmeOptions.SectionName,
                    nameof(AcmeOptions.PollInterval),
                    pollInterval.ToString()
                )
            );
            touchedHotReloadingField = true;
        }
        else if (pollIntervalPresent)
            throw new ValidationException(
                "'pollInterval' must be a valid TimeSpan (for example \"12:00:00\")."
            );

        if (
            patch.TryGetMergePatchField(
                "accountStateFilePath",
                out JsonElement accountStateFilePath
            )
        )
        {
            string accountStateFilePathText = accountStateFilePath.GetString();

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
            patch.TryGetMergePatchField(
                "certificateDirectoryPath",
                out JsonElement certificateDirectoryPath
            )
        )
        {
            string certificateDirectoryPathText = certificateDirectoryPath.GetString();

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

        if (patch.TryGetMergePatchField("csrOrganization", out JsonElement csrOrganization))
        {
            string csrOrganizationText = csrOrganization.GetString();

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
                caaAutoManagement.TryGetMergePatchField(
                    "defaultEnabled",
                    out JsonElement defaultEnabled
                )
            )
            {
                settings.Add(
                    new PresetConfigurationSetting(
                        CaaAutoManagementOptions.SectionName,
                        nameof(CaaAutoManagementOptions.DefaultEnabled),
                        defaultEnabled.GetBoolean()
                    )
                );
                touchedHotReloadingField = true;
            }

            if (
                caaAutoManagement.TryGetMergePatchField(
                    "defaultIssuerDomain",
                    out JsonElement defaultIssuerDomain
                )
            )
            {
                settings.Add(
                    new PresetConfigurationSetting(
                        CaaAutoManagementOptions.SectionName,
                        nameof(CaaAutoManagementOptions.DefaultIssuerDomain),
                        defaultIssuerDomain.GetString() ?? string.Empty
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
    ///     This method reads a merge-patch field that should hold a TimeSpan string, telling the
    ///     caller both whether the field was present and whether it parsed.
    /// </summary>
    private static bool TryGetDuration(
        JsonElement patch,
        string propertyName,
        out TimeSpan value,
        out bool present
    )
    {
        value = default;
        present = patch.TryGetMergePatchField(propertyName, out JsonElement element);

        return present && TimeSpan.TryParse(element.GetString(), out value);
    }

    /// <summary>
    ///     This method parses a merge-patch field that must hold a JSON array of strings, checking
    ///     each entry with <paramref name="validateEntry" />.
    /// </summary>
    private static List<string> ParseStringArray(
        JsonElement element,
        string fieldName,
        Action<string> validateEntry
    )
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ValidationException($"'{fieldName}' must be a JSON array of strings.");

        List<string> parsed = [];

        foreach (JsonElement item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                throw new ValidationException($"Each '{fieldName}' entry must be a string.");

            string value = item.GetString();
            validateEntry(value);
            parsed.Add(value);
        }

        return parsed;
    }

    /// <summary>
    ///     This method checks that a <c>domains</c> entry is a valid DNS name, using
    ///     <see cref="DnsName.TryParse(string,out DnsName)" /> like the API's other domain-name
    ///     fields.
    /// </summary>
    private static void ValidateDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || !DnsName.TryParse(domain, out _))
            throw new ValidationException($"'{domain}' is not a valid domain name.");
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
