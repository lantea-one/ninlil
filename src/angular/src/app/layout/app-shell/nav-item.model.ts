import { SETTINGS_TAB_PERMISSIONS } from '../../features/settings/settings-shell/settings-nav';

/**
 * This interface describes one entry in the app shell's side navigation.
 */
export interface NavItem {
  /** The label shown in the nav. */
  label: string;

  /** The route path (relative to the shell) this item navigates to. */
  path: string;

  /** The `@coreui/icons` icon name (registered in `app.config.ts`) shown alongside the label. */
  icon: string;

  /**
   * The permission (`resource:action`, as in the backend's `[RequirePermission("...")]`) needed
   * to see this item. A list means any one of them will do; `null` means any signed-in user. The
   * route in `app.routes.ts` must have a guard with the same permission.
   */
  permission: string | readonly string[] | null;
}

/**
 * This constant lists the sidebar's nav items, in display order. Each permission is the read
 * permission of the controller behind the page. Icon names are `@coreui/icons` names and must be
 * registered in `app.config.ts`.
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
  { label: 'Cluster', path: 'cluster', icon: 'cilSitemap', permission: 'clusternodes:read' },
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
    icon: 'cilRouter',
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
    permission: SETTINGS_TAB_PERMISSIONS,
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
