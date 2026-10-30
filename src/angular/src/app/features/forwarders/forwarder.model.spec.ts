import { KNOWN_FORWARDER_OPTIONS, deriveForwarderName } from './forwarder.model';

/**
 * Covers the resolver presets in `KNOWN_FORWARDER_OPTIONS` that the forwarder form's dropdown
 * offers: the data is plain, so the tests check the invariants the dropdown and the bulk-create
 * naming rely on (unique values and names, one group per provider, well-formed definition strings).
 */
describe('KNOWN_FORWARDER_OPTIONS', () => {
  /** The providers the dropdown offers, in the order of their first entry. */
  const providers = [
    'Cloudflare',
    'Google Public DNS',
    'Quad9',
    'OpenDNS',
    'Mullvad DNS',
    'DNS4EU',
    'AdGuard DNS',
    'Control D',
    'CleanBrowsing',
  ];

  it('offers_everyPreset_aUniqueName', () => {
    const names = KNOWN_FORWARDER_OPTIONS.map((option) => option.name);

    expect(new Set(names).size).toBe(names.length);
  });

  it('offers_everyPreset_aUniqueValue', () => {
    // The dropdown tracks options by value and deriveForwarderName looks a line up by value.
    const values = KNOWN_FORWARDER_OPTIONS.map((option) => option.value);

    expect(new Set(values).size).toBe(values.length);
  });

  it('offers_everyPreset_aUniqueLabelWithinItsProvider', () => {
    const labels = KNOWN_FORWARDER_OPTIONS.map((option) => `${option.provider}/${option.label}`);

    expect(new Set(labels).size).toBe(labels.length);
  });

  it('groups_theProviders_inOneContiguousBlockEach', () => {
    const order = [...new Set(KNOWN_FORWARDER_OPTIONS.map((option) => option.provider))];

    expect(order).toEqual(providers);
    // A provider that came back after another one would split its <optgroup> from the next
    // first-seen position, so the entries of each provider must be adjacent.
    const runs = KNOWN_FORWARDER_OPTIONS.filter(
      (option, index) =>
        index === 0 || KNOWN_FORWARDER_OPTIONS[index - 1].provider !== option.provider,
    );
    expect(runs.length).toBe(providers.length);
  });

  it('startsEveryName_withItsProviderAndFitsTheNameColumn', () => {
    for (const option of KNOWN_FORWARDER_OPTIONS) {
      expect(option.name.startsWith(`${option.provider} (`)).toBeTrue();
      // The forwarder name column holds 255 characters.
      expect(option.name.length).toBeLessThanOrEqual(255);
    }
  });

  it('writesEveryValue_inTheSyntaxTheParserAccepts', () => {
    for (const option of KNOWN_FORWARDER_OPTIONS) {
      const value = option.value;
      if (value.startsWith('tls://') || value.startsWith('quic://')) {
        // An encrypted preset names a host (or a bracketed IPv6 literal) without a path.
        expect(value).toMatch(/^(tls|quic):\/\/([a-z0-9.-]+|\[[0-9a-f:]+\])$/);
      } else if (value.startsWith('https://')) {
        expect(value).toMatch(/^https:\/\/[a-z0-9.-]+\/[a-z0-9/-]+$/);
      } else if (value.startsWith('[')) {
        // An IPv6 literal is bracketed.
        expect(value).toMatch(/^\[[0-9a-f:]+\]$/);
      } else {
        expect(value).toMatch(/^\d{1,3}(\.\d{1,3}){3}$/);
      }
    }
  });

  it('offersDoq_onlyForProvidersThatDocumentIt', () => {
    const doqProviders = new Set(
      KNOWN_FORWARDER_OPTIONS.filter((option) => option.value.startsWith('quic://')).map(
        (option) => option.provider,
      ),
    );

    expect([...doqProviders].sort()).toEqual(['AdGuard DNS', 'Control D']);
  });

  it('deriveForwarderName_forAPresetValue_returnsThePresetName', () => {
    const preset = KNOWN_FORWARDER_OPTIONS.find(
      (option) => option.value === 'quic://dns.adguard-dns.com',
    );

    expect(preset).toBeDefined();
    expect(deriveForwarderName(preset!.value, 1, 3, 'ignored')).toBe('AdGuard DNS (Default, DoQ)');
  });
});
