/**
 * This function renders how long ago an instant was, coarsely ("just now", "5 min ago", "3 h ago",
 * "2 d ago", "1 y ago"), for a "last seen" column whose tooltip has the exact time. An instant
 * slightly in the future, from clock skew between browser and server, reads as "just now".
 *
 * @param instant An ISO-8601 timestamp, or `null`/`undefined` for "never".
 * @param now The instant to measure from; injectable so a test can pin it.
 * @returns The relative description, or `null` when there is no instant at all.
 */
export function formatRelativeTime(
  instant: string | null | undefined,
  now: Date = new Date(),
): string | null {
  if (!instant) return null;

  const then = new Date(instant).getTime();
  if (Number.isNaN(then)) return null;

  // Whole seconds elapsed, floored at zero so a small clock skew never reads as negative.
  const seconds = Math.max(0, Math.floor((now.getTime() - then) / 1000));

  if (seconds < 60) return 'just now';
  const minutes = Math.floor(seconds / 60);
  if (minutes < 60) return `${minutes} min ago`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${hours} h ago`;
  const days = Math.floor(hours / 24);
  if (days < 365) return `${days} d ago`;
  return `${Math.floor(days / 365)} y ago`;
}

/**
 * This function returns the start of the local calendar day containing `now`, as an ISO-8601 UTC
 * timestamp. "New today" means the viewer's day, and the server does not know the viewer's time
 * zone, so the page sends this instant.
 *
 * @param now The instant whose local day to use; injectable so a test can pin it.
 */
export function startOfLocalDayIso(now: Date = new Date()): string {
  const midnight = new Date(now.getFullYear(), now.getMonth(), now.getDate());
  return midnight.toISOString();
}

/**
 * This function renders a .NET `TimeSpan` as sent in JSON (`[d.]hh:mm:ss[.fffffff]`) in plain
 * words, such as "10 minutes", "1 hour 30 minutes" or "2 days". Fractional seconds are dropped.
 *
 * @param value The serialized `TimeSpan`, or `null`/`undefined`.
 * @returns The description, or `null` when the value is missing or not a `TimeSpan`.
 */
export function describeTimeSpan(value: string | null | undefined): string | null {
  const match = value ? /^(?:(\d+)\.)?(\d{1,2}):(\d{2}):(\d{2})(?:\.\d+)?$/.exec(value) : null;
  if (!match) return null;

  const parts: [number, string][] = [
    [Number(match[1] ?? 0), 'day'],
    [Number(match[2]), 'hour'],
    [Number(match[3]), 'minute'],
    [Number(match[4]), 'second'],
  ];
  const words = parts
    .filter(([amount]) => amount > 0)
    .map(([amount, unit]) => `${amount} ${unit}${amount === 1 ? '' : 's'}`);

  return words.length > 0 ? words.join(' ') : '0 seconds';
}
