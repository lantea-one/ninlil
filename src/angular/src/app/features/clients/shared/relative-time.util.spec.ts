import { describeTimeSpan, formatRelativeTime, startOfLocalDayIso } from './relative-time.util';

/** These tests cover the coarse "time ago" text, missing values and clock skew. */
describe('formatRelativeTime', () => {
  const now = new Date('2026-09-23T12:00:00Z');

  it('returns null for a missing or unparseable instant', () => {
    expect(formatRelativeTime(null, now)).toBeNull();
    expect(formatRelativeTime(undefined, now)).toBeNull();
    expect(formatRelativeTime('not a date', now)).toBeNull();
  });

  it('describes each magnitude coarsely', () => {
    expect(formatRelativeTime('2026-09-23T11:59:30Z', now)).toBe('just now');
    expect(formatRelativeTime('2026-09-23T11:55:00Z', now)).toBe('5 min ago');
    expect(formatRelativeTime('2026-09-23T09:00:00Z', now)).toBe('3 h ago');
    expect(formatRelativeTime('2026-09-21T12:00:00Z', now)).toBe('2 d ago');
    expect(formatRelativeTime('2024-09-01T12:00:00Z', now)).toBe('2 y ago');
  });

  it('reads a slightly future instant (clock skew) as just now', () => {
    expect(formatRelativeTime('2026-09-23T12:00:20Z', now)).toBe('just now');
  });
});

/** This test checks that the local midnight is returned as a UTC ISO string. */
describe('startOfLocalDayIso', () => {
  it('returns local midnight of the given day', () => {
    const now = new Date(2026, 8, 23, 15, 42, 7);
    const midnight = new Date(startOfLocalDayIso(now));

    expect(midnight.getFullYear()).toBe(2026);
    expect(midnight.getMonth()).toBe(8);
    expect(midnight.getDate()).toBe(23);
    expect(midnight.getHours()).toBe(0);
    expect(midnight.getMinutes()).toBe(0);
  });
});

/** These tests cover turning a serialized `TimeSpan` into words, and malformed input. */
describe('describeTimeSpan', () => {
  it('describes a serialized TimeSpan in words', () => {
    expect(describeTimeSpan('00:10:00')).toBe('10 minutes');
    expect(describeTimeSpan('01:30:00')).toBe('1 hour 30 minutes');
    expect(describeTimeSpan('2.00:00:00')).toBe('2 days');
    expect(describeTimeSpan('00:00:01.5000000')).toBe('1 second');
  });

  it('returns null for a missing or malformed value', () => {
    expect(describeTimeSpan(null)).toBeNull();
    expect(describeTimeSpan('ten minutes')).toBeNull();
  });
});
