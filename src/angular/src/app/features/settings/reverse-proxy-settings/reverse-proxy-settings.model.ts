/**
 * This function splits a textarea's own one-entry-per-line text into a trimmed, non-empty-only
 * array -- the same helper `ZoneFormComponent`'s own `splitAddressLines` already establishes for
 * `primaryServerAddresses`/`forwarderAddresses`, applied here to `knownProxies`/`knownNetworks`
 * so an operator can type one IP address or CIDR network per line rather than hand-authoring a
 * JSON array.
 */
export function splitLines(text: string): string[] {
  return text
    .split('\n')
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

/** This function is `splitLines`'s own inverse, used to populate a textarea from a loaded array. */
export function joinLines(values: readonly string[] | null | undefined): string {
  return (values ?? []).join('\n');
}

/**
 * This function builds the `sharedSecret` field for `ReverseProxySettingsController.Patch`'s own
 * merge-patch body, omitting it entirely whenever the caller left the input blank.
 *
 * `ReverseProxySettingsController.Get` never returns the configured shared secret at all
 * (confirmed directly against `ReverseProxySettingsDetail`'s own doc comment -- only a
 * `sharedSecretConfigured` boolean indicator is ever returned), and `Patch` only ever writes a
 * field that is *present* in the merge-patch JSON body at all (the same `TryGetMergePatchField`
 * guard `EventBrokerSettingsController`'s own `buildBrokerPatchFields` precedent already relies
 * on) -- a field simply absent from the patch is left completely untouched in the configuration
 * file, including `sharedSecret`. Since this form can never pre-fill the current secret (it never
 * received it), leaving the secret input blank on submit must mean "leave unchanged," not "clear
 * it" -- exactly the reasoning `buildBrokerPatchFields` already established for a broker's own
 * `password` field, applied here to a field with a different name that function's own generic
 * signature does not cover.
 */
export function buildSharedSecretPatchField(sharedSecret: string): { sharedSecret?: string } {
  return sharedSecret.trim().length > 0 ? { sharedSecret } : {};
}
