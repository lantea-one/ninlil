import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { extractErrorMessage } from '../../shared/http-error/http-error.util';

/**
 * This service downloads a backup archive from `POST /api/backup` with a plain
 * {@link HttpClient} call instead of the generated `BackupService.apiBackupPost`. The OpenAPI
 * document declares no response schema for that endpoint, so the generated method expects JSON
 * and would fail to parse the binary body; this call asks for a `blob`. `authInterceptor` still
 * adds the Bearer token, as it does for every request.
 */
@Injectable({ providedIn: 'root' })
export class BackupDownloadService {
  private readonly httpClient = inject(HttpClient);

  /**
   * This method requests a backup archive, encrypted with `passphrase` unless it is empty, and
   * returns it as a {@link Blob}. On failure it throws an `Error` with a readable message.
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
   * This method gets a readable message from a {@link downloadBackup} error. With
   * `responseType: 'blob'` the error body is a `Blob` too, which `extractErrorMessage` cannot
   * read, so this reads the text, takes `detail` or `title` from JSON, or uses the text itself.
   * Other errors (a network failure, no body) go to `extractErrorMessage`.
   */
  private async extractBlobErrorMessage(error: unknown): Promise<string> {
    if (error instanceof HttpErrorResponse && error.error instanceof Blob) {
      const bodyText = await error.error.text();

      try {
        const parsed = JSON.parse(bodyText) as { detail?: string; title?: string };
        if (parsed.detail) return parsed.detail;
        if (parsed.title) return parsed.title;
      } catch {
        // Not JSON: show the plain text body as it is.
      }

      if (bodyText.trim().length > 0) return bodyText;
    }

    return extractErrorMessage(error, 'Failed to create the backup archive.');
  }
}
