/**
 * This interface mirrors `Ninlil.Web.Models.StatusComponent` -- part of an authenticated,
 * `status:read`-holding caller's own additive `GET /status` response. Hand-written rather than
 * generated: the backing endpoint (`EndpointRouteBuilderStatusExtension.MapNinlilDnsStatus`)
 * is a Minimal API action with no declared OpenAPI response schema (confirmed by reading the
 * committed `openapi/swagger.json` snapshot's own `/status` path directly -- its `200` response
 * carries no `content`/`schema` at all), so the generated `NinlilWebService.statusGet()`
 * returns a bare `Observable<any>`; this interface is what {@link DashboardStatsService} casts
 * that response through instead.
 */
export interface StatusComponent {
  /** The health check's own registered name, e.g. `storage`, `transports`. */
  name: string;

  /** This one component's own status: `Healthy`, `Degraded`, or `Unhealthy`. */
  status: string;

  /** A human-readable description of this component's own current state. */
  description: string;

  /** The message of an exception this check raised, if any, or `null`. */
  error: string | null;
}

/**
 * This interface mirrors `Ninlil.Web.Models.StatusResponse` -- see
 * {@link StatusComponent}'s own doc comment for why this is hand-written rather than generated.
 * `components` is `null` for an unauthenticated or unpermitted caller, and populated additively
 * for one holding `status:read`, matching the backend's own deliberate additive-detail design for
 * this endpoint.
 */
export interface StatusResponse {
  /** The overall aggregate status: `Healthy`, `Degraded`, or `Unhealthy`. */
  status: string;

  /** The per-subsystem breakdown, present only for an authenticated caller with `status:read`. */
  components: StatusComponent[] | null;
}

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
