/**
 * This function splits a textarea's own one-entry-per-line text into a trimmed, non-empty-only
 * array -- the same helper `TransportSettingsComponent`'s own `splitLines` already establishes,
 * applied here to the DHCPv4 relay's own upstream server address list.
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
 * This function parses one Router Advertisement interface override's own tri-state
 * ("inherit"/"On"/"Off") select control into the nullable boolean
 * `DhcpRaInterfaceOverrideDetail` itself carries -- an empty string (the "inherit the global
 * default" option) parses to `null`, never to `false`.
 */
export function parseOptionalTriStateBoolean(value: string): boolean | null {
  if (value === 'true') return true;
  if (value === 'false') return false;
  return null;
}

/** This function is {@link parseOptionalTriStateBoolean}'s own inverse, used to populate that same select from a loaded nullable boolean. */
export function triStateBooleanToSelectValue(value: boolean | null | undefined): string {
  if (value === true) return 'true';
  if (value === false) return 'false';
  return '';
}

/**
 * This function parses one Router Advertisement interface override's own "blank means inherit
 * the global default" numeric text field -- blank (after trimming) parses to `null`, never `0`
 * or `NaN`.
 */
export function parseOptionalNumber(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) return null;

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * This function parses one Router Advertisement interface override's own "blank means inherit
 * the global default" duration/free-text field -- blank (after trimming) parses to `null`.
 */
export function parseOptionalText(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

/**
 * This function parses one Router Advertisement interface override's own "blank textarea means
 * inherit the global default list" field -- a blank textarea parses to `null` (inherit), while
 * any non-blank textarea parses to the same one-entry-per-line array {@link splitLines} already
 * produces, even if every line turns out blank (a deliberate, documented simplification: this
 * form has no separate way to express "wholesale-replace with an explicitly empty list," only
 * "inherit" vs. "replace with these entries" -- see `DhcpRaInterfaceOverrideDetail`'s own doc
 * comment for the REST API's own fuller null-vs-empty-list distinction, still reachable directly).
 */
export function parseOptionalLines(value: string): string[] | null {
  return value.trim().length === 0 ? null : splitLines(value);
}
