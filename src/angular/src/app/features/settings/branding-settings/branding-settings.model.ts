/**
 * This constant contains the largest accepted logo, in bytes before base64 encoding, checked
 * before a selected file is read. It copies `BrandingOptions.MaxLogoSizeBytes`
 * (`Ninlil.Web/Options/BrandingOptions.cs`), which the API does not expose, so it must be changed
 * together with that constant.
 */
export const MAX_LOGO_SIZE_BYTES = 512 * 1024;

/**
 * This function reads a selected {@link File} into a data URI (for example
 * `data:image/png;base64,...`). The logo is stored as that string (`BrandingOptions.LogoDataUri`)
 * and sent in the `PATCH` body; there is no upload endpoint. It wraps the callback-based
 * {@link FileReader.readAsDataURL} in a promise and lives outside the component so it can be
 * tested on its own.
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
 * This function checks a selected {@link File} before it is read: an `image/` MIME type and a size
 * within {@link MAX_LOGO_SIZE_BYTES}, the same two checks `BrandingSettingsController.ValidateLogoDataUri`
 * makes. It returns a message to show, or `null` when the file is acceptable.
 */
export function validateLogoFile(file: File): string | null {
  if (!file.type.startsWith('image/'))
    return `'${file.name}' is not an image (its browser-reported type was '${file.type || 'unknown'}').`;

  if (file.size > MAX_LOGO_SIZE_BYTES)
    return `'${file.name}' is too large -- the logo may not exceed ${Math.floor(MAX_LOGO_SIZE_BYTES / 1024)} KB.`;

  return null;
}
