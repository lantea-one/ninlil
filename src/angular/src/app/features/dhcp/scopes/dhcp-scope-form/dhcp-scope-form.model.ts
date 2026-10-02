/**
 * This function splits one-entry-per-line textarea text into trimmed, non-empty entries, for a
 * scope's gateway and DNS server lists.
 */
export function splitLines(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/** This function is the inverse of {@link splitLines}, to fill a textarea from a loaded array. */
export function joinLines(values: readonly string[] | null | undefined): string {
  return (values ?? []).join('\n');
}
