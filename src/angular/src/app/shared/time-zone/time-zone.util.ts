/**
 * This constant is the time zone list used when `Intl.supportedValuesOf` is unavailable. It backs
 * {@link timeZoneOptions} for the client and client group schedule forms.
 */
const FALLBACK_TIME_ZONES = [
  'America/New_York',
  'America/Chicago',
  'America/Denver',
  'America/Los_Angeles',
  'America/Sao_Paulo',
  'Europe/London',
  'Europe/Paris',
  'Europe/Berlin',
  'Europe/Moscow',
  'Africa/Cairo',
  'Africa/Johannesburg',
  'Asia/Dubai',
  'Asia/Kolkata',
  'Asia/Shanghai',
  'Asia/Tokyo',
  'Asia/Singapore',
  'Australia/Sydney',
  'Pacific/Auckland',
];

/** This function returns the browser's IANA time zone list, or the fallback list. */
function browserTimeZones(): string[] {
  const intlWithSupportedValuesOf = Intl as unknown as {
    supportedValuesOf?: (key: string) => string[];
  };

  try {
    const zones = intlWithSupportedValuesOf.supportedValuesOf?.('timeZone');
    if (zones && zones.length > 0) return zones;
  } catch {
    // Use the fallback list.
  }

  return FALLBACK_TIME_ZONES;
}

/**
 * This function returns the time zone ids a schedule form offers: `UTC` first, then the browser's
 * IANA zones, then `current` when it is not among them, with no duplicates.
 *
 * `UTC` is added because the browsers' lists (Chromium's, for one) leave it out, yet it is the
 * form's default and the zone most schedules use. `current` is the id a stored schedule holds;
 * an older alias such as `Asia/Calcutta` is missing from the browser's list, and without an
 * option for it the select would show nothing and a save would change the zone.
 */
export function timeZoneOptions(current: string | null | undefined = null): string[] {
  const options = ['UTC'];
  for (const zone of browserTimeZones()) {
    if (!options.includes(zone)) options.push(zone);
  }
  if (current && !options.includes(current)) options.push(current);
  return options;
}
