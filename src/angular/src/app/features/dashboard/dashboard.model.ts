/**
 * This interface describes one quick-stat tile: a total read from a list endpoint's
 * `PagedResult.totalCount` with a `skip=0&take=1` request. `count: null` means the request failed,
 * usually because the user lacks that resource's read permission (the dashboard itself needs
 * none), and the tile shows "unavailable" instead of a misleading `0`.
 */
export interface QuickStat {
  /** The tile label, e.g. `Zones`. */
  label: string;

  /** The `@coreui/icons` icon shown with the count; it must be registered in `app.config.ts`. */
  icon: string;

  /** The total count, or `null` if it could not be fetched. */
  count: number | null;
}

/**
 * This interface describes one row of the Dashboard's "Getting Started" checklist: an onboarding
 * step and whether the server state shows it done. `GettingStartedChecklistService.fetchItems`
 * computes each row; a row whose read permission the user lacks is left out.
 */
export interface GettingStartedItem {
  /** A stable id for `@for` tracking (the card is dismissed as a whole, not per row). */
  id: string;

  /** The row label, e.g. `Add your first zone`. */
  label: string;

  /** Whether the step is done. */
  complete: boolean;

  /** The route the row links to. */
  routerLink: string;
}
