/**
 * One well-known public DNS resolver endpoint offered by `ForwarderFormComponent`'s own
 * "known resolvers" dropdown -- a single transport (plain/DoT/DoH/DoQ) of a single,
 * independently-operated public resolver.
 *
 * Every {@link value} here is a publicly documented fact about a real third-party service (an
 * address or URL the operator itself publishes for exactly this purpose), sourced from this
 * project's own general knowledge of these well-known services -- never read, copied, or derived
 * from Technitium's or dnsmasq's own source or bundled configuration, per the clean-room
 * rule in `NOTICE.md`. Each {@link value} uses exactly the definition-string syntax
 * `UpstreamForwarderDefinitionParser` accepts: a bare address for plain DNS, `tls://host[:port]`
 * for DNS-over-TLS, a complete `https://` URL for DNS-over-HTTPS, and `quic://host[:port]` for
 * DNS-over-QUIC -- confirmed directly against that parser's own source before writing any of
 * these values, not guessed.
 */
export interface KnownForwarderOption {
  /** The provider grouping this option belongs to, used as this dropdown's own `<optgroup>` label. */
  readonly provider: string;
  /** The text shown for this option inside its provider's `<optgroup>`. */
  readonly label: string;
  /** The literal forwarder definition string appended to the textarea when this option is picked. */
  readonly value: string;
  /** The forwarder name auto-derived when a bulk-create line's content exactly matches this option's {@link value}. */
  readonly name: string;
}

/**
 * This constant lists every known-public-resolver option `ForwarderFormComponent`'s dropdown
 * offers. Deliberately not exhaustive -- four independently-operated public resolvers, covering
 * every transport `UpstreamForwarderDefinitionParser` supports across the set as a whole (not
 * every provider necessarily offers every transport, since not every real provider publicly
 * documents one -- Google Public DNS and OpenDNS, for example, do not publish a DNS-over-QUIC
 * endpoint, so none is listed for them here rather than guessing one).
 *
 * Every provider also lists its own well-known IPv6 address(es) alongside its IPv4 ones, so an
 * operator running a dual-stack network can forward over either family -- an IPv6 literal always
 * uses this parser's own required bracketed form (`[addr]`, or `[addr]:port`) even with no port,
 * since the unbracketed host/port split (`ParseHostPort`'s own `LastIndexOf(':')` path) would
 * otherwise misread an IPv6 address's own colons as a port separator; confirmed directly against
 * `UpstreamForwarderDefinitionParser` before writing any of these values. Only Cloudflare's
 * DNS-over-TLS/-QUIC entries get an IPv6-literal variant, matching the existing IPv4 pattern
 * already established for that provider specifically (its own encrypted-transport entries use a
 * bare IP literal, not a hostname); Google Public DNS/Quad9's own DoT/DoH/DoQ entries already use
 * a hostname, which resolves over whichever address family the operating system prefers, so no
 * separate IPv6-literal variant is needed for those.
 */
