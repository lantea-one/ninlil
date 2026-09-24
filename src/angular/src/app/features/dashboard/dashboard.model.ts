/**
 * This interface describes one derived "quick stat" tile -- a total count read cheaply from a
 * list endpoint's own `PagedResult.totalCount` via a minimal `skip=0&take=1` request (never a
 * full page of data fetched just to count it).
 * `count: null` means the request itself failed (most plausibly because the signed-in caller
 * lacks that resource's own `*:read` permission -- the dashboard route itself requires no
 * specific permission, so this is a real, expected case, not just a defensive fallback), rendered
 * as an explicit "unavailable" state rather than a misleading `0`.
 */
export interface QuickStat {
  /** The tile's own label, e.g. `Zones`. */
  label: string;

  /** The `@coreui/icons` icon name (registered in `app.config.ts`) shown alongside the count. */
  icon: string;

  /** The resource's own total count, or `null` if it could not be fetched. */
  count: number | null;
}

/**
 * This interface describes one row of the Dashboard's "Getting Started" checklist -- each one
 * ties a specific onboarding action to a live completion check against a real REST endpoint, so
 * the card always reflects this session's actual current server state rather than a static list
 * of suggestions. See `GettingStartedChecklistService.fetchItems` for how each `complete` value
 * is derived, and for why a row this session cannot even read (its own underlying `*:read`
 * permission is absent) never appears here at all rather than rendering as permanently
 * incomplete.
 */
export interface GettingStartedItem {
  /** A stable identifier for this row, used only for `@for` tracking (dismissal is per-card). */
  id: string;

  /** The row's own label, e.g. `Add your first zone`. */
  label: string;

  /** Whether this row's underlying condition is currently satisfied. */
  complete: boolean;

  /** The route this row's own link navigates to when clicked. */
  routerLink: string;
}
