/**
 * This function builds a broker sub-object (`rabbitMq` or `activeMq`) for the
 * `PATCH /api/event-broker-settings` merge-patch body, leaving out the `password` key when the
 * field is blank.
 *
 * `EventBrokerSettingsController.Get` never returns a broker password, so the form cannot show
 * the current one, and `Patch` leaves a field that is absent from the body unchanged. Omitting a
 * blank password therefore means "keep the stored password", where sending it would clear it.
 */
export function buildBrokerPatchFields<T extends { password: string }>(
  values: T,
): Omit<T, 'password'> & { password?: string } {
  const { password, ...rest } = values;
  return password.trim().length > 0 ? { ...rest, password } : rest;
}
