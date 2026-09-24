/**
 * This constant lists every TSIG MAC algorithm name this backend actually accepts, confirmed
 * directly against `TsigKeysController.TryResolveAlgorithmName` and `DnsTsigOptions
 * .HmacSha256AlgorithmName` (`"hmac-sha256"`, dot-stripped) rather than assumed from RFC 8945's
 * own full algorithm registry -- Ninlil deliberately implements exactly this one algorithm.
 * Exposed as a select with a single option, rather than a plain read-only label, so a future
 * algorithm addition to the backend needs only a change here, not a new form control.
 */
export const TSIG_ALGORITHM_OPTIONS: readonly string[] = ['hmac-sha256'];
