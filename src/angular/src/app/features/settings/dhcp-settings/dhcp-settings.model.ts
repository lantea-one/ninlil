import { AbstractControl, ValidationErrors } from '@angular/forms';

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

/** The most Recursive DNS Server addresses one Router Advertisement can carry in 1240 octets: (1240 - 16 header - 8 SLLA - 8 MTU - 8 option header) / 16. */
export const MAX_RA_RECURSIVE_DNS_SERVERS = 75;

/** The most octets the encoded DNS Search List domains may take so the advertisement fits 1240 octets. */
export const MAX_RA_DNS_SEARCH_LIST_OCTETS = 1208;

/**
 * This validator rejects a Recursive DNS Server textarea with more than
 * {@link MAX_RA_RECURSIVE_DNS_SERVERS} entries (the server rejects it too). It reports
 * `maxEntries`. Blank is valid, since an override inherits the global list.
 */
export function maxRecursiveDnsServersValidator(control: AbstractControl): ValidationErrors | null {
  const count = splitLines(String(control.value ?? '')).length;
  return count > MAX_RA_RECURSIVE_DNS_SERVERS
    ? { maxEntries: { max: MAX_RA_RECURSIVE_DNS_SERVERS, actual: count } }
    : null;
}

/**
 * This validator accepts a .NET `TimeSpan` text (`[d.]hh:mm:ss[.fffffff]`) that is greater than
 * zero, and reports `positiveDuration` otherwise. An empty value is left to
 * `Validators.required`; text the server alone can judge is not rejected here.
 */
export function positiveDurationValidator(control: AbstractControl): ValidationErrors | null {
  const text = String(control.value ?? '').trim();
  if (text.length === 0) return null;

  const match = /^(?:(\d+)\.)?(\d+):(\d+):(\d+)(?:\.(\d+))?$/.exec(text);
  if (!match) return { positiveDuration: true };

  const total = [match[1], match[2], match[3], match[4], match[5]].reduce(
    (sum, part) => sum + Number(part ?? 0),
    0,
  );
  return total > 0 ? null : { positiveDuration: true };
}