export const KNOWN_FORWARDER_OPTIONS: readonly KnownForwarderOption[] = [
  // Cloudflare's own public resolver, 1.1.1.1 -- its plain/DoT/DoH/DoQ endpoints are all
  // documented at https://developers.cloudflare.com/1.1.1.1/.
  {
    provider: 'Cloudflare',
    label: 'Plain -- 1.1.1.1',
    value: '1.1.1.1',
    name: 'Cloudflare (Plain)',
  },
  {
    provider: 'Cloudflare',
    label: 'Plain -- 1.0.0.1 (secondary)',
    value: '1.0.0.1',
    name: 'Cloudflare (Plain, secondary)',
  },
  {
    provider: 'Cloudflare',
    label: 'DNS-over-TLS -- tls://1.1.1.1',
    value: 'tls://1.1.1.1',
    name: 'Cloudflare (DoT)',
  },
  {
    provider: 'Cloudflare',
    label: 'DNS-over-HTTPS -- https://cloudflare-dns.com/dns-query',
    value: 'https://cloudflare-dns.com/dns-query',
    name: 'Cloudflare (DoH)',
  },
  {
    provider: 'Cloudflare',
    label: 'DNS-over-QUIC -- quic://1.1.1.1',
    value: 'quic://1.1.1.1',
    name: 'Cloudflare (DoQ)',
  },
  {
    provider: 'Cloudflare',
    label: 'Plain (IPv6) -- 2606:4700:4700::1111',
    value: '[2606:4700:4700::1111]',
    name: 'Cloudflare (Plain, IPv6)',
  },
  {
    provider: 'Cloudflare',
    label: 'Plain (IPv6) -- 2606:4700:4700::1001 (secondary)',
    value: '[2606:4700:4700::1001]',
    name: 'Cloudflare (Plain, IPv6 secondary)',
  },
  {
    provider: 'Cloudflare',
    label: 'DNS-over-TLS (IPv6) -- tls://[2606:4700:4700::1111]',
    value: 'tls://[2606:4700:4700::1111]',
    name: 'Cloudflare (DoT, IPv6)',
  },
  {
    provider: 'Cloudflare',
    label: 'DNS-over-QUIC (IPv6) -- quic://[2606:4700:4700::1111]',
    value: 'quic://[2606:4700:4700::1111]',
    name: 'Cloudflare (DoQ, IPv6)',
  },

  // Google Public DNS -- documented at https://developers.google.com/speed/public-dns.
  {
    provider: 'Google Public DNS',
    label: 'Plain -- 8.8.8.8',
    value: '8.8.8.8',
    name: 'Google Public DNS (Plain)',
  },
  {
    provider: 'Google Public DNS',
    label: 'Plain -- 8.8.4.4 (secondary)',
    value: '8.8.4.4',
    name: 'Google Public DNS (Plain, secondary)',
  },
  {
    provider: 'Google Public DNS',
    label: 'DNS-over-TLS -- tls://dns.google',
    value: 'tls://dns.google',
    name: 'Google Public DNS (DoT)',
  },
  {
    provider: 'Google Public DNS',
    label: 'DNS-over-HTTPS -- https://dns.google/dns-query',
    value: 'https://dns.google/dns-query',
    name: 'Google Public DNS (DoH)',
  },
  {
    provider: 'Google Public DNS',
    label: 'Plain (IPv6) -- 2001:4860:4860::8888',
    value: '[2001:4860:4860::8888]',
    name: 'Google Public DNS (Plain, IPv6)',
  },
  {
    provider: 'Google Public DNS',
    label: 'Plain (IPv6) -- 2001:4860:4860::8844 (secondary)',
    value: '[2001:4860:4860::8844]',
    name: 'Google Public DNS (Plain, IPv6 secondary)',
  },

  // Quad9 -- filtered (malware-blocking) + DNSSEC-validating by default, documented at
  // https://www.quad9.net/service/service-addresses-and-features.
  {
    provider: 'Quad9',
    label: 'Plain -- 9.9.9.9',
    value: '9.9.9.9',
    name: 'Quad9 (Plain)',
  },
  {
    provider: 'Quad9',
    label: 'Plain -- 149.112.112.112 (secondary)',
    value: '149.112.112.112',
    name: 'Quad9 (Plain, secondary)',
  },
  {
    provider: 'Quad9',
    label: 'DNS-over-TLS -- tls://dns.quad9.net',
    value: 'tls://dns.quad9.net',
    name: 'Quad9 (DoT)',
  },
  {
    provider: 'Quad9',
    label: 'DNS-over-HTTPS -- https://dns.quad9.net/dns-query',
    value: 'https://dns.quad9.net/dns-query',
    name: 'Quad9 (DoH)',
  },
  {
    provider: 'Quad9',
    label: 'DNS-over-QUIC -- quic://dns.quad9.net',
    value: 'quic://dns.quad9.net',
    name: 'Quad9 (DoQ)',
  },
  {
    provider: 'Quad9',
    label: 'Plain (IPv6) -- 2620:fe::fe',
    value: '[2620:fe::fe]',
    name: 'Quad9 (Plain, IPv6)',
  },
  {
    provider: 'Quad9',
    label: 'Plain (IPv6) -- 2620:fe::9 (secondary)',
    value: '[2620:fe::9]',
    name: 'Quad9 (Plain, IPv6 secondary)',
  },

  // OpenDNS (Cisco) -- plain-only public documentation at https://use.opendns.com/.
  {
    provider: 'OpenDNS',
    label: 'Plain -- 208.67.222.222',
    value: '208.67.222.222',
    name: 'OpenDNS (Plain)',
  },
  {
    provider: 'OpenDNS',
    label: 'Plain -- 208.67.220.220 (secondary)',
    value: '208.67.220.220',
    name: 'OpenDNS (Plain, secondary)',
  },
  {
    provider: 'OpenDNS',
    label: 'Plain (IPv6) -- 2620:119:35::35',
    value: '[2620:119:35::35]',
    name: 'OpenDNS (Plain, IPv6)',
  },
  {
    provider: 'OpenDNS',
    label: 'Plain (IPv6) -- 2620:119:53::53 (secondary)',
    value: '[2620:119:53::53]',
    name: 'OpenDNS (Plain, IPv6 secondary)',
  },
];

/**
 * This function derives the `name` a bulk-create line gets when `ForwarderFormComponent.save()`
 * turns one non-blank textarea line into its own `POST /api/forwarders` call. A line whose
 * trimmed content exactly matches a {@link KnownForwarderOption.value} always takes that option's
 * own descriptive {@link KnownForwarderOption.name}, regardless of whether the operator reached
 * it by picking the dropdown or by typing the identical value by hand -- the two are
 * indistinguishable once the line sits in the textarea, and giving a recognized public resolver
 * its real name either way is strictly more useful than a generic fallback. An unrecognized
 * (manually typed) line falls back to `namePrefix` verbatim when it is the only line being
 * created (preserving this form's original single-create behavior exactly), or `"<namePrefix>
 * <lineNumber>"` when multiple lines are being created in the same bulk save, so no two
 * unrecognized lines in the same save collide on name.
 */
export function deriveForwarderName(
  line: string,
  lineNumber: number,
  totalLines: number,
  namePrefix: string,
): string {
  const known = KNOWN_FORWARDER_OPTIONS.find((option) => option.value === line);
  if (known) return known.name;

  const prefix = namePrefix.trim().length > 0 ? namePrefix.trim() : 'Forwarder';
  return totalLines > 1 ? `${prefix} ${lineNumber}` : prefix;
}
