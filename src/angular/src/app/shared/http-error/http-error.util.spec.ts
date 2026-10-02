import { HttpErrorResponse } from '@angular/common/http';
import { extractErrorMessage } from './http-error.util';

/**
 * These tests cover `extractErrorMessage`: each body shape it reads (validation `errors`,
 * `detail`, `title`, a plain string), the fixed messages for 0, 404 and 409, and the fallbacks.
 * Plain calls with hand-built `HttpErrorResponse` values.
 */
describe('extractErrorMessage', () => {
  it('returns a bare JSON string body verbatim (Conflict("...")/BadRequest("..."))', () => {
    const error = new HttpErrorResponse({ error: 'A zone named foo already exists.', status: 409 });
    expect(extractErrorMessage(error)).toBe('A zone named foo already exists.');
  });

  it('joins a field-keyed errors dictionary (a ValidationException carrying multiple failures)', () => {
    const error = new HttpErrorResponse({
      error: {
        title: 'One or more validation errors occurred.',
        status: 400,
        detail: 'One or more validation errors occurred.',
        errors: { DuplicateUserName: ["Username 'bob' is already taken."] },
      },
      status: 400,
    });
    expect(extractErrorMessage(error)).toBe("Username 'bob' is already taken.");
  });

  it('joins every message across multiple error-dictionary keys', () => {
    const error = new HttpErrorResponse({
      error: {
        status: 400,
        errors: {
          DuplicateUserName: ["Username 'bob' is already taken."],
          PasswordTooShort: ['Passwords must be at least 8 characters.'],
        },
      },
      status: 400,
    });
    const message = extractErrorMessage(error);
    expect(message).toContain("Username 'bob' is already taken.");
    expect(message).toContain('Passwords must be at least 8 characters.');
  });

  it('falls back to detail when the errors dictionary is present but empty', () => {
    const error = new HttpErrorResponse({
      error: { status: 400, detail: 'Invalid request.', errors: {} },
      status: 400,
    });
    expect(extractErrorMessage(error)).toBe('Invalid request.');
  });

  it("reads a NotFoundException-shaped ProblemDetails' own detail and extension fields", () => {
    const error = new HttpErrorResponse({
      error: {
        title: 'Not found.',
        status: 404,
        detail: "User 'abc-123' could not be found.",
        entityType: 'User',
        entityId: 'abc-123',
      },
      status: 404,
    });
    expect(extractErrorMessage(error)).toBe("User 'abc-123' could not be found.");
  });

  it('reads ProblemDetails\' own detail field (Problem(detail: "..."))', () => {
    const error = new HttpErrorResponse({
      error: { type: 'about:blank', title: 'Bad Request', status: 400, detail: 'not a valid name' },
      status: 400,
    });
    expect(extractErrorMessage(error)).toBe('not a valid name');
  });

  it('falls back to the ProblemDetails title when detail is missing', () => {
    const error = new HttpErrorResponse({
      error: { title: 'Bad Request', status: 400 },
      status: 400,
    });
    expect(extractErrorMessage(error)).toBe('Bad Request');
  });

  it('gives a dedicated message for a 404 with no body', () => {
    const error = new HttpErrorResponse({ status: 404 });
    expect(extractErrorMessage(error)).toContain('could not be found');
  });

  it('gives a dedicated message for a 409 with no body', () => {
    const error = new HttpErrorResponse({ status: 409 });
    expect(extractErrorMessage(error)).toContain('conflicts');
  });

  it('gives a dedicated message for a network failure (status 0)', () => {
    const error = new HttpErrorResponse({ status: 0 });
    expect(extractErrorMessage(error)).toContain('Could not reach the server');
  });

  it('falls back to a generic message with the status code for anything else', () => {
    const error = new HttpErrorResponse({ status: 500 });
    expect(extractErrorMessage(error, 'Save failed.')).toBe('Save failed. (HTTP 500)');
  });

  it('returns the fallback unchanged for a non-HttpErrorResponse value', () => {
    expect(extractErrorMessage(new Error('boom'), 'Save failed.')).toBe('Save failed.');
  });
});
