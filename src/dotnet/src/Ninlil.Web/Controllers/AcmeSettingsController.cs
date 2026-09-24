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
///     This controller reads and writes this host's own <see cref="AcmeOptions" /> (RFC 8555
///     ACME issuance/renewal) and <see cref="CaaAutoManagementOptions" /> (the global default for
///     CAA auto-management -- the per-zone override already exists directly on
///     <c>ZonesController</c>, not duplicated here), plus a real, per-domain certificate
///     inventory and a manual "issue/renew now" trigger -- closing the "LE Certs" configuration
///     and visibility gap entirely: before this controller existed, both were unconfigurable and
///     unobservable short of hand-editing the configuration file and reading log output. Every
///     action requires the <c>acmesettings:read</c>, <c>acmesettings:write</c>, or
///     <c>acmesettings:manage</c> permission -- the same three-tier split
///     <c>ServicesController</c> already establishes for an endpoint whose write actions have a
///     real, external, operationally significant effect (there, taking a live DNS transport
///     offline; here, spending a request against Let's Encrypt's own real, rate-limited issuance
///     quota): <see cref="Get" />/<see cref="Certificates" /> only ever reveal already-known,
///     non-secret configuration and state, so both are gated by the lighter
///     <c>acmesettings:read</c>; <see cref="Patch" /> is gated by the ordinary
///     <c>acmesettings:write</c>; <see cref="IssueOrRenewNow" /> gets its own, deliberately
///     separate <c>acmesettings:manage</c>, so a role with broad ordinary settings-write access
///     does not also silently gain the ability to trigger a real external CA request on demand.
///     <para>
///         <b>Every field except <see cref="AcmeSettingsDetail.AccountStateFilePath" /> takes
///         effect immediately, with no restart.</b> <see cref="AcmeOptions" /> is read via
///         <see cref="IOptionsMonitor{TOptions}" /> everywhere it actually matters for a live
///         issuance/renewal attempt (<c>AcmeCertificateRenewalService</c>'s own poll loop and its
///         shared <c>RenewOneDomainAsync</c>, and <c>AcmeCertificateIssuer</c> itself) rather than
///         the once-cached <see cref="IOptions{TOptions}" /> every one of those classes used
///         previously -- the same hot-reload gap already closed elsewhere in this project for
///         other recursive-resolver and transport-settings singletons.
///         <see cref="CaaAutoManagementOptions" /> is likewise read live by
///         <c>CaaRecordReconciliationService</c>. <b>Exactly one field is the deliberate
///         exception</b>: <see cref="AcmeSettingsDetail.AccountStateFilePath" /> is only ever read
///         by <c>FileAcmeAccountStore</c>, which caches its own in-memory state the first time it
///         is asked to load it (typically at or shortly after startup) -- relocating the path
///         afterward would only move *future* writes to the new location without ever actually
///         reading back whatever already exists there, so this one field still needs a restart to
///         take real effect, exactly like <c>RecursiveSettingsController</c>'s own two
///         override-list exceptions. <see cref="Patch" /> reports which note applies.
///     </para>
/// </summary>
/// <param name="acmeOptions">This host's own currently-effective ACME settings, live.</param>
/// <param name="caaOptions">This host's own currently-effective global CAA auto-management defaults, live.</param>
/// <param name="accountStore">The store this controller reads every domain's own certificate state and most recent failure from.</param>
/// <param name="renewalService">The service <see cref="IssueOrRenewNow" /> drives a single domain's own on-demand issuance/renewal through.</param>
/// <param name="runtimeConfigurationOptions">The JSON configuration file a write is merged into.</param>
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
    ///     This action reads this host's own currently-effective (live) ACME and global CAA
    ///     auto-management settings.
    /// </summary>
    [HttpGet]
    [RequirePermission("acmesettings:read")]
    [SwaggerResponseExample(StatusCodes.Status200OK, typeof(AcmeSettingsDetailExample))]
    public ActionResult<AcmeSettingsDetail> Get() =>
        Ok(ToDetail(acmeOptions.CurrentValue, caaOptions.CurrentValue));

    /// <summary>
    ///     This action merges a partial update into <c>DnsServer:Acme</c>/
    ///     <c>DnsServer:Acme:CaaAutoManagement</c> via <see cref="PresetConfigurationFileApplier" />'s
    ///     own generic settings-list overload, RFC 7396 merge-patch style: a field absent from
    ///     <paramref name="patch" /> is left exactly as it was in the file. See this class's own
    ///     doc comment for which field needs a restart.
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
    ///     This action returns a real, per-domain certificate inventory: every domain
    ///     <see cref="AcmeOptions.Domains" /> currently names, alongside whatever
    ///     <see cref="IAcmeAccountStore" /> actually knows about it -- whether it has ever had a
    ///     certificate successfully issued (a real, distinct state from "issued, but not due for
    ///     renewal yet"), its issued/expiry dates, whether it is currently due for renewal, and
    ///     its most recent renewal failure, if any. Returned in <see cref="AcmeOptions.Domains" />'s
    ///     own configured order.
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
    ///     This action issues or renews a single, configured domain's own certificate right now,
    ///     outside <c>AcmeCertificateRenewalService</c>'s own poll loop timing -- driven directly
    ///     through <see cref="AcmeCertificateRenewalService.RenewOneDomainAsync" />, the exact
    ///     same scope-per-attempt construction that service's own background loop already uses
    ///     (see that method's own doc comment for why it is the one, shared implementation rather
    ///     than a second copy here). The real attempt's own outcome (success or failure) is
    ///     always reflected in the returned inventory entry -- a genuine issuance/renewal failure
    ///     (a CA outage, a challenge that failed to validate) is not reported as an HTTP error
    ///     here, the same "one item's own failure must not be treated as the request itself
    ///     failing" reasoning the background loop already applies, since the failure itself is
    ///     already logged and durably recorded by the time this action returns; only a domain
    ///     this server does not currently have configured at all is a genuine <c>404</c>.
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
            // RenewOneDomainAsync has already logged this failure and durably recorded it via
            // IAcmeAccountStore.SaveDomainFailure -- the returned inventory entry below already
            // reflects it, so there is nothing further to do with the exception itself here.
        }

        return Ok(ToInventoryEntry(domain, acmeOptions.CurrentValue.RenewBeforeExpiration));
    }

    /// <summary>
    ///     This method reads a merge-patch field expected to hold a <see cref="TimeSpan" />-parseable
    ///     string, distinguishing "absent" from "present but unparseable" for the caller.
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
    ///     This method parses a merge-patch field expected to hold a JSON array of strings,
    ///     validating each entry with <paramref name="validateEntry" /> along the way.
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
    ///     This method validates a single <c>domains</c> entry the same way this server would
    ///     actually need it to parse to ever act on it -- a syntactically valid DNS name, via
    ///     <see cref="DnsName.TryParse(string,out DnsName)" />, the exact parser every real
    ///     domain-name-shaped field elsewhere in this REST API already validates against.
    /// </summary>
    private static void ValidateDomain(string domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || !DnsName.TryParse(domain, out _))
            throw new ValidationException($"'{domain}' is not a valid domain name.");
    }

    /// <summary>
    ///     This method applies a lightweight, non-exhaustive sanity check to a single
    ///     <c>contactEmails</c> entry -- the real, authoritative validation of an ACME contact
    ///     address happens CA-side, the moment <c>AcmeCertificateIssuer</c> actually registers or
    ///     reuses an account with it, so this check exists only to catch an obviously-malformed
    ///     value early, not to fully replicate RFC 5322.
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
    ///     This method builds <see cref="AcmeSettingsDetail" /> from a live
    ///     <see cref="AcmeOptions" />/<see cref="CaaAutoManagementOptions" /> snapshot.
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
    ///     This method builds a single domain's own <see cref="AcmeCertificateInventoryEntry" />
    ///     from <see cref="IAcmeAccountStore" />'s own currently-known state.
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
