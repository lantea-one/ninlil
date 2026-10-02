/**
 * This function splits the one-line `scopes` field into trimmed, non-empty, unique entries. Scopes
 * are space-separated as in an OAuth 2.0 `scope` parameter (RFC 6749 §3.3); commas are accepted
 * too, since people often type them.
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

/** This function is the inverse of {@link splitScopes}, for filling the field from a loaded array. */
export function joinScopes(values: readonly string[] | null | undefined): string {
  return (values ?? []).join(' ');
}

/**
 * This function builds the `clientSecret` part of the `PATCH /api/sso-settings` body, leaving it
 * out when the input is blank. `Get` never returns the secret (only `clientSecretConfigured`) and
 * `Patch` leaves an absent field unchanged, so a blank input means "keep the stored secret", as
 * for the reverse proxy and SMTP secrets.
 */
export function buildClientSecretPatchField(clientSecret: string): { clientSecret?: string } {
  return clientSecret.trim().length > 0 ? { clientSecret } : {};
}
