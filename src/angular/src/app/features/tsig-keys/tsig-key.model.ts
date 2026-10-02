/**
 * This constant lists the TSIG algorithms the backend accepts (`TsigKeysController.
 * TryResolveAlgorithmName`): only `hmac-sha256` (`DnsTsigOptions.HmacSha256AlgorithmName` without
 * the trailing dot), not the whole RFC 8945 registry. The form shows it as a select, so another
 * algorithm would need only a new entry here.
 */
export const TSIG_ALGORITHM_OPTIONS: readonly string[] = ['hmac-sha256'];
