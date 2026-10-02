/**
 * This interface describes one settings page link inside a {@link SettingsNavGroup}: its label,
 * route path and the read permission its route guard checks.
 */
export interface SettingsTab {
  label: string;
  path: string;
  permission: string;

  /**
   * The permission needed to change what the page shows, when it isn't simply
   * {@link permission} with `:read` swapped for `:write` (see {@link settingsTabWritePermission}).
   */
  writePermission?: string;
}

/**
 * This interface describes one labeled category in the settings navigation of
 * `SettingsShellComponent`: a heading and its pages in order.
 */
export interface SettingsNavGroup {
  label: string;
  tabs: readonly SettingsTab[];
}

/**
 * This constant lists every settings page, in eight categories, with the permission that gates
 * each. Each page uses the read permission of its backend controller (for example
 * `ratelimiting:read` for `RateLimitingSettingsController`, `storage:read` for
 * `StorageController`), not one shared settings permission. Blocked Services and Safe Search reuse
 * `filterlists:read` and `dnsrewrites:read`, because those pages only manage ordinary filter lists
 * and rewrites. `SettingsShellComponent` hides a tab whose permission the user lacks, and each
 * child route in `app.routes.ts` also has a `permissionGuard` for it.
 *
 * <p><b>Grouping.</b> Pages are grouped by the subsystem they configure:
 * - `Overview`: the DNS security presets, the landing page at `/settings`.
 * - `DNS Resolution & Protocol`: how a query is answered (recursive resolver, transports,
 *   forwarding, pipeline) and query-time tuning (rate limiting, outbound-query hardening, EDNS
 *   Client Subnet, DNS64, DNS Cookies).
 * - `Filtering & Privacy`: the filtering engine with Blocked Services and Safe Search, rewrites,
 *   query logging, client presence and MAC vendor lookup.
 * - `Zone Management`: DNSSEC policy and key rollover, PTR sync, zone signing, NOTIFY, zone
 *   transfers, secondary zone refresh and TSIG, which all serve zone operations.
 * - `Identity & Access`: local account policy, SSO and admin-login rate limiting.
 * - `Integrations & Delivery`: connections to outside systems (event broker, reverse proxy, ACME,
 *   SMTP, webhook delivery).
 * - `System`: storage engine, cluster, web listener, appearance, branding and self-update.
 * - `DHCP`: the DHCP server settings, a separate protocol family with one page.
 */
