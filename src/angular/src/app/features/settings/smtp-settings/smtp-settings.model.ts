/**
 * This function builds the `PATCH /api/smtp-settings` merge-patch body, leaving out the `password`
 * key when the field is blank.
 *
 * `SmtpSettingsController.Get` never returns the password (only `passwordConfigured`), and `Patch`
 * leaves an absent field unchanged, so a blank input means "keep the stored password". Clearing
 * the password is possible through the API but not from this form, since one text box cannot tell
 * "left empty" from "emptied on purpose".
 */
export function buildSmtpSettingsPatchBody<T extends { password: string }>(
  values: T,
): Omit<T, 'password'> & { password?: string } {
  const { password, ...rest } = values;
  return password.trim().length > 0 ? { ...rest, password } : rest;
}
