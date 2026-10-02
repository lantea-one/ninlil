/** This function splits one-entry-per-line textarea text into trimmed, non-empty entries. */
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
 * This function turns a Router Advertisement interface override's three-way select ("inherit",
 * "On", "Off") into the nullable boolean `DhcpRaInterfaceOverrideDetail` carries. The empty
 * "inherit" value becomes `null`, not `false`.
 */
export function parseOptionalTriStateBoolean(value: string): boolean | null {
  if (value === 'true') return true;
  if (value === 'false') return false;
  return null;
}

/** This function is the inverse of {@link parseOptionalTriStateBoolean}, for filling the select from a loaded value. */
export function triStateBooleanToSelectValue(value: boolean | null | undefined): string {
  if (value === true) return 'true';
  if (value === false) return 'false';
  return '';
}

/**
 * This function parses an interface override's numeric text field, where blank means "inherit the
 * global value". Blank, or text that is not a number, becomes `null`.
 */
export function parseOptionalNumber(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) return null;

  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * This function parses an interface override's duration or text field, where blank means "inherit
 * the global value" and becomes `null`.
 */
export function parseOptionalText(value: string): string | null {
  const trimmed = value.trim();
  return trimmed.length === 0 ? null : trimmed;
}

/**
 * This function parses an interface override's list textarea: blank becomes `null` (inherit the
 * global list), anything else the {@link splitLines} entries. The form cannot express "replace with
 * an empty list"; the API can (see `DhcpRaInterfaceOverrideDetail`).
 */
export function parseOptionalLines(value: string): string[] | null {
  return value.trim().length === 0 ? null : splitLines(value);
}
