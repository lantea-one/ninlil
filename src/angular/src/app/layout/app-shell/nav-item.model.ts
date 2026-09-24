/**
 * This interface describes one entry in the app shell's own side navigation.
 */
export interface NavItem {
  /** The label shown in the nav. */
  label: string;

  /** The route path (relative to the shell) this item navigates to. */
  path: string;

  /** The `@coreui/icons` icon name (registered in `app.config.ts`) shown alongside the label. */
  icon: string;

  /**
   * The permission claim (`resource:action`, matching the backend's own
   * `[RequirePermission("...")]` names) required to see and navigate to this item. `null` means
   * "any signed-in user," with no specific permission required.
   */
  permission: string | null;
}

/**
 * This constant contains every top-level nav destination this shell knows about, each backed by
 * its own real feature page rather than a `PlaceholderComponent` stand-in. Every permission string
 * below is a real, already-checked-against-the-backend value (see `RequirePermissionAttribute`
 * usages across `Ninlil.Web/Controllers`), not a guess -- so the permission-gating pattern this
 * array demonstrates is always wired to the exact claims each real page requires. Icon names are
 * `@coreui/icons` names, registered in `app.config.ts`.
 */
export const NAV_ITEMS: readonly NavItem[] = [
  { label: 'Dashboard', path: 'dashboard', icon: 'cilSpeedometer', permission: null },
  { label: 'Zones', path: 'zones', icon: 'cilLan', permission: 'zones:read' },
  {
    label: 'DNS Rewrites',
    path: 'dns-rewrites',
    icon: 'cilSwapHorizontal',
    permission: 'dnsrewrites:read',
  },
  { label: 'TSIG Keys', path: 'tsig-keys', icon: 'cilLockLocked', permission: 'tsigkeys:read' },
  { label: 'Cluster', path: 'cluster', icon: 'cilShareAll', permission: 'clusternodes:read' },
  {
    label: 'Cloud Credentials',
    path: 'cloud-provider-credentials',
    icon: 'cilCloud',
    permission: 'cloudprovidercredentials:read',
  },
  {
    label: 'Filter Lists',
    path: 'filter-lists',
    icon: 'cilBan',
    permission: 'filterlists:read',
  },
  {
    label: 'Clients',
    path: 'clients',
    icon: 'cilContact',
    permission: 'clients:read',
  },
  {
    label: 'Static Address Mappings',
    path: 'client-static-address-mappings',
    icon: 'cilAddressBook',
    permission: 'clients:read',
  },
  {
    label: 'Client Groups',
    path: 'client-groups',
    icon: 'cilGroup',
    permission: 'clientgroups:read',
  },
  {
    label: 'DHCP Services',
    path: 'dhcp-services',
    icon: 'cilCog',
    permission: 'dhcpservices:read',
  },
  {
    label: 'DHCP Scopes',
    path: 'dhcp-scopes',
    icon: 'cilDevices',
    permission: 'dhcpscopes:read',
  },
  {
    label: 'DHCP Tag Rules',
    path: 'dhcp-tag-rules',
    icon: 'cilTags',
    permission: 'dhcptagrules:read',
  },
  {
    label: 'DHCP Leases',
    path: 'dhcp-leases',
    icon: 'cilClock',
    permission: 'dhcpleases:read',
  },
  {
    label: 'Forwarders',
    path: 'forwarders',
    icon: 'cilShareAll',
    permission: 'forwarders:read',
  },
  {
    label: 'Access Control Rules',
    path: 'access-control-rules',
    icon: 'cilCheckCircle',
    permission: 'accesscontrol:read',
  },
  { label: 'Query Log', path: 'query-log', icon: 'cilHistory', permission: 'querylog:read' },
  { label: 'Webhooks', path: 'webhooks', icon: 'cilLink', permission: 'webhooks:read' },
  {
    label: 'Presets & Settings',
    path: 'settings',
    icon: 'cilSettings',
    permission: 'presets:read',
  },
  { label: 'Users', path: 'users', icon: 'cilUser', permission: 'users:read' },
  { label: 'Roles', path: 'roles', icon: 'cilShieldAlt', permission: 'roles:read' },
  { label: 'API Tokens', path: 'api-tokens', icon: 'cilLockUnlocked', permission: null },
  {
    label: 'Backup & Restore',
    path: 'backup',
    icon: 'cilCloudDownload',
    permission: 'backup:read',
  },
  { label: 'Services', path: 'services', icon: 'cilCog', permission: 'services:read' },
] as const;
