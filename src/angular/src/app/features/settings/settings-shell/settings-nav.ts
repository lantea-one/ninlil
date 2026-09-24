/** This interface describes one settings sub-page link inside a {@link SettingsNavGroup}. */
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
 * This interface describes one labeled category inside {@link SettingsShellComponent}'s own
 * grouped sub-navigation -- a short heading plus the ordered set of settings pages that belong
 * under it. See {@link SETTINGS_NAV_GROUPS}'s own doc comment for the grouping rationale.
 */
export interface SettingsNavGroup {
  label: string;
  tabs: readonly SettingsTab[];
}

/**
 * This constant contains every settings sub-page this shell knows about, organized into 8
 * labeled categories, alongside the specific permission claim gating each tab -- deliberately
 * *not* one shared claim such as `presets:read` (the parent `/settings` route and nav item accept
 * any of these, see {@link SETTINGS_TAB_PERMISSIONS}), since `RateLimitingSettingsController`/`EventBrokerSettingsController`/
 * `DnsCookieSettingsController`/`QueryLoggingSettingsController`/`ReverseProxySettingsController`/
 * `AppearanceSettingsController`/`StorageController`/`RecursiveSettingsController`/
 * `TransportSettingsController`/`FilteringSettingsController`/`ForwardingSettingsController`/
 * `RewriteSettingsController`/`AcmeSettingsController`/`SmtpSettingsController`/
 * `DnssecPolicySettingsController`/`DnssecKeyRolloverSettingsController`/`IdentitySettingsController`/
 * `WebListenSettingsController`/
 * `PtrSyncSettingsController`/`ZoneSigningSettingsController`/`NotifySettingsController`/
 * `ZoneTransferSettingsController`/`ZoneTransferClientSettingsController`/
 * `SecondaryZoneRefreshSettingsController`/`TsigSettingsController`/
 * `WebhookDeliverySettingsController`/`DnsPipelineSettingsController`/`BrandingSettingsController`/
 * `OutboundQuerySettingsController`/`ClientSubnetSettingsController`/`Dns64SettingsController`/
 * `AdminLoginRateLimitSettingsController`/`SelfUpdateSettingsController`/`ClusterSettingsController`/
 * `ClientPresenceSettingsController`/`MacVendorSettingsController`
 * each require their own distinct permission (`ratelimiting:read`/`outboundquerysettings:read`/
 * `clientsubnetsettings:read`/`dns64settings:read`/`selfupdatesettings:read`/
 * `clientpresencesettings:read`/`macvendorsettings:read`/`eventbroker:read`/
 * `dnscookies:read`/`querylog:read`/`reverseproxy:read`/`appearance:read`/`storage:read`/
 * `recursivesettings:read`/`transportsettings:read`/`filteringsettings:read`/
 * `forwardingsettings:read`/`rewritesettings:read`/`acmesettings:read`/`smtpsettings:read`/
 * `ssosettings:read`/`dnssecpolicysettings:read`/`dnsseckeyrolloversettings:read`/
 * `identitysettings:read`/`weblistensettings:read`/
 * `ptrsyncsettings:read`/`zonesigningsettings:read`/`notifysettings:read`/
 * `zonetransfersettings:read`/`zonetransferclientsettings:read`/
 * `secondaryzonerefreshsettings:read`/`tsigsettings:read`/`webhookdeliverysettings:read`/
 * `dnspipelinesettings:read`/`branding:read`/`adminloginratelimit:read`/`clustersettings:read`/
 * `dhcpsettings:read`), not `presets:read` -- confirmed directly against each controller's own `[RequirePermission(...)]`
 * attribute, not assumed from the shared "Presets & Settings" umbrella label. `BlockedServicesController`/
 * `SafeSearchController` are the one exception: their own tabs below deliberately reuse
 * `filterlists:read`/`dnsrewrites:read` (the same permissions `FilteringSettingsController`/
 * `RewriteSettingsController` themselves gate on) rather than a distinct new claim, since both are
 * thin sugar over the ordinary filter-list/rewrite entities those permissions already cover -- see
 * each controller's own doc comment. A tab whose own permission a session lacks is hidden here
 * (mirroring `AppShellComponent`'s own `visibleNavItems`), on top of (not instead of) each child
 * route's own `permissionGuard` actually blocking direct navigation.
 *
 * <p><b>Grouping rationale</b> (the fix for a real, live-audited information-architecture problem:
 * this shell used to render all 37 pages as one flat, wrapping row of pill tabs, four full lines
 * deep at desktop width with no categorization at all). The first 7 categories below were
 * derived by reading every one of the original 37 pages' own doc comments to determine what
 * each genuinely configures, then grouping by the real subsystem each page's `Options` class
 * belongs to -- not by superficial name similarity. `Overview` stands alone since
 * it is this shell's own default-route landing page, not a configuration form. `DNS Resolution &
 * Protocol` groups everything that shapes how a query is actually answered end-to-end (the
 * recursive resolver itself, transports, forwarding, the query-processing pipeline, and every
 * query-time hardening/tuning knob -- rate limiting, outbound-query hardening, EDNS Client Subnet,
 * DNS64, DNS Cookies). `Filtering & Privacy` groups the block/allow-list engine and its two curated
 * sugar layers (Blocked Services, Safe Search), the rewrite engine, and the query log's own privacy
 * controls. `Zone Management` groups every zone-transfer/signing/notification-adjacent page,
 * including TSIG (clock-skew tolerance for signed transfers) and DNSSEC Policy (algorithm
 * restrictions for newly-generated zone-signing keys) since both exist in service of zone
 * operations specifically, not as standalone security features. `Identity & Access` groups local
 * account policy, SSO federation, and admin-login brute-force protection -- every page governing
 * who can sign in and how. `Integrations & Delivery` groups every page configuring an outbound
 * connection to something external to the DNS engine itself: the message-broker event transport,
 * the HTTP reverse-proxy trust list, ACME/Let's Encrypt issuance, outbound SMTP, and webhook
 * delivery. `System` groups platform-level, DNS-protocol-agnostic concerns: the storage-engine
 * hot-switch, clustering, the admin API's own web listener, UI appearance/branding defaults, and
 * the self-update checker. `DHCP` is its own eighth, later-added category rather than folded into
 * `DNS Resolution & Protocol` -- DHCP is a whole separate protocol family sharing this same admin
 * process, not a DNS-query-answering concern, and today holds a single settings page
 * (`DhcpSettingsController`'s three sub-resources), so it doesn't yet need the multi-tab breakdown
 * every other category has; a future DHCPv6 settings page belongs here too.
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
 * This constant contains every settings tab's read permission. Holding any one of them is enough
 * to open the settings area -- the parent `/settings` route guard and nav item both use it -- and
 * each tab's own route guard still checks that tab's own permission.
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
