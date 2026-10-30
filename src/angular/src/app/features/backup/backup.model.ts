/**
 * This constant is the minimum length of a backup passphrase, when one is given. It copies the
 * default of `BackupOptions.MinimumPassphraseLength` (`Ninlil.Dns.Server.Backup/Options/
 * BackupOptions.cs`), since no endpoint exposes that setting. If the setting is changed in the
 * configuration file, the backend's 400 still enforces it.
 */
export const MINIMUM_BACKUP_PASSPHRASE_LENGTH = 12;

/**
 * This function builds the file name a downloaded backup is saved under, in the
 * `ninlil-backup-{yyyyMMdd-HHmmss}.ltbk` pattern `BackupController.Create` uses, in UTC like the
 * server, so the name does not depend on the browser's time zone. The download does not read the server's
 * `Content-Disposition` header, so the name is built here.
 */
export function buildBackupFileName(now: Date = new Date()): string {
  const pad = (value: number) => value.toString().padStart(2, '0');

  const datePart = `${now.getUTCFullYear()}${pad(now.getUTCMonth() + 1)}${pad(now.getUTCDate())}`;
  const timePart = `${pad(now.getUTCHours())}${pad(now.getUTCMinutes())}${pad(now.getUTCSeconds())}`;

  return `ninlil-backup-${datePart}-${timePart}.ltbk`;
}

/**
 * This function saves `blob` as a browser download named `fileName`, by clicking a temporary
 * hidden link to an object URL. The DOM has no more direct way to save an in-memory `Blob`.
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
    // Revoking right after click() is safe: the browser has already started the download, and
    // keeping the URL would hold the blob in memory until the page closes.
    URL.revokeObjectURL(objectUrl);
  }
}
