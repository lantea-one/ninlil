/**
 * This function builds `SmtpSettingsController.Patch`'s own merge-patch request body, omitting the
 * `password` key entirely whenever the caller left that field blank.
 *
 * `SmtpSettingsController.Get` never returns `EmailSenderOptions.Password` at all (confirmed
 * directly against `SmtpSettingsDetail.PasswordConfigured`'s own doc comment -- deliberately never
 * returned, the same reveal-once posture every other secret-bearing setting in this project already
 * follows, mirroring `EventBrokerSettingsController`'s own RabbitMq/ActiveMq password handling), and
 * `SmtpSettingsController.Patch` only ever writes a field that is *present* in the merge-patch JSON
 * at all -- a field simply absent from the patch body is left completely untouched in the
 * configuration file, including `password`. Since this form can never pre-fill the current
 * password (it never received it), leaving the password input blank on submit must mean "leave
 * unchanged," not "clear it" -- which this function implements by never including the `password`
 * key in the built object at all when the input is blank, exactly matching the backend's own real
 * "absent means untouched" merge-patch semantics. (A genuinely blank password can still be written
 * deliberately by an operator directly against the REST API -- this form simply never offers that
 * as a UI affordance, since there is no way to distinguish "never typed anything" from "typed
 * nothing on purpose" in a single text input.)
 */
export function buildSmtpSettingsPatchBody<T extends { password: string }>(
  values: T,
): Omit<T, 'password'> & { password?: string } {
  const { password, ...rest } = values;
  return password.trim().length > 0 ? { ...rest, password } : rest;
}
