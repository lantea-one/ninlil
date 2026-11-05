/**
 * This function turns the owner name typed in the record form into the full name the records API
 * stores. `POST` and `PUT` on `/api/zones/{zoneId}/records` refuse a name that is neither the
 * zone's name nor below it, so a short name such as `www` gets the zone appended
 * (`www` in `example.test` becomes `www.example.test`).
 *
 * `@` (the zone apex) and any name that is the zone or below it are returned as typed, which
 * keeps the case the user chose. A name with a trailing dot is absolute, so it is also returned
 * as typed when it is outside the zone and the API answers with its own "did you mean" message.
 *
 * @param typed The text in the owner name field.
 * @param zoneName The owning zone's name, or `null` while it is not loaded.
 */
export function resolveRecordName(typed: string, zoneName: string | null): string {
  const name = typed.trim();

  if (name === '' || name === '@' || !zoneName) {
    return name;
  }

  // A trailing dot marks a fully qualified name. Compare without it so `example.test.` matches
  // the zone `example.test`.
  const zone = stripOneTrailingDot(zoneName).toLowerCase();
  const lowered = stripOneTrailingDot(name).toLowerCase();

  // Label-wise: the name must equal the zone or end with `.` + zone. A plain suffix test would
  // wrongly accept `wwwexample.test`, which is not below `example.test`.
  if (lowered === zone || lowered.endsWith(`.${zone}`)) {
    return name;
  }

  if (name.endsWith('.')) {
    return name;
  }

  return `${name}.${stripOneTrailingDot(zoneName)}`;
}

/** This function removes a single trailing dot from a DNS name, if there is one. */
function stripOneTrailingDot(value: string): string {
  return value.endsWith('.') ? value.slice(0, -1) : value;
}
