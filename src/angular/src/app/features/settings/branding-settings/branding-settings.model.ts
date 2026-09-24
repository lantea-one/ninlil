/**
 * This constant contains the maximum accepted logo size, in raw (pre-base64) bytes, this page
 * enforces client-side before ever reading a selected file into a data URI -- mirrors the real
 * backend bound (`BrandingOptions.MaxLogoSizeBytes`, `Ninlil.Web/Options/BrandingOptions.cs`)
 * exactly, the same "no live settings surface for this constant yet, so hardcode it with a comment
 * pointing at the real source of truth" convention `backup.model.ts`'s own
 * `MINIMUM_BACKUP_PASSPHRASE_LENGTH` already established -- update this value here if that backend
 * constant is ever changed.
 */
export const MAX_LOGO_SIZE_BYTES = 512 * 1024;

/**
 * This function reads a browser-selected {@link File} into a complete data URI (for example
 * `data:image/png;base64,...`) -- this project's confirmed logo storage mechanism (a plain string
 * field, `BrandingOptions.LogoDataUri`, sent as-is in the `PATCH` body, never a dedicated multipart
 * upload endpoint). Wraps {@link FileReader.readAsDataURL} in a promise, since that API is
 * callback-based, and is exported standalone (rather than inlined into the component) so it can be
 * unit-tested in isolation -- the same "externalize the pure conversion for testability" shape
 * `storage-settings.model.ts`'s own `buildStorageSwitchRequest` already established.
 */
export function readFileAsDataUri(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(reader.result as string);
    reader.onerror = () => reject(reader.error ?? new Error('Could not read the selected file.'));
    reader.readAsDataURL(file);
  });
}

/**
 * This function validates a browser-selected {@link File} before it is ever read into a data URI
 * or sent to the backend -- a real image MIME type and a size within {@link MAX_LOGO_SIZE_BYTES} --
 * mirroring, client-side, the exact two checks `BrandingSettingsController.ValidateLogoDataUri`
 * performs server-side. Returns a human-readable rejection reason, or `null` when the file is
 * acceptable, so the caller can show it directly rather than duplicating the message.
 */
export function validateLogoFile(file: File): string | null {
  if (!file.type.startsWith('image/'))
    return `'${file.name}' is not an image (its browser-reported type was '${file.type || 'unknown'}').`;

  if (file.size > MAX_LOGO_SIZE_BYTES)
    return `'${file.name}' is too large -- the logo may not exceed ${Math.floor(MAX_LOGO_SIZE_BYTES / 1024)} KB.`;

  return null;
}
