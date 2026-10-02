/** This function splits one-entry-per-line textarea text (addresses or CIDRs) into trimmed, non-empty entries. */
export function splitLines(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/** This function is the inverse of `splitLines`, for filling a textarea from a loaded array. */
export function joinLines(values: readonly string[] | null | undefined): string {
  return (values ?? []).join('\n');
}

/**
 * This function builds the `sharedSecret` part of the `PATCH /api/reverse-proxy-settings` body,
 * leaving it out when the input is blank. `Get` never returns the secret (only
 * `sharedSecretConfigured`), and `Patch` leaves an absent field unchanged, so a blank input means
 * "keep the stored secret". It is the same rule as `buildBrokerPatchFields` for broker passwords.
 */
export function buildSharedSecretPatchField(sharedSecret: string): { sharedSecret?: string } {
  return sharedSecret.trim().length > 0 ? { sharedSecret } : {};
}
