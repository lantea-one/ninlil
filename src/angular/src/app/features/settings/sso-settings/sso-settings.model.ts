/**
 * This function splits this form's own single-line `scopes` field into a trimmed, non-empty,
 * duplicate-free array -- an operator types scopes the same way they'd appear in a real OAuth 2.0
 * `scope` parameter (RFC 6749 §3.3), whitespace-separated, with a comma also accepted since that's
 * the more familiar separator for a short list typed by hand. Mirrors `splitLines`
 * (`ReverseProxySettingsComponent`'s own helper for a one-value-per-line textarea) in spirit, but
 * scopes are conventionally a single space-separated line, not one-per-line, so this is its own
 * small helper rather than a reuse of that one.
 */
export function splitScopes(text: string): string[] {
  const seen = new Set<string>();
  const scopes: string[] = [];

  for (const candidate of text.split(/[\s,]+/)) {
    const scope = candidate.trim();
    if (scope.length === 0 || seen.has(scope)) continue;
    seen.add(scope);
    scopes.push(scope);
  }

  return scopes;
}

/** This function is {@link splitScopes}'s own inverse, used to populate the form field from a loaded array. */
export function joinScopes(values: readonly string[] | null | undefined): string {
  return (values ?? []).join(' ');
}

/**
 * This function builds the `clientSecret` field for `OidcSsoSettingsController.Patch`'s own
 * merge-patch body, omitting it entirely whenever the caller left the input blank.
 *
 * `OidcSsoSettingsController.Get` never returns the configured client secret at all (only a
 * `clientSecretConfigured` boolean indicator), and `Patch` only ever writes a field that is
 * *present* in the merge-patch JSON body at all -- a field simply absent from the patch is left
 * completely untouched in the configuration file, including `clientSecret`. Since this form can
 * never pre-fill the current secret (it never received it), leaving the secret input blank on
 * submit must mean "leave unchanged," not "clear it" -- the same reasoning
 * `buildSharedSecretPatchField` (`ReverseProxySettingsComponent`'s own model) and
 * `buildSmtpSettingsPatchBody` (`SmtpSettingsComponent`'s own model) already establish for their
 * own differently-named secret fields.
 */
export function buildClientSecretPatchField(clientSecret: string): { clientSecret?: string } {
  return clientSecret.trim().length > 0 ? { clientSecret } : {};
}
