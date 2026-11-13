import { BRAND_ICON, NAV_ITEMS } from './nav-item.model';

/**
 * These tests cover {@link NAV_ITEMS}: every sidebar entry has its own icon, and none repeats the brand icon, so two entries are
 * never told apart by their label alone. A pure data check, no TestBed.
 */
describe('NAV_ITEMS', () => {
  it('gives no two nav items the same icon', () => {
    // The brand icon counts as an owner, so a menu entry cannot repeat it.
    const owners = new Map<string, string[]>([[BRAND_ICON, ['brand']]]);
    for (const item of NAV_ITEMS) {
      owners.set(item.icon, [...(owners.get(item.icon) ?? []), item.label]);
    }

    const shared = [...owners.entries()].filter(([, labels]) => labels.length > 1);

    expect(shared).toEqual([]);
  });
});