export const SETTINGS_NAV_GROUPS: readonly SettingsNavGroup[] = [
  {
    label: 'Overview',
    tabs: [{ label: 'DNS Security Presets', path: '/settings', permission: 'presets:read' }],
  },
  {
    label: 'DNS Resolution & Protocol',
    tabs: [
      {
        label: 'Recursive Resolver',
        path: '/settings/recursive',
        permission: 'recursivesettings:read',
      },
      { label: 'Transports', path: '/settings/transports', permission: 'transportsettings:read' },
      { label: 'Forwarding', path: '/settings/forwarding', permission: 'forwardingsettings:read' },
      { label: 'Pipeline', path: '/settings/pipeline', permission: 'dnspipelinesettings:read' },
      { label: 'Rate limiting', path: '/settings/rate-limiting', permission: 'ratelimiting:read' },
      {
        label: 'Outbound Query Hardening',
        path: '/settings/outbound-query',
        permission: 'outboundquerysettings:read',
      },
      {
        label: 'Client Subnet (ECS)',
        path: '/settings/client-subnet',
        permission: 'clientsubnetsettings:read',
      },
      { label: 'DNS64', path: '/settings/dns64', permission: 'dns64settings:read' },
      { label: 'DNS Cookies', path: '/settings/dns-cookies', permission: 'dnscookies:read' },
    ],
  },
  {
    label: 'Filtering & Privacy',
    tabs: [
      { label: 'Filtering', path: '/settings/filtering', permission: 'filteringsettings:read' },
      { label: 'DNS Rewrites', path: '/settings/rewrites', permission: 'rewritesettings:read' },
      {
        label: 'Blocked Services',
        path: '/settings/blocked-services',
        permission: 'filterlists:read',
      },
      { label: 'Safe Search', path: '/settings/safe-search', permission: 'dnsrewrites:read' },
      { label: 'Query logging', path: '/settings/query-logging', permission: 'querylog:read' },
      {
        label: 'Client Presence',
        path: '/settings/client-presence',
        permission: 'clientpresencesettings:read',
      },
      {
        label: 'MAC Vendors',
        path: '/settings/mac-vendors',
        permission: 'macvendorsettings:read',
      },
    ],
  },
  {
    label: 'Zone Management',
    tabs: [
      {
        label: 'DNSSEC Policy',
        path: '/settings/dnssec-policy',
        permission: 'dnssecpolicysettings:read',
      },
      {
        label: 'DNSSEC Key Rollover',
        path: '/settings/dnssec-key-rollover',
        permission: 'dnsseckeyrolloversettings:read',
      },
      { label: 'PTR Sync', path: '/settings/ptr-sync', permission: 'ptrsyncsettings:read' },
      {
        label: 'Zone Signing',
        path: '/settings/zone-signing',
        permission: 'zonesigningsettings:read',
      },
      { label: 'NOTIFY', path: '/settings/notify', permission: 'notifysettings:read' },
      {
        label: 'Zone Transfer',
        path: '/settings/zone-transfer',
        permission: 'zonetransfersettings:read',
      },
      {
        label: 'Zone Transfer Client',
        path: '/settings/zone-transfer-client',
        permission: 'zonetransferclientsettings:read',
      },
      {
        label: 'Secondary Zone Refresh',
        path: '/settings/secondary-zone-refresh',
        permission: 'secondaryzonerefreshsettings:read',
      },
      { label: 'TSIG', path: '/settings/tsig', permission: 'tsigsettings:read' },
    ],
  },
  {
    label: 'Identity & Access',
    tabs: [
      { label: 'Identity', path: '/settings/identity', permission: 'identitysettings:read' },
      { label: 'Single Sign-On', path: '/settings/sso', permission: 'ssosettings:read' },
      {
        label: 'Admin Login Rate Limit',
        path: '/settings/admin-login-rate-limit',
        permission: 'adminloginratelimit:read',
      },
    ],
  },
  {
    label: 'Integrations & Delivery',
    tabs: [
      { label: 'Event broker', path: '/settings/event-broker', permission: 'eventbroker:read' },
      { label: 'Reverse proxy', path: '/settings/reverse-proxy', permission: 'reverseproxy:read' },
      { label: 'ACME / Certificates', path: '/settings/acme', permission: 'acmesettings:read' },
      { label: 'SMTP', path: '/settings/smtp', permission: 'smtpsettings:read' },
      {
        label: 'Webhook Delivery',
        path: '/settings/webhook-delivery',
        permission: 'webhookdeliverysettings:read',
      },
    ],
  },
  {
    label: 'System',
    tabs: [
      {
        label: 'Storage',
        path: '/settings/storage',
        permission: 'storage:read',
        writePermission: 'storage:manage',
      },
      { label: 'Cluster', path: '/settings/cluster', permission: 'clustersettings:read' },
      { label: 'Web Listener', path: '/settings/web-listen', permission: 'weblistensettings:read' },
      { label: 'Appearance', path: '/settings/appearance', permission: 'appearance:read' },
      { label: 'Branding', path: '/settings/branding', permission: 'branding:read' },
      {
        label: 'Self-Update',
        path: '/settings/self-update',
        permission: 'selfupdatesettings:read',
      },
    ],
  },
  {
    label: 'DHCP',
    tabs: [{ label: 'DHCP', path: '/settings/dhcp', permission: 'dhcpsettings:read' }],
  },
];

/**
 * This constant contains every settings tab's read permission. Holding any one of them opens the
 * settings area: the parent `/settings` route guard and the "Presets & Settings" nav item use it.
 * Each tab's route guard still checks that tab's permission.
 */
export const SETTINGS_TAB_PERMISSIONS: readonly string[] = SETTINGS_NAV_GROUPS.flatMap((group) =>
  group.tabs.map((tab) => tab.permission),
);

/**
 * This function returns the permission a settings tab's Save (and other changing) controls need:
 * {@link SettingsTab.writePermission} when set, otherwise the read permission with `:read` swapped
 * for `:write`, which is how every settings controller except storage names it.
 */
export function settingsTabWritePermission(tab: SettingsTab): string {
  return tab.writePermission ?? tab.permission.replace(/:read$/, ':write');
}
