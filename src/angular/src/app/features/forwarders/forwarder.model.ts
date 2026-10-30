/**
 * One public DNS resolver endpoint offered in the forwarder form's resolver dropdown: one
 * transport (plain, DoT or DoH) of one provider.
 *
 * Each {@link value} is an address or URL for this use, written in the
 * definition syntax `UpstreamForwarderDefinitionParser` accepts: a bare address for plain DNS,
 * `tls://host[:port]` for DNS-over-TLS, a full `https://` URL for DNS-over-HTTPS and
 * `quic://host[:port]` for DNS-over-QUIC. The dropdown offers no DNS-over-QUIC entry (see
 * {@link KNOWN_FORWARDER_OPTIONS}), but the parser accepts the form.
 */
export interface KnownForwarderOption {
  /** The provider, used as the dropdown's `<optgroup>` label. */
  readonly provider: string;
  /** The text shown for this option inside its provider's `<optgroup>`. */
  readonly label: string;
  /** The literal forwarder definition string appended to the textarea when this option is picked. */
  readonly value: string;
  /** The forwarder name used when a bulk-create line equals this option's {@link value}. */
  readonly name: string;
}

/**
 * This constant lists the resolvers the forwarder form's dropdown offers: four public providers,
 * not an exhaustive list. Every plain, DoT and DoH entry is an endpoint the provider documents at
 * the page named above its group. No DoQ entry is offered. Cloudflare, Quad9 and Google Public DNS
 * document no QUIC endpoint. A DNS-over-QUIC query (ALPN `doq`, port 853) to Cloudflare's 1.1.1.1
 * and `2606:4700:4700::1111` got no answer. Quad9's host name answers it over IPv6 only. Ninlil
 * connects to the IPv4 address of a host name that has one (`ForwarderEndpointCache`), and a
 * handshake with Quad9's IPv4 addresses aborted a probe process inside libmsquic 2.4.11.
 *
 * Each provider's IPv6 addresses are listed with its IPv4 ones. An IPv6 literal is always
 * bracketed (`[addr]` or `[addr]:port`), because the parser splits host and port at the last `:`
 * and would otherwise read part of the address as a port. Cloudflare's encrypted entries use IP
 * literals, so they get IPv6 variants too; the Google and Quad9 encrypted entries use host names,
 * which resolve over either address family.
 */
export const KNOWN_FORWARDER_OPTIONS: readonly KnownForwarderOption[] = [
  // Cloudflare (1.1.1.1): plain, DoT and DoH are documented at
  // https://developers.cloudflare.com/1.1.1.1/.
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

  // Google Public DNS, documented at https://developers.google.com/speed/public-dns.
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

  // Quad9, which blocks malware and validates DNSSEC by default. Plain, DoT and DoH are documented at
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

  // OpenDNS (Cisco), plain DNS only, documented at https://use.opendns.com/.
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
 * This function returns the name for one bulk-create line. A line equal to a
 * {@link KnownForwarderOption.value} gets that option's name, whether it was picked or typed.
 * Any other line gets the trimmed `namePrefix` (or "Forwarder" when blank) when it is the only
 * line, or `"<prefix> <lineNumber>"` when several lines are saved, so they do not clash with each
 * other.
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

/**
 * This function gives every line in `lines` that has no number yet in `assigned` the next unused
 * number, and leaves the numbers already there alone. A bulk create form calls it on each save
 * with one map that lives as long as the page, so a line that failed and is sent again keeps the
 * number (and so the name) it had on the first attempt, instead of taking its new position in the
 * shorter list of failed lines and clashing with a forwarder an earlier line created. A new or edited
 * line gets a number above every number handed out so far. It returns the number of each line,
 * in the order of `lines`.
 */
export function assignBulkLineNumbers(lines: string[], assigned: Map<string, number>): number[] {
  let next = Math.max(0, ...assigned.values()) + 1;
  return lines.map((line) => {
    if (!assigned.has(line)) assigned.set(line, next++);
    return assigned.get(line) as number;
  });
}
