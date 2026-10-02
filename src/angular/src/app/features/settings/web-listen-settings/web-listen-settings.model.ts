/** This function splits one-entry-per-line textarea text, such as a certificate's subject alternative names, into trimmed, non-empty entries. */
export function splitLines(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/** This function is the inverse of {@link splitLines}, for filling a textarea from a loaded array. */
export function joinLines(values: readonly string[] | null | undefined): string {
  return (values ?? []).join('\n');
}

/**
 * This function builds the certificate's `pfxPassword` part of the `PATCH` body, leaving it out
 * when the input is blank. `WebListenSettingsController.Get` never returns the password (only
 * `pfxPasswordConfigured`), so a blank input means "keep the stored password".
 */
export function buildPfxPasswordPatchField(pfxPassword: string): { pfxPassword?: string } {
  return pfxPassword.trim().length > 0 ? { pfxPassword } : {};
}

/**
 * This function reads the listen-address field into the array the API takes, as the transport
 * page does: addresses separated by new lines, commas, semicolons or spaces, and an empty field
 * (every address) as an empty array.
 */
export function splitListenAddresses(text: string): string[] {
  return text
    .split(/[\s,;]+/)
    .map((address) => address.trim())
    .filter((address) => address.length > 0);
}
