/**
 * This pattern matches a .NET time span as the settings API writes it: `[d.]hh:mm:ss[.fffffff]`.
 * `changedFields` compares two such texts by value, because the page may show `0:05:00` where the
 * server wrote `00:05:00`.
 */
const TIME_SPAN_PATTERN = /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.(\d{1,7}))?$/;

/** This function returns the length of a time span text in seconds, or null if it is not one. */
function timeSpanSeconds(text: string): number | null {
  const match = TIME_SPAN_PATTERN.exec(text);
  if (match === null) {
    return null;
  }

  const days = match[1] === undefined ? 0 : Number(match[1]);
  const fraction = match[5] === undefined ? 0 : Number(`0.${match[5]}`);
  return (
    days * 86_400 + Number(match[2]) * 3_600 + Number(match[3]) * 60 + Number(match[4]) + fraction
  );
}

/** This function reports whether a value is a plain object (not null, not an array). */
function isPlainObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

/**
 * This function compares two settings values by content: arrays element by element, plain objects
 * key by key, two time span texts by their length and everything else with `===`.
 */
function sameValue(left: unknown, right: unknown): boolean {
  if (Array.isArray(left) && Array.isArray(right)) {
    return (
      left.length === right.length && left.every((item, index) => sameValue(item, right[index]))
    );
  }

  if (isPlainObject(left) && isPlainObject(right)) {
    const leftKeys = Object.keys(left);
    return (
      leftKeys.length === Object.keys(right).length &&
      leftKeys.every((key) => key in right && sameValue(left[key], right[key]))
    );
  }

  if (typeof left === 'string' && typeof right === 'string') {
    const leftSeconds = timeSpanSeconds(left);
    const rightSeconds = timeSpanSeconds(right);
    if (leftSeconds !== null && rightSeconds !== null) {
      return leftSeconds === rightSeconds;
    }
  }

  return left === right;
}

/**
 * This function returns the fields of `current` whose value differs from `baseline`, the body of a
 * changed-only `PATCH` on a settings page. A page keeps the body it last loaded or saved as the
 * baseline and sends only this difference, so the server's "restart needed" note names only the
 * fields the user touched.
 *
 * <p>A field that is an array or an object is compared by content and, when it differs, sent
 * whole: a JSON merge patch replaces an array as one value (RFC 7396 §2) and a page cannot send a
 * single list entry. Two time span texts are equal when they name the same length of time.
 */
export function changedFields<T extends object>(baseline: T, current: T): Partial<T> {
  const changed: Partial<T> = {};

  for (const key of Object.keys(current) as (keyof T)[]) {
    if (!(key in baseline) || !sameValue(baseline[key], current[key])) {
      changed[key] = current[key];
    }
  }

  return changed;
}

/** This function reports whether a difference from `changedFields` holds at least one field. */
export function hasChangedFields(changes: object): boolean {
  return Object.keys(changes).length > 0;
}
