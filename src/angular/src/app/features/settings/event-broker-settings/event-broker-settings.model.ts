/**
 * This function builds a broker sub-object (`rabbitMq`/`activeMq`) for
 * `EventBrokerSettingsController.Patch`'s own merge-patch body, omitting the `password` key
 * entirely whenever the caller left that field blank.
 *
 * This is a real, checked-not-assumed judgment call:
 * `EventBrokerSettingsController.Get` never returns a broker's own `Password` at all
 * (confirmed directly against `RabbitMqBrokerSettingsDetail`/`ActiveMqBrokerSettingsDetail`'s own
 * doc comments -- deliberately never returned, the same reveal-once posture every other
 * secret-bearing entity in this project already follows), and `EventBrokerSettingsController.Patch`
 * only ever writes a field that is *present* in the merge-patch JSON at all (confirmed directly
 * against `AddStringField`'s own `TryGetMergePatchField` guard) -- a field simply absent from the
 * patch body is left completely untouched in the configuration file, including `password`. Since
 * this form can never pre-fill the current password (it never received it), leaving the password
 * input blank on submit must mean "leave unchanged," not "clear it" -- which this function
 * implements by never including the `password` key in the built object at all when the input is
 * blank, exactly matching the backend's own real "absent means untouched" merge-patch semantics.
 */
export function buildBrokerPatchFields<T extends { password: string }>(
  values: T,
): Omit<T, 'password'> & { password?: string } {
  const { password, ...rest } = values;
  return password.trim().length > 0 ? { ...rest, password } : rest;
}
