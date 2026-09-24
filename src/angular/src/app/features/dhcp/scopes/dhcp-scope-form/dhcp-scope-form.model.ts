/**
 * This function splits a textarea's own one-entry-per-line text into a trimmed, non-empty-only
 * array -- the same helper `TransportSettingsComponent`'s own `splitLines` already establishes,
 * applied here to a scope's own gateway/DNS server address lists.
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
