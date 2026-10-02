import { HttpErrorResponse } from '@angular/common/http';

/**
 * This function turns an error from an API call into a message to show the user. Pages pass every
 * caught error through it, so the UI never shows `[object Object]` or a stack trace.
 *
 * <p>The backend's controllers throw Ninlil's exception types, which `NinlilExceptionHandler`
 * turns into RFC 7807 `ProblemDetails` (`application/problem+json`). Some responses have no
 * body: the 401 and 423 from login, the 401 and 403 from the authorization pipeline, and network
 * failures. The function checks, in order:
 *
 * 1. `errors`, the per-field validation messages (`fieldOrCode -> message[]`) a validation
 *    failure with several problems produces, such as a duplicate user name from Identity. They
 *    come first because the matching `detail` is only a generic summary.
 * 2. The `detail` field.
 * 3. The `title` field, when `detail` is empty.
 * 4. A plain string body, which `HttpClient` has already parsed from JSON. Kept as a fallback.
 * 5. No usable body: a fixed message for status 0 (server unreachable), 404 and 409, otherwise
 *    `fallback` with the status code.
 *
 * Any error that is not an `HttpErrorResponse` returns `fallback`.
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
