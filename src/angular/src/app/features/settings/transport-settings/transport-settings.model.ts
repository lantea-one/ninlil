/**
 * This function splits a textarea's own one-entry-per-line text into a trimmed, non-empty-only
 * array -- the same helper `ReverseProxySettingsComponent`'s own `splitLines` already establishes,
 * applied here to a certificate's own subject alternative names list.
 */
export function splitLines(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/** This function is {@link splitLines}'s own inverse, used to populate a textarea from a loaded array. */
export function joinLines(values: readonly string[] | null | undefined): string {
  return (values ?? []).join('\n');
}

/**
 * This function builds a certificate's own `pfxPassword` field for a transport settings `PATCH`
 * body, omitting it entirely whenever the caller left the input blank -- the same reasoning
 * `ReverseProxySettingsComponent`'s own `buildSharedSecretPatchField` already establishes for its
 * own `sharedSecret` field: `TransportSettingsController.Get*` never returns a certificate's own
 * configured PFX password at all, only whether one is currently configured
 * (`pfxPasswordConfigured`), so this form can never pre-fill it, and leaving it blank on submit
 * must mean "leave unchanged," not "clear it."
 */
export function buildPfxPasswordPatchField(pfxPassword: string): { pfxPassword?: string } {
  return pfxPassword.trim().length > 0 ? { pfxPassword } : {};
}

/**
 * This function reads the listen-address field into the array the API takes. Addresses may be
 * separated by new lines, commas or spaces, since all three are natural to type or paste; an
 * empty field is an empty array, meaning every address. The server validates each address and
 * names any it rejects.
 */
export function splitListenAddresses(text: string): string[] {
  return text
    .split(/[\s,;]+/)
    .map((address) => address.trim())
    .filter((address) => address.length > 0);
}
