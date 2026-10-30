/**
 * One public DNS resolver endpoint offered in the forwarder form's resolver dropdown: one
 * transport (plain, DoT, DoH or DoQ) of one provider, and for a provider with several filtering
 * variants, of one variant.
 *
 * Each {@link value} is an address or URL for this use, written in the
 * definition syntax `UpstreamForwarderDefinitionParser` accepts: a bare address for plain DNS,
 * `tls://host[:port]` for DNS-over-TLS, a full `https://` URL for DNS-over-HTTPS and
 * `quic://host[:port]` for DNS-over-QUIC. {@link KNOWN_FORWARDER_OPTIONS} lists DoQ endpoints only
 * for providers that document one and answered a DoQ query.
 */
export interface KnownForwarderOption {
  /** The provider, used as the dropdown's `<optgroup>` label. */
  readonly provider: string;
  /**
   * The text shown for this option inside its provider's `<optgroup>`: the transport, a dash pair,
   * the address, and in brackets the provider's variant name where the provider has variants.
   */
  readonly label: string;
  /** The literal forwarder definition string appended to the textarea when this option is picked. */
  readonly value: string;
  /**
   * The forwarder name used when a bulk-create line equals this option's {@link value}. It is
   * unique across the list and stays stable, because saved forwarders carry it.
   */
  readonly name: string;
}

