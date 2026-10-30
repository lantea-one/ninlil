import { timeZoneOptions } from './time-zone.util';

/** These tests cover the schedule forms' time zone list: UTC first, no duplicates, stored id kept. */
describe('timeZoneOptions', () => {
  /** This helper makes `Intl.supportedValuesOf` answer with `zones` for the current test. */
  function stubBrowserZones(zones: string[]): void {
    spyOn(
      Intl as unknown as { supportedValuesOf: () => string[] },
      'supportedValuesOf',
    ).and.returnValue(zones);
  }

  it('puts UTC first when the browser list does not contain it', () => {
    stubBrowserZones(['Europe/Paris', 'America/New_York']);

    expect(timeZoneOptions()).toEqual(['UTC', 'Europe/Paris', 'America/New_York']);
  });

  it('lists UTC once when the browser list already contains it', () => {
    stubBrowserZones(['Europe/Paris', 'UTC', 'Asia/Tokyo']);

    const options = timeZoneOptions();

    expect(options[0]).toBe('UTC');
    expect(options.filter((zone) => zone === 'UTC').length).toBe(1);
    expect(options).toEqual(['UTC', 'Europe/Paris', 'Asia/Tokyo']);
  });

  it('keeps a stored id the browser does not list', () => {
    stubBrowserZones(['Asia/Kolkata']);

    expect(timeZoneOptions('Asia/Calcutta')).toEqual(['UTC', 'Asia/Kolkata', 'Asia/Calcutta']);
  });

  it('adds nothing for a stored id the list already holds', () => {
    stubBrowserZones(['Asia/Kolkata']);

    expect(timeZoneOptions('Asia/Kolkata')).toEqual(['UTC', 'Asia/Kolkata']);
    expect(timeZoneOptions('UTC')).toEqual(['UTC', 'Asia/Kolkata']);
  });

  it('falls back to a short list that starts with UTC when the browser has no list', () => {
    stubBrowserZones([]);

    const options = timeZoneOptions();

    expect(options[0]).toBe('UTC');
    expect(options).toContain('Europe/London');
  });
});
