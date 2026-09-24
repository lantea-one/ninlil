import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { extractErrorMessage } from '../../shared/http-error/http-error.util';

/**
 * This service downloads a full-system backup archive via a raw {@link HttpClient} request,
 * deliberately bypassing the generated `BackupService.apiBackupPost` -- the OpenAPI document
 * Swashbuckle produced for `POST /api/backup` declares no response schema at all (an
 * `ActionResult`-returning `File(...)` response carries no `[ProducesResponseType]` of its
 * own), so the generated method's own response handling defaults to `responseType: 'json'`,
 * which would try to `JSON.parse` this endpoint's real, binary octet-stream body and fail. A
 * plain `HttpClient` call with an explicit `responseType: 'blob'` sidesteps that entirely, and
 * still picks up the current session's own bearer token automatically via `authInterceptor`,
 * which attaches to every outgoing request outside `/api/auth/*` regardless of which client
 * issued it.
 */
@Injectable({ providedIn: 'root' })
export class BackupDownloadService {
  private readonly httpClient = inject(HttpClient);

  /**
   * This method requests a complete backup archive encrypted with `passphrase`, returning its
   * raw bytes as a {@link Blob}.
   */
  async downloadBackup(passphrase: string): Promise<Blob> {
    try {
      return await firstValueFrom(
        this.httpClient.post('/api/backup', { passphrase }, { responseType: 'blob' }),
      );
    } catch (error: unknown) {
      throw new Error(await this.extractBlobErrorMessage(error), { cause: error });
    }
  }

  /**
   * This method reads a human-readable message out of an error raised by
   * {@link downloadBackup}. A `responseType: 'blob'` request that fails still parses its own
   * error body as a `Blob` too -- Angular's `HttpClient` uses the same response type for both
   * the success and failure path -- so `extractErrorMessage`'s own assumption that
   * `HttpErrorResponse.error` is already a parsed JSON value or string does not hold here; this
   * reads the blob's own text content first and re-parses it the identical way
   * `extractErrorMessage` would, before falling back to it for every other error shape (a
   * network failure, a non-blob body).
   */
  private async extractBlobErrorMessage(error: unknown): Promise<string> {
    if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
      const bodyText = await error.error.text();

      try {
        const parsed = JSON.parse(bodyText) as { detail?: string; title?: string };
        if (parsed.detail) return parsed.detail;
        if (parsed.title) return parsed.title;
      } catch {
        // The body was not JSON -- a bare string body (Conflict("...")/BadRequest("...")) is
        // already exactly what should be shown.
      }

      if (bodyText.trim().length > 0) return bodyText;
    }

    return extractErrorMessage(error, 'Failed to create the backup archive.');
  }
}
