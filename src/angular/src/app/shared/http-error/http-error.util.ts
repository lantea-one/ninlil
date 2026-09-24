import { HttpErrorResponse } from '@angular/common/http';

/**
 * This function extracts a human-readable message out of an {@link HttpErrorResponse} raised by
 * this project's own backend. Every administrative REST controller in this project -- Identity/
 * Auth (`AuthController`/`TwoFactorController`/`ApiTokensController`/`UsersController`/
 * `RolesController`), zone-and-key-management (`ZonesController`/`TsigKeysController`/
 * `DnssecKeysController`/`CloudProviderCredentialsController`), `RecordsController`, filtering/
 * forwarding (`FilterListsController`/`FilterListEntriesController`/`ClientGroupsController`/
 * `ClientGroupNetworksController`/`ClientGroupListAssignmentsController`/`ForwardersController`),
 * webhooks/settings (`WebhooksController`/`PresetsController`/`EventBrokerSettingsController`/
 * `RateLimitingSettingsController`/`DnsCookieSettingsController`), and the five controllers that
 * were built before this hierarchy existed and only converted onto it afterward
 * (`DnsRewritesController`/`QueryLogController`/`QueryLoggingSettingsController`/
 * `BackupController`/`ReverseProxySettingsController`) -- now throws this project's own custom
 * exception hierarchy, producing a single, consistent RFC 7807 `ProblemDetails` shape
 * (`application/problem+json`, from the shared, registered `NinlilExceptionHandler`) for every
 * one of them. Confirmed directly by grep across every controller's own source, not assumed: no
 * controller anywhere in this project still hand-rolls `NotFound()`/`Conflict("...")`/
 * `Problem(...)`/`BadRequest("...")`.
 *
 * The older, ad hoc shapes this function still falls back to below are therefore reachable only
 * from the small, deliberately-untouched authentication/authorization surface this hierarchy was
 * never meant to cover (`AuthController.Login`'s own bare `Unauthorized()`/`423 Locked`, and every
 * controller's own `user is null -> Unauthorized()` defensive check -- `401`/`403` responses stay
 * exactly as ASP.NET Core's own pipeline already produces them), and from a genuine network
 * failure this function must always handle regardless of backend shape:
 *
 * 1. The new shape's own field-keyed validation breakdown (`errors`, an
 *    `HttpValidationProblemDetails`-style object of `fieldOrCode -> message[]`, the shape a
 *    converted `ValidationException` carrying more than one distinct failure produces -- an
 *    `IdentityResult` reporting a duplicate user name, for example). Read first, ahead of
 *    `detail`, since it carries the more specific, per-failure messages a generic
 *    "One or more validation errors occurred." `detail` string would otherwise mask.
 * 2. RFC 7807 `ProblemDetails`' own `detail` field.
 * 3. That same body's own `title` field, when `detail` is present but empty.
 * 4. A bare JSON string body -- Angular's `HttpClient` already parses this into a plain JS string
 *    before this function ever sees it. No converted controller produces this shape anymore; this
 *    case exists only as a defensive fallback.
 * 5. No body at all (a `401`/`423` authentication/authorization response, or a network failure),
 *    for which this function falls back to a generic message built from the HTTP status text.
 *
 * Every zone/record form and list action in this project routes its own caught error through this
 * one function, so a caller never sees a raw `[object Object]` or an unhandled stack trace surface
 * in the UI.
 */
export function extractErrorMessage(error: unknown, fallback = 'Something went wrong.'): string {
  if (!(error instanceof HttpErrorResponse)) return fallback;

  const body: unknown = error.error;

  if (body && typeof body === 'object') {
    const problem = body as { detail?: string; title?: string; errors?: Record<string, string[]> };

    if (problem.errors && typeof problem.errors === 'object') {
      const messages = Object.values(problem.errors).flat();
      if (messages.length > 0) return messages.join(' ');
    }

    if (typeof problem.detail === 'string' && problem.detail.trim().length > 0) {
      return problem.detail;
    }
    if (typeof problem.title === 'string' && problem.title.trim().length > 0) {
      return problem.title;
    }
  }

  if (typeof body === 'string' && body.trim().length > 0) return body;

  if (error.status === 0) return 'Could not reach the server. Check your connection and try again.';
  if (error.status === 404)
    return 'That item could not be found -- it may have already been deleted.';
  if (error.status === 409) return 'This change conflicts with the current state of the data.';

  return `${fallback} (HTTP ${error.status})`;
}