/**
 * This constant lists the resolvers the forwarder form's dropdown offers: nine public providers
 * (Cloudflare, Google Public DNS, Quad9, OpenDNS, Mullvad DNS, DNS4EU, AdGuard DNS, Control D and
 * CleanBrowsing), not an exhaustive list. Each provider is one `<optgroup>`. Every entry is an
 * endpoint the provider documents at the page named above its group. A provider with filtering variants (such as an unfiltered, a family or a
 * malware-blocking one) has one entry per transport and variant, and the label ends with the
 * provider's own variant name.
 *
 * DoQ entries exist only for AdGuard DNS and Control D, which document a QUIC endpoint and answered
 * it over IPv4 and IPv6. Cloudflare, Quad9 and Google Public DNS document no QUIC endpoint. A
 * DNS-over-QUIC query (ALPN `doq`, port 853) to Cloudflare's 1.1.1.1 and `2606:4700:4700::1111` got
 * no answer. Quad9's host name answers it over IPv6 only. Ninlil connects to the IPv4 address of a
 * host name that has one (`ForwarderEndpointCache`), and a handshake with Quad9's IPv4 addresses
 * aborts a process that runs libmsquic 2.4.11 (the Deb, RPM and Arch packages ask for 2.4.20 or later).
 *
 * Each provider's IPv6 addresses are listed with its IPv4 ones. An IPv6 literal is always
 * bracketed (`[addr]` or `[addr]:port`), because the parser splits host and port at the last `:`
 * and would otherwise read part of the address as a port. Cloudflare's encrypted entries use IP
 * literals, so they get IPv6 variants too; the encrypted entries of every other provider use host
 * names, which resolve over either address family the provider serves.
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

  // Mullvad DNS: DoT and DoH by host name, one host per content-blocking variant (the page lists no
  // plain-DNS or QUIC endpoint for them), documented at
  // https://mullvad.net/en/help/dns-over-https-and-dns-over-tls. The host names resolve over IPv4
  // and IPv6 (194.242.2.x and 2a07:e340::x). The page says its IP addresses work only with DoH or
  // DoT, so the entries use the host names. Variants: the page's hostname table.
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-TLS -- tls://dns.mullvad.net (Default)',
    value: 'tls://dns.mullvad.net',
    name: 'Mullvad DNS (Default, DoT)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-HTTPS -- https://dns.mullvad.net/dns-query (Default)',
    value: 'https://dns.mullvad.net/dns-query',
    name: 'Mullvad DNS (Default, DoH)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-TLS -- tls://adblock.dns.mullvad.net (Adblock)',
    value: 'tls://adblock.dns.mullvad.net',
    name: 'Mullvad DNS (Adblock, DoT)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-HTTPS -- https://adblock.dns.mullvad.net/dns-query (Adblock)',
    value: 'https://adblock.dns.mullvad.net/dns-query',
    name: 'Mullvad DNS (Adblock, DoH)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-TLS -- tls://base.dns.mullvad.net (Base)',
    value: 'tls://base.dns.mullvad.net',
    name: 'Mullvad DNS (Base, DoT)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-HTTPS -- https://base.dns.mullvad.net/dns-query (Base)',
    value: 'https://base.dns.mullvad.net/dns-query',
    name: 'Mullvad DNS (Base, DoH)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-TLS -- tls://extended.dns.mullvad.net (Extended)',
    value: 'tls://extended.dns.mullvad.net',
    name: 'Mullvad DNS (Extended, DoT)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-HTTPS -- https://extended.dns.mullvad.net/dns-query (Extended)',
    value: 'https://extended.dns.mullvad.net/dns-query',
    name: 'Mullvad DNS (Extended, DoH)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-TLS -- tls://family.dns.mullvad.net (Family)',
    value: 'tls://family.dns.mullvad.net',
    name: 'Mullvad DNS (Family, DoT)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-HTTPS -- https://family.dns.mullvad.net/dns-query (Family)',
    value: 'https://family.dns.mullvad.net/dns-query',
    name: 'Mullvad DNS (Family, DoH)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-TLS -- tls://all.dns.mullvad.net (All)',
    value: 'tls://all.dns.mullvad.net',
    name: 'Mullvad DNS (All, DoT)',
  },
  {
    provider: 'Mullvad DNS',
    label: 'DNS-over-HTTPS -- https://all.dns.mullvad.net/dns-query (All)',
    value: 'https://all.dns.mullvad.net/dns-query',
    name: 'Mullvad DNS (All, DoH)',
  },

  // DNS4EU, the European public resolver: plain (two IPv4 and two IPv6 addresses), DoT and DoH for
  // each of its five variants, documented at https://www.joindns4.eu/for-public. The page lists no
  // QUIC endpoint. The variant names are the page's own.
  {
    provider: 'DNS4EU',
    label: 'Plain -- 86.54.11.1 (Protective)',
    value: '86.54.11.1',
    name: 'DNS4EU (Protective, Plain)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain -- 86.54.11.201 (Protective, secondary)',
    value: '86.54.11.201',
    name: 'DNS4EU (Protective, Plain, secondary)',
  },
  {
    provider: 'DNS4EU',
    label: 'DNS-over-TLS -- tls://protective.joindns4.eu (Protective)',
    value: 'tls://protective.joindns4.eu',
    name: 'DNS4EU (Protective, DoT)',
  },
  {
    provider: 'DNS4EU',
    label: 'DNS-over-HTTPS -- https://protective.joindns4.eu/dns-query (Protective)',
    value: 'https://protective.joindns4.eu/dns-query',
    name: 'DNS4EU (Protective, DoH)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain (IPv6) -- 2a13:1001::86:54:11:1 (Protective)',
    value: '[2a13:1001::86:54:11:1]',
    name: 'DNS4EU (Protective, Plain, IPv6)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain (IPv6) -- 2a13:1001::86:54:11:201 (Protective, secondary)',
    value: '[2a13:1001::86:54:11:201]',
    name: 'DNS4EU (Protective, Plain, IPv6 secondary)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain -- 86.54.11.12 (Protective + Child Protection)',
    value: '86.54.11.12',
    name: 'DNS4EU (Protective + Child Protection, Plain)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain -- 86.54.11.212 (Protective + Child Protection, secondary)',
    value: '86.54.11.212',
    name: 'DNS4EU (Protective + Child Protection, Plain, secondary)',
  },
  {
    provider: 'DNS4EU',
    label: 'DNS-over-TLS -- tls://child.joindns4.eu (Protective + Child Protection)',
    value: 'tls://child.joindns4.eu',
    name: 'DNS4EU (Protective + Child Protection, DoT)',
  },
  {
    provider: 'DNS4EU',
    label: 'DNS-over-HTTPS -- https://child.joindns4.eu/dns-query (Protective + Child Protection)',
    value: 'https://child.joindns4.eu/dns-query',
    name: 'DNS4EU (Protective + Child Protection, DoH)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain (IPv6) -- 2a13:1001::86:54:11:12 (Protective + Child Protection)',
    value: '[2a13:1001::86:54:11:12]',
    name: 'DNS4EU (Protective + Child Protection, Plain, IPv6)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain (IPv6) -- 2a13:1001::86:54:11:212 (Protective + Child Protection, secondary)',
    value: '[2a13:1001::86:54:11:212]',
    name: 'DNS4EU (Protective + Child Protection, Plain, IPv6 secondary)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain -- 86.54.11.13 (Protective + Ad Blocking)',
    value: '86.54.11.13',
    name: 'DNS4EU (Protective + Ad Blocking, Plain)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain -- 86.54.11.213 (Protective + Ad Blocking, secondary)',
    value: '86.54.11.213',
    name: 'DNS4EU (Protective + Ad Blocking, Plain, secondary)',
  },
  {
    provider: 'DNS4EU',
    label: 'DNS-over-TLS -- tls://noads.joindns4.eu (Protective + Ad Blocking)',
    value: 'tls://noads.joindns4.eu',
    name: 'DNS4EU (Protective + Ad Blocking, DoT)',
  },
  {
    provider: 'DNS4EU',
    label: 'DNS-over-HTTPS -- https://noads.joindns4.eu/dns-query (Protective + Ad Blocking)',
    value: 'https://noads.joindns4.eu/dns-query',
    name: 'DNS4EU (Protective + Ad Blocking, DoH)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain (IPv6) -- 2a13:1001::86:54:11:13 (Protective + Ad Blocking)',
    value: '[2a13:1001::86:54:11:13]',
    name: 'DNS4EU (Protective + Ad Blocking, Plain, IPv6)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain (IPv6) -- 2a13:1001::86:54:11:213 (Protective + Ad Blocking, secondary)',
    value: '[2a13:1001::86:54:11:213]',
    name: 'DNS4EU (Protective + Ad Blocking, Plain, IPv6 secondary)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain -- 86.54.11.11 (Protective + Child Protection + Ad Blocking)',
    value: '86.54.11.11',
    name: 'DNS4EU (Protective + Child Protection + Ad Blocking, Plain)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain -- 86.54.11.211 (Protective + Child Protection + Ad Blocking, secondary)',
    value: '86.54.11.211',
    name: 'DNS4EU (Protective + Child Protection + Ad Blocking, Plain, secondary)',
  },
  {
    provider: 'DNS4EU',
    label:
      'DNS-over-TLS -- tls://child-noads.joindns4.eu (Protective + Child Protection + Ad Blocking)',
    value: 'tls://child-noads.joindns4.eu',
    name: 'DNS4EU (Protective + Child Protection + Ad Blocking, DoT)',
  },
  {
    provider: 'DNS4EU',
    label:
      'DNS-over-HTTPS -- https://child-noads.joindns4.eu/dns-query (Protective + Child Protection + Ad Blocking)',
    value: 'https://child-noads.joindns4.eu/dns-query',
    name: 'DNS4EU (Protective + Child Protection + Ad Blocking, DoH)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain (IPv6) -- 2a13:1001::86:54:11:11 (Protective + Child Protection + Ad Blocking)',
    value: '[2a13:1001::86:54:11:11]',
    name: 'DNS4EU (Protective + Child Protection + Ad Blocking, Plain, IPv6)',
  },
  {
    provider: 'DNS4EU',
    label:
      'Plain (IPv6) -- 2a13:1001::86:54:11:211 (Protective + Child Protection + Ad Blocking, secondary)',
    value: '[2a13:1001::86:54:11:211]',
    name: 'DNS4EU (Protective + Child Protection + Ad Blocking, Plain, IPv6 secondary)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain -- 86.54.11.100 (Unfiltered)',
    value: '86.54.11.100',
    name: 'DNS4EU (Unfiltered, Plain)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain -- 86.54.11.200 (Unfiltered, secondary)',
    value: '86.54.11.200',
    name: 'DNS4EU (Unfiltered, Plain, secondary)',
  },
  {
    provider: 'DNS4EU',
    label: 'DNS-over-TLS -- tls://unfiltered.joindns4.eu (Unfiltered)',
    value: 'tls://unfiltered.joindns4.eu',
    name: 'DNS4EU (Unfiltered, DoT)',
  },
  {
    provider: 'DNS4EU',
    label: 'DNS-over-HTTPS -- https://unfiltered.joindns4.eu/dns-query (Unfiltered)',
    value: 'https://unfiltered.joindns4.eu/dns-query',
    name: 'DNS4EU (Unfiltered, DoH)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain (IPv6) -- 2a13:1001::86:54:11:100 (Unfiltered)',
    value: '[2a13:1001::86:54:11:100]',
    name: 'DNS4EU (Unfiltered, Plain, IPv6)',
  },
  {
    provider: 'DNS4EU',
    label: 'Plain (IPv6) -- 2a13:1001::86:54:11:200 (Unfiltered, secondary)',
    value: '[2a13:1001::86:54:11:200]',
    name: 'DNS4EU (Unfiltered, Plain, IPv6 secondary)',
  },

  // AdGuard DNS: plain, DoT, DoH and DoQ for each of its three servers, documented at
  // https://adguard-dns.io/en/public-dns.html. The page's DNSCrypt stamps are not offered here.
  {
    provider: 'AdGuard DNS',
    label: 'Plain -- 94.140.14.14 (Default)',
    value: '94.140.14.14',
    name: 'AdGuard DNS (Default, Plain)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain -- 94.140.15.15 (Default, secondary)',
    value: '94.140.15.15',
    name: 'AdGuard DNS (Default, Plain, secondary)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'DNS-over-TLS -- tls://dns.adguard-dns.com (Default)',
    value: 'tls://dns.adguard-dns.com',
    name: 'AdGuard DNS (Default, DoT)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'DNS-over-HTTPS -- https://dns.adguard-dns.com/dns-query (Default)',
    value: 'https://dns.adguard-dns.com/dns-query',
    name: 'AdGuard DNS (Default, DoH)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'DNS-over-QUIC -- quic://dns.adguard-dns.com (Default)',
    value: 'quic://dns.adguard-dns.com',
    name: 'AdGuard DNS (Default, DoQ)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain (IPv6) -- 2a10:50c0::ad1:ff (Default)',
    value: '[2a10:50c0::ad1:ff]',
    name: 'AdGuard DNS (Default, Plain, IPv6)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain (IPv6) -- 2a10:50c0::ad2:ff (Default, secondary)',
    value: '[2a10:50c0::ad2:ff]',
    name: 'AdGuard DNS (Default, Plain, IPv6 secondary)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain -- 94.140.14.140 (Non-filtering)',
    value: '94.140.14.140',
    name: 'AdGuard DNS (Non-filtering, Plain)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain -- 94.140.14.141 (Non-filtering, secondary)',
    value: '94.140.14.141',
    name: 'AdGuard DNS (Non-filtering, Plain, secondary)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'DNS-over-TLS -- tls://unfiltered.adguard-dns.com (Non-filtering)',
    value: 'tls://unfiltered.adguard-dns.com',
    name: 'AdGuard DNS (Non-filtering, DoT)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'DNS-over-HTTPS -- https://unfiltered.adguard-dns.com/dns-query (Non-filtering)',
    value: 'https://unfiltered.adguard-dns.com/dns-query',
    name: 'AdGuard DNS (Non-filtering, DoH)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'DNS-over-QUIC -- quic://unfiltered.adguard-dns.com (Non-filtering)',
    value: 'quic://unfiltered.adguard-dns.com',
    name: 'AdGuard DNS (Non-filtering, DoQ)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain (IPv6) -- 2a10:50c0::1:ff (Non-filtering)',
    value: '[2a10:50c0::1:ff]',
    name: 'AdGuard DNS (Non-filtering, Plain, IPv6)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain (IPv6) -- 2a10:50c0::2:ff (Non-filtering, secondary)',
    value: '[2a10:50c0::2:ff]',
    name: 'AdGuard DNS (Non-filtering, Plain, IPv6 secondary)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain -- 94.140.14.15 (Family protection)',
    value: '94.140.14.15',
    name: 'AdGuard DNS (Family protection, Plain)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain -- 94.140.15.16 (Family protection, secondary)',
    value: '94.140.15.16',
    name: 'AdGuard DNS (Family protection, Plain, secondary)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'DNS-over-TLS -- tls://family.adguard-dns.com (Family protection)',
    value: 'tls://family.adguard-dns.com',
    name: 'AdGuard DNS (Family protection, DoT)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'DNS-over-HTTPS -- https://family.adguard-dns.com/dns-query (Family protection)',
    value: 'https://family.adguard-dns.com/dns-query',
    name: 'AdGuard DNS (Family protection, DoH)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'DNS-over-QUIC -- quic://family.adguard-dns.com (Family protection)',
    value: 'quic://family.adguard-dns.com',
    name: 'AdGuard DNS (Family protection, DoQ)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain (IPv6) -- 2a10:50c0::bad1:ff (Family protection)',
    value: '[2a10:50c0::bad1:ff]',
    name: 'AdGuard DNS (Family protection, Plain, IPv6)',
  },
  {
    provider: 'AdGuard DNS',
    label: 'Plain (IPv6) -- 2a10:50c0::bad2:ff (Family protection, secondary)',
    value: '[2a10:50c0::bad2:ff]',
    name: 'AdGuard DNS (Family protection, Plain, IPv6 secondary)',
  },

  // Control D, free DNS with its native filters: plain, DoT, DoH and DoQ for each of its six
  // variants, documented at https://docs.controld.com/docs/free-dns. The page also lists endpoints that
  // apply third-party block lists (names starting with x-); the dropdown leaves them out.
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.2.0 (Unfiltered)',
    value: '76.76.2.0',
    name: 'Control D (Unfiltered, Plain)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.10.0 (Unfiltered, secondary)',
    value: '76.76.10.0',
    name: 'Control D (Unfiltered, Plain, secondary)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-TLS -- tls://p0.freedns.controld.com (Unfiltered)',
    value: 'tls://p0.freedns.controld.com',
    name: 'Control D (Unfiltered, DoT)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-HTTPS -- https://freedns.controld.com/p0 (Unfiltered)',
    value: 'https://freedns.controld.com/p0',
    name: 'Control D (Unfiltered, DoH)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-QUIC -- quic://p0.freedns.controld.com (Unfiltered)',
    value: 'quic://p0.freedns.controld.com',
    name: 'Control D (Unfiltered, DoQ)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40::0 (Unfiltered)',
    value: '[2606:1a40::0]',
    name: 'Control D (Unfiltered, Plain, IPv6)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40:1::0 (Unfiltered, secondary)',
    value: '[2606:1a40:1::0]',
    name: 'Control D (Unfiltered, Plain, IPv6 secondary)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.2.1 (Malware)',
    value: '76.76.2.1',
    name: 'Control D (Malware, Plain)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.10.1 (Malware, secondary)',
    value: '76.76.10.1',
    name: 'Control D (Malware, Plain, secondary)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-TLS -- tls://p1.freedns.controld.com (Malware)',
    value: 'tls://p1.freedns.controld.com',
    name: 'Control D (Malware, DoT)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-HTTPS -- https://freedns.controld.com/p1 (Malware)',
    value: 'https://freedns.controld.com/p1',
    name: 'Control D (Malware, DoH)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-QUIC -- quic://p1.freedns.controld.com (Malware)',
    value: 'quic://p1.freedns.controld.com',
    name: 'Control D (Malware, DoQ)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40::1 (Malware)',
    value: '[2606:1a40::1]',
    name: 'Control D (Malware, Plain, IPv6)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40:1::1 (Malware, secondary)',
    value: '[2606:1a40:1::1]',
    name: 'Control D (Malware, Plain, IPv6 secondary)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.2.2 (Ads & Tracking)',
    value: '76.76.2.2',
    name: 'Control D (Ads & Tracking, Plain)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.10.2 (Ads & Tracking, secondary)',
    value: '76.76.10.2',
    name: 'Control D (Ads & Tracking, Plain, secondary)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-TLS -- tls://p2.freedns.controld.com (Ads & Tracking)',
    value: 'tls://p2.freedns.controld.com',
    name: 'Control D (Ads & Tracking, DoT)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-HTTPS -- https://freedns.controld.com/p2 (Ads & Tracking)',
    value: 'https://freedns.controld.com/p2',
    name: 'Control D (Ads & Tracking, DoH)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-QUIC -- quic://p2.freedns.controld.com (Ads & Tracking)',
    value: 'quic://p2.freedns.controld.com',
    name: 'Control D (Ads & Tracking, DoQ)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40::2 (Ads & Tracking)',
    value: '[2606:1a40::2]',
    name: 'Control D (Ads & Tracking, Plain, IPv6)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40:1::2 (Ads & Tracking, secondary)',
    value: '[2606:1a40:1::2]',
    name: 'Control D (Ads & Tracking, Plain, IPv6 secondary)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.2.3 (Social)',
    value: '76.76.2.3',
    name: 'Control D (Social, Plain)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.10.3 (Social, secondary)',
    value: '76.76.10.3',
    name: 'Control D (Social, Plain, secondary)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-TLS -- tls://p3.freedns.controld.com (Social)',
    value: 'tls://p3.freedns.controld.com',
    name: 'Control D (Social, DoT)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-HTTPS -- https://freedns.controld.com/p3 (Social)',
    value: 'https://freedns.controld.com/p3',
    name: 'Control D (Social, DoH)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-QUIC -- quic://p3.freedns.controld.com (Social)',
    value: 'quic://p3.freedns.controld.com',
    name: 'Control D (Social, DoQ)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40::3 (Social)',
    value: '[2606:1a40::3]',
    name: 'Control D (Social, Plain, IPv6)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40:1::3 (Social, secondary)',
    value: '[2606:1a40:1::3]',
    name: 'Control D (Social, Plain, IPv6 secondary)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.2.4 (Family Friendly)',
    value: '76.76.2.4',
    name: 'Control D (Family Friendly, Plain)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.10.4 (Family Friendly, secondary)',
    value: '76.76.10.4',
    name: 'Control D (Family Friendly, Plain, secondary)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-TLS -- tls://family.freedns.controld.com (Family Friendly)',
    value: 'tls://family.freedns.controld.com',
    name: 'Control D (Family Friendly, DoT)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-HTTPS -- https://freedns.controld.com/family (Family Friendly)',
    value: 'https://freedns.controld.com/family',
    name: 'Control D (Family Friendly, DoH)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-QUIC -- quic://family.freedns.controld.com (Family Friendly)',
    value: 'quic://family.freedns.controld.com',
    name: 'Control D (Family Friendly, DoQ)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40::4 (Family Friendly)',
    value: '[2606:1a40::4]',
    name: 'Control D (Family Friendly, Plain, IPv6)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40:1::4 (Family Friendly, secondary)',
    value: '[2606:1a40:1::4]',
    name: 'Control D (Family Friendly, Plain, IPv6 secondary)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.2.5 (Uncensored)',
    value: '76.76.2.5',
    name: 'Control D (Uncensored, Plain)',
  },
  {
    provider: 'Control D',
    label: 'Plain -- 76.76.10.5 (Uncensored, secondary)',
    value: '76.76.10.5',
    name: 'Control D (Uncensored, Plain, secondary)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-TLS -- tls://uncensored.freedns.controld.com (Uncensored)',
    value: 'tls://uncensored.freedns.controld.com',
    name: 'Control D (Uncensored, DoT)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-HTTPS -- https://freedns.controld.com/uncensored (Uncensored)',
    value: 'https://freedns.controld.com/uncensored',
    name: 'Control D (Uncensored, DoH)',
  },
  {
    provider: 'Control D',
    label: 'DNS-over-QUIC -- quic://uncensored.freedns.controld.com (Uncensored)',
    value: 'quic://uncensored.freedns.controld.com',
    name: 'Control D (Uncensored, DoQ)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40::5 (Uncensored)',
    value: '[2606:1a40::5]',
    name: 'Control D (Uncensored, Plain, IPv6)',
  },
  {
    provider: 'Control D',
    label: 'Plain (IPv6) -- 2606:1a40:1::5 (Uncensored, secondary)',
    value: '[2606:1a40:1::5]',
    name: 'Control D (Uncensored, Plain, IPv6 secondary)',
  },

  // CleanBrowsing, free filters: plain, DoT and DoH for each of its three filters, documented at
  // https://cleanbrowsing.org/filters. The page lists no QUIC endpoint. The DoT and DoH host names
  // resolve over IPv4 only.
  {
    provider: 'CleanBrowsing',
    label: 'Plain -- 185.228.168.168 (Family Filter)',
    value: '185.228.168.168',
    name: 'CleanBrowsing (Family Filter, Plain)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain -- 185.228.169.168 (Family Filter, secondary)',
    value: '185.228.169.168',
    name: 'CleanBrowsing (Family Filter, Plain, secondary)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'DNS-over-TLS -- tls://family-filter-dns.cleanbrowsing.org (Family Filter)',
    value: 'tls://family-filter-dns.cleanbrowsing.org',
    name: 'CleanBrowsing (Family Filter, DoT)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'DNS-over-HTTPS -- https://doh.cleanbrowsing.org/doh/family-filter/ (Family Filter)',
    value: 'https://doh.cleanbrowsing.org/doh/family-filter/',
    name: 'CleanBrowsing (Family Filter, DoH)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain (IPv6) -- 2a0d:2a00:1:: (Family Filter)',
    value: '[2a0d:2a00:1::]',
    name: 'CleanBrowsing (Family Filter, Plain, IPv6)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain (IPv6) -- 2a0d:2a00:2:: (Family Filter, secondary)',
    value: '[2a0d:2a00:2::]',
    name: 'CleanBrowsing (Family Filter, Plain, IPv6 secondary)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain -- 185.228.168.10 (Adult Filter)',
    value: '185.228.168.10',
    name: 'CleanBrowsing (Adult Filter, Plain)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain -- 185.228.169.11 (Adult Filter, secondary)',
    value: '185.228.169.11',
    name: 'CleanBrowsing (Adult Filter, Plain, secondary)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'DNS-over-TLS -- tls://adult-filter-dns.cleanbrowsing.org (Adult Filter)',
    value: 'tls://adult-filter-dns.cleanbrowsing.org',
    name: 'CleanBrowsing (Adult Filter, DoT)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'DNS-over-HTTPS -- https://doh.cleanbrowsing.org/doh/adult-filter/ (Adult Filter)',
    value: 'https://doh.cleanbrowsing.org/doh/adult-filter/',
    name: 'CleanBrowsing (Adult Filter, DoH)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain (IPv6) -- 2a0d:2a00:1::1 (Adult Filter)',
    value: '[2a0d:2a00:1::1]',
    name: 'CleanBrowsing (Adult Filter, Plain, IPv6)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain (IPv6) -- 2a0d:2a00:2::1 (Adult Filter, secondary)',
    value: '[2a0d:2a00:2::1]',
    name: 'CleanBrowsing (Adult Filter, Plain, IPv6 secondary)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain -- 185.228.168.9 (Security Filter)',
    value: '185.228.168.9',
    name: 'CleanBrowsing (Security Filter, Plain)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain -- 185.228.169.9 (Security Filter, secondary)',
    value: '185.228.169.9',
    name: 'CleanBrowsing (Security Filter, Plain, secondary)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'DNS-over-TLS -- tls://security-filter-dns.cleanbrowsing.org (Security Filter)',
    value: 'tls://security-filter-dns.cleanbrowsing.org',
    name: 'CleanBrowsing (Security Filter, DoT)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'DNS-over-HTTPS -- https://doh.cleanbrowsing.org/doh/security-filter/ (Security Filter)',
    value: 'https://doh.cleanbrowsing.org/doh/security-filter/',
    name: 'CleanBrowsing (Security Filter, DoH)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain (IPv6) -- 2a0d:2a00:1::2 (Security Filter)',
    value: '[2a0d:2a00:1::2]',
    name: 'CleanBrowsing (Security Filter, Plain, IPv6)',
  },
  {
    provider: 'CleanBrowsing',
    label: 'Plain (IPv6) -- 2a0d:2a00:2::2 (Security Filter, secondary)',
    value: '[2a0d:2a00:2::2]',
    name: 'CleanBrowsing (Security Filter, Plain, IPv6 secondary)',
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
