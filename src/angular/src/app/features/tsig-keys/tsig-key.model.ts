/**
 * This constant lists the TSIG algorithms the backend accepts (`TsigAlgorithm.Supported` in
 * `Ninlil.Dns.Server.Tsig`, enforced by `TsigKeysController`): the four HMAC-SHA variants in
 * RFC 8945 §6, strongest last. The key form shows it as a select and defaults to the entry
 * `hmac-sha256`.
 */
export const TSIG_ALGORITHM_OPTIONS: readonly string[] = [
  'hmac-sha1',
  'hmac-sha256',
  'hmac-sha384',
  'hmac-sha512',
];

/** This constant is the algorithm a new key starts with (the backend's default as well). */
export const DEFAULT_TSIG_ALGORITHM = 'hmac-sha256';
