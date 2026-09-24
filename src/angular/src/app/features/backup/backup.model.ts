/**
 * This constant contains the fewest characters a backup passphrase must contain when one is
 * supplied at all. It mirrors this project's real source of truth,
 * `BackupOptions.MinimumPassphraseLength`
 * (`src/dotnet/src/Ninlil.Dns.Server.Backup/Options/BackupOptions.cs`), hardcoded here rather
 * than read live because `BackupOptions` has no REST settings surface of its own to read it
 * from -- if one is ever added, this constant should be replaced by reading the real value from
 * it instead of duplicating it a second place.
 */
export const MINIMUM_BACKUP_PASSPHRASE_LENGTH = 12;

/**
 * This function builds the default file name a downloaded backup archive is saved under,
 * matching `BackupController.Create`'s own `ninlil-backup-{yyyyMMdd-HHmmss}.ltbk` naming
 * convention exactly, computed client-side since the server's own `Content-Disposition` header
 * is not read by this feature's own download call (a raw `HttpClient` request bypassing the
 * generated API client entirely -- see `backup.service.ts`'s own doc comment for why).
 */
export function buildBackupFileName(now: Date = new Date()): string {
  const pad = (value: number) => value.toString().padStart(2, '0');

  const datePart = `${now.getFullYear()}${pad(now.getMonth() + 1)}${pad(now.getDate())}`;
  const timePart = `${pad(now.getHours())}${pad(now.getMinutes())}${pad(now.getSeconds())}`;

  return `ninlil-backup-${datePart}-${timePart}.ltbk`;
}

/**
 * This function triggers a real browser download of `blob`, saved as `fileName`, via a
 * temporary, invisible anchor element -- the standard technique for turning an in-memory
 * `Blob` (already fetched over XHR/`fetch`, not a plain hyperlink) into a "Save As" prompt,
 * since neither `HttpClient` nor the DOM offers a more direct API for it.
 */
export function triggerBlobDownload(blob: Blob, fileName: string): void {
  const objectUrl = URL.createObjectURL(blob);

  try {
    const anchor = document.createElement('a');
    anchor.href = objectUrl;
    anchor.download = fileName;
    anchor.style.display = 'none';
    document.body.appendChild(anchor);
    anchor.click();
    document.body.removeChild(anchor);
  } finally {
    // Revoking synchronously right after click() is safe -- the browser has already queued the
    // navigation/download by the time click() returns, and holding the object URL open any
    // longer than that would just leak memory for the life of this page.
    URL.revokeObjectURL(objectUrl);
  }
}
