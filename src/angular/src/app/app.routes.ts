import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { anyPermissionGuard, permissionGuard } from './core/guards/permission.guard';
import { SETTINGS_TAB_PERMISSIONS } from './features/settings/settings-shell/settings-nav';
import { settingsLandingGuard } from './features/settings/settings-shell/settings-landing.guard';
import { AppShellComponent } from './layout/app-shell/app-shell.component';
import { ForbiddenComponent } from './layout/forbidden/forbidden.component';

/**
 * This constant is the app's route table. The public pages (login, forgot and reset password,
 * SSO callback) sit at the top level. Every other page renders inside {@link AppShellComponent},
 * whose route has {@link authGuard}, so they all need a session.
 *
 * <p>Most areas also have a {@link permissionGuard} for the backend's read permission on the
 * parent route and for the write permission on each create and edit route. The guard blocks a
 * typed-in URL or bookmark as well as a nav click; the nav hides the same items. Each permission
 * matches the `[RequirePermission]` on the controller the page calls and the entry in
 * {@link NAV_ITEMS}. Actions on a page that need more (delete, start/stop, restore) are hidden by
 * the page itself.
 *
 * <p>Every page is lazy-loaded with `loadComponent`, so it downloads as a separate chunk on first
 * visit. Only {@link AppShellComponent} and the small {@link ForbiddenComponent} are eager. Add
 * new pages the same way: an eager `component:` import pulls the page and its imports into the
 * initial bundle.
 */
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login/login.component').then((m) => m.LoginComponent),
  },
  {
    // The public page for requesting a password reset e-mail, linked from the login page. Like
    // the other public pages it has no guard: the visitor has no session.
    path: 'forgot-password',
    loadComponent: () =>
      import('./features/auth/forgot-password/forgot-password.component').then(
        (m) => m.ForgotPasswordComponent,
      ),
  },
  {
    // The public "set new password" page that reset e-mails link to, both those an administrator
    // sends and those from `forgot-password`. `userId` and `token` arrive as query parameters
    // in the mailed link.
    path: 'reset-password',
    loadComponent: () =>
      import('./features/auth/reset-password/set-new-password.component').then(
        (m) => m.SetNewPasswordComponent,
      ),
  },
  {
    // Where a completed OpenID Connect sign-in redirects, with the access token in the URL
    // fragment. No guard: the visitor has no session until this page applies the token.
    path: 'sso-callback',
    loadComponent: () =>
      import('./features/auth/sso-callback/sso-callback.component').then(
        (m) => m.SsoCallbackComponent,
      ),
  },
  {
    path: '',
    component: AppShellComponent,
    canActivate: [authGuard],
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },
      { path: 'forbidden', component: ForbiddenComponent },
      {
        path: 'dashboard',
        loadComponent: () =>
          import('./features/dashboard/dashboard.component').then((m) => m.DashboardComponent),
      },
      {
        // Zones, their records and their DNSSEC keys, as one flat list of child routes. Every
        // route needs `zones:read`; create and edit routes also need `zones:write`, as in
        // ZonesController, RecordsController and DnssecKeysController.
        path: 'zones',
        canActivate: [permissionGuard('zones:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/zones/zone-list/zone-list.component').then(
                (m) => m.ZoneListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/zones/zone-form/zone-form.component').then(
                (m) => m.ZoneFormComponent,
              ),
            canActivate: [permissionGuard('zones:write')],
          },
          {
            path: ':zoneId/edit',
            loadComponent: () =>
              import('./features/zones/zone-form/zone-form.component').then(
                (m) => m.ZoneFormComponent,
              ),
            canActivate: [permissionGuard('zones:write')],
          },
          {
            path: ':zoneId/records',
            loadComponent: () =>
              import('./features/zones/records/record-list/record-list.component').then(
                (m) => m.RecordListComponent,
              ),
          },
          {
            path: ':zoneId/records/new',
            loadComponent: () =>
              import('./features/zones/records/record-form/record-form.component').then(
                (m) => m.RecordFormComponent,
              ),
            canActivate: [permissionGuard('zones:write')],
          },
          {
            path: ':zoneId/records/:recordId/edit',
            loadComponent: () =>
              import('./features/zones/records/record-form/record-form.component').then(
                (m) => m.RecordFormComponent,
              ),
            canActivate: [permissionGuard('zones:write')],
          },
          // DNSSEC keys belong to one zone, so they live here and use the zone permissions.
          {
            path: ':zoneId/dnssec-keys',
            loadComponent: () =>
              import('./features/zones/dnssec-keys/dnssec-key-list/dnssec-key-list.component').then(
                (m) => m.DnssecKeyListComponent,
              ),
          },
          {
            path: ':zoneId/dnssec-keys/new',
            loadComponent: () =>
              import('./features/zones/dnssec-keys/dnssec-key-form/dnssec-key-form.component').then(
                (m) => m.DnssecKeyFormComponent,
              ),
            canActivate: [permissionGuard('zones:write')],
          },
          {
            path: ':zoneId/dnssec-keys/:keyId/edit',
            loadComponent: () =>
              import('./features/zones/dnssec-keys/dnssec-key-form/dnssec-key-form.component').then(
                (m) => m.DnssecKeyFormComponent,
              ),
            canActivate: [permissionGuard('zones:write')],
          },
        ],
      },
      {
        // DNS rewrites (`/api/dns-rewrites`), which belong to no zone.
        path: 'dns-rewrites',
        canActivate: [permissionGuard('dnsrewrites:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/dns-rewrites/dns-rewrite-list/dns-rewrite-list.component').then(
                (m) => m.DnsRewriteListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/dns-rewrites/dns-rewrite-form/dns-rewrite-form.component').then(
                (m) => m.DnsRewriteFormComponent,
              ),
            canActivate: [permissionGuard('dnsrewrites:write')],
          },
          {
            path: ':id/edit',
            loadComponent: () =>
              import('./features/dns-rewrites/dns-rewrite-form/dns-rewrite-form.component').then(
                (m) => m.DnsRewriteFormComponent,
              ),
            canActivate: [permissionGuard('dnsrewrites:write')],
          },
        ],
      },
      {
        // TSIG keys (`/api/tsig-keys`), shared by all zones.
        path: 'tsig-keys',
        canActivate: [permissionGuard('tsigkeys:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/tsig-keys/tsig-key-list/tsig-key-list.component').then(
                (m) => m.TsigKeyListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/tsig-keys/tsig-key-form/tsig-key-form.component').then(
                (m) => m.TsigKeyFormComponent,
              ),
            canActivate: [permissionGuard('tsigkeys:write')],
          },
          {
            path: ':id/edit',
            loadComponent: () =>
              import('./features/tsig-keys/tsig-key-form/tsig-key-form.component').then(
                (m) => m.TsigKeyFormComponent,
              ),
            canActivate: [permissionGuard('tsigkeys:write')],
          },
        ],
      },
      {
        // Cluster peers (`/api/cluster/nodes`). Clustering itself is switched on under
        // Settings > Cluster (ClusterSettingsComponent), which must be done before joining or
        // adding a peer here succeeds.
        path: 'cluster',
        loadComponent: () =>
          import('./features/cluster/cluster.component').then((m) => m.ClusterComponent),
        canActivate: [permissionGuard('clusternodes:read')],
      },
      {
        // Cloud provider credentials (`/api/cloud-provider-credentials`).
        path: 'cloud-provider-credentials',
        canActivate: [permissionGuard('cloudprovidercredentials:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/cloud-provider-credentials/cloud-provider-credential-list/cloud-provider-credential-list.component').then(
                (m) => m.CloudProviderCredentialListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/cloud-provider-credentials/cloud-provider-credential-form/cloud-provider-credential-form.component').then(
                (m) => m.CloudProviderCredentialFormComponent,
              ),
            canActivate: [permissionGuard('cloudprovidercredentials:write')],
          },
          {
            path: ':id/edit',
            loadComponent: () =>
              import('./features/cloud-provider-credentials/cloud-provider-credential-form/cloud-provider-credential-form.component').then(
                (m) => m.CloudProviderCredentialFormComponent,
              ),
            canActivate: [permissionGuard('cloudprovidercredentials:write')],
          },
        ],
      },
      {
        // Filter lists with their entries nested underneath, shaped like zones and records.
        path: 'filter-lists',
        canActivate: [permissionGuard('filterlists:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/filter-lists/filter-list-list/filter-list-list.component').then(
                (m) => m.FilterListListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/filter-lists/filter-list-form/filter-list-form.component').then(
                (m) => m.FilterListFormComponent,
              ),
            canActivate: [permissionGuard('filterlists:write')],
          },
          {
            path: ':listId/edit',
            loadComponent: () =>
              import('./features/filter-lists/filter-list-form/filter-list-form.component').then(
                (m) => m.FilterListFormComponent,
              ),
            canActivate: [permissionGuard('filterlists:write')],
          },
          {
            path: ':listId/entries',
            loadComponent: () =>
              import('./features/filter-lists/entries/filter-list-entry-list/filter-list-entry-list.component').then(
                (m) => m.FilterListEntryListComponent,
              ),
          },
          {
            path: ':listId/entries/new',
            loadComponent: () =>
              import('./features/filter-lists/entries/filter-list-entry-form/filter-list-entry-form.component').then(
                (m) => m.FilterListEntryFormComponent,
              ),
            canActivate: [permissionGuard('filterlists:write')],
          },
          {
            path: ':listId/entries/:entryId/edit',
            loadComponent: () =>
              import('./features/filter-lists/entries/filter-list-entry-form/filter-list-entry-form.component').then(
                (m) => m.FilterListEntryFormComponent,
              ),
            canActivate: [permissionGuard('filterlists:write')],
          },
        ],
      },
      {
        // Known clients, with their identifiers (addresses), filter-list assignments, schedules
        // and activity as separate nested pages, like client groups below. Safe Search and
        // instant pause are panels on the edit form, since neither is a list.
        path: 'clients',
        canActivate: [permissionGuard('clients:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/clients/client-list/client-list.component').then(
                (m) => m.ClientListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/clients/client-form/client-form.component').then(
                (m) => m.ClientFormComponent,
              ),
            canActivate: [permissionGuard('clients:write')],
          },
          {
            path: ':clientId/edit',
            loadComponent: () =>
              import('./features/clients/client-form/client-form.component').then(
                (m) => m.ClientFormComponent,
              ),
            canActivate: [permissionGuard('clients:write')],
          },
          {
            // Add and remove only, on one page: an identifier cannot be edited, so there is no
            // edit route. The page hides its write controls without clients:write.
            path: ':clientId/addresses',
            loadComponent: () =>
              import('./features/clients/addresses/client-address-list/client-address-list.component').then(
                (m) => m.ClientAddressListComponent,
              ),
          },
          {
            path: ':clientId/list-assignments',
            loadComponent: () =>
              import('./features/clients/list-assignments/client-list-assignment-list/client-list-assignment-list.component').then(
                (m) => m.ClientListAssignmentListComponent,
              ),
          },
          {
            // No edit route: ClientListAssignmentsController has no PUT or PATCH. Changing the
            // list means deleting one assignment and creating another.
            path: ':clientId/list-assignments/new',
            loadComponent: () =>
              import('./features/clients/list-assignments/client-list-assignment-form/client-list-assignment-form.component').then(
                (m) => m.ClientListAssignmentFormComponent,
              ),
            canActivate: [permissionGuard('clients:write')],
          },
          {
            path: ':clientId/schedules',
            loadComponent: () =>
              import('./features/clients/schedules/client-schedule-list/client-schedule-list.component').then(
                (m) => m.ClientScheduleListComponent,
              ),
          },
          {
            path: ':clientId/schedules/new',
            loadComponent: () =>
              import('./features/clients/schedules/client-schedule-form/client-schedule-form.component').then(
                (m) => m.ClientScheduleFormComponent,
              ),
            canActivate: [permissionGuard('clients:write')],
          },
          {
            path: ':clientId/schedules/:scheduleId/edit',
            loadComponent: () =>
              import('./features/clients/schedules/client-schedule-form/client-schedule-form.component').then(
                (m) => m.ClientScheduleFormComponent,
              ),
            canActivate: [permissionGuard('clients:write')],
          },
          // Read-only. It is the client's slice of the query log, so it needs `querylog:read`
          // (as ClientActivityController does) on top of the parent's `clients:read`.
          {
            path: ':clientId/activity',
            loadComponent: () =>
              import('./features/clients/activity/client-activity-list/client-activity-list.component').then(
                (m) => m.ClientActivityListComponent,
              ),
            canActivate: [permissionGuard('querylog:read')],
          },
        ],
      },
      {
        // Manual address-to-client mappings (`/api/client-static-address-mappings`). They use
        // the client permissions, as the controller does.
        path: 'client-static-address-mappings',
        canActivate: [permissionGuard('clients:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/client-static-address-mappings/static-address-mapping-list/static-address-mapping-list.component').then(
                (m) => m.StaticAddressMappingListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/client-static-address-mappings/static-address-mapping-form/static-address-mapping-form.component').then(
                (m) => m.StaticAddressMappingFormComponent,
              ),
            canActivate: [permissionGuard('clients:write')],
          },
          {
            path: ':id/edit',
            loadComponent: () =>
              import('./features/client-static-address-mappings/static-address-mapping-form/static-address-mapping-form.component').then(
                (m) => m.StaticAddressMappingFormComponent,
              ),
            canActivate: [permissionGuard('clients:write')],
          },
        ],
      },
      {
        // Client groups, with their networks, filter-list assignments and schedules as separate
        // nested pages, one per controller.
        path: 'client-groups',
        canActivate: [permissionGuard('clientgroups:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/client-groups/client-group-list/client-group-list.component').then(
                (m) => m.ClientGroupListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/client-groups/client-group-form/client-group-form.component').then(
                (m) => m.ClientGroupFormComponent,
              ),
            canActivate: [permissionGuard('clientgroups:write')],
          },
          {
            path: ':groupId/edit',
            loadComponent: () =>
              import('./features/client-groups/client-group-form/client-group-form.component').then(
                (m) => m.ClientGroupFormComponent,
              ),
            canActivate: [permissionGuard('clientgroups:write')],
          },
          {
            path: ':groupId/networks',
            loadComponent: () =>
              import('./features/client-groups/networks/client-group-network-list/client-group-network-list.component').then(
                (m) => m.ClientGroupNetworkListComponent,
              ),
          },
          {
            path: ':groupId/networks/new',
            loadComponent: () =>
              import('./features/client-groups/networks/client-group-network-form/client-group-network-form.component').then(
                (m) => m.ClientGroupNetworkFormComponent,
              ),
            canActivate: [permissionGuard('clientgroups:write')],
          },
          {
            path: ':groupId/networks/:networkId/edit',
            loadComponent: () =>
              import('./features/client-groups/networks/client-group-network-form/client-group-network-form.component').then(
                (m) => m.ClientGroupNetworkFormComponent,
              ),
            canActivate: [permissionGuard('clientgroups:write')],
          },
          {
            path: ':groupId/list-assignments',
            loadComponent: () =>
              import('./features/client-groups/list-assignments/client-group-list-assignment-list/client-group-list-assignment-list.component').then(
                (m) => m.ClientGroupListAssignmentListComponent,
              ),
          },
          {
            // No edit route: ClientGroupListAssignmentsController has no PUT or PATCH.
            path: ':groupId/list-assignments/new',
            loadComponent: () =>
              import('./features/client-groups/list-assignments/client-group-list-assignment-form/client-group-list-assignment-form.component').then(
                (m) => m.ClientGroupListAssignmentFormComponent,
              ),
            canActivate: [permissionGuard('clientgroups:write')],
          },
          {
            path: ':groupId/schedules',
            loadComponent: () =>
              import('./features/client-groups/schedules/client-group-schedule-list/client-group-schedule-list.component').then(
                (m) => m.ClientGroupScheduleListComponent,
              ),
          },
          {
            path: ':groupId/schedules/new',
            loadComponent: () =>
              import('./features/client-groups/schedules/client-group-schedule-form/client-group-schedule-form.component').then(
                (m) => m.ClientGroupScheduleFormComponent,
              ),
            canActivate: [permissionGuard('clientgroups:write')],
          },
          {
            path: ':groupId/schedules/:scheduleId/edit',
            loadComponent: () =>
              import('./features/client-groups/schedules/client-group-schedule-form/client-group-schedule-form.component').then(
                (m) => m.ClientGroupScheduleFormComponent,
              ),
            canActivate: [permissionGuard('clientgroups:write')],
          },
        ],
      },
      {
        // Start and stop the DHCP services (`/api/dhcp-services`). A fixed set, so there is no
        // create or edit route. It is separate from the `services` page below; see
        // DhcpServiceListComponent. The route needs `dhcpservices:read`; the page hides start
        // and stop without `dhcpservices:manage`.
        path: 'dhcp-services',
        loadComponent: () =>
          import('./features/dhcp/services/dhcp-service-list/dhcp-service-list.component').then(
            (m) => m.DhcpServiceListComponent,
          ),
        canActivate: [permissionGuard('dhcpservices:read')],
      },
      {
        // DHCP scopes with their reservations nested underneath. A scope's custom options are a
        // table on the scope form (DhcpScopeFormComponent), not a separate route.
        path: 'dhcp-scopes',
        canActivate: [permissionGuard('dhcpscopes:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/dhcp/scopes/dhcp-scope-list/dhcp-scope-list.component').then(
                (m) => m.DhcpScopeListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/dhcp/scopes/dhcp-scope-form/dhcp-scope-form.component').then(
                (m) => m.DhcpScopeFormComponent,
              ),
            canActivate: [permissionGuard('dhcpscopes:write')],
          },
          {
            path: ':scopeId/edit',
            loadComponent: () =>
              import('./features/dhcp/scopes/dhcp-scope-form/dhcp-scope-form.component').then(
                (m) => m.DhcpScopeFormComponent,
              ),
            canActivate: [permissionGuard('dhcpscopes:write')],
          },
          {
            path: ':scopeId/reservations',
            loadComponent: () =>
              import('./features/dhcp/scopes/reservations/dhcp-reservation-list/dhcp-reservation-list.component').then(
                (m) => m.DhcpReservationListComponent,
              ),
          },
          {
            path: ':scopeId/reservations/new',
            loadComponent: () =>
              import('./features/dhcp/scopes/reservations/dhcp-reservation-form/dhcp-reservation-form.component').then(
                (m) => m.DhcpReservationFormComponent,
              ),
            canActivate: [permissionGuard('dhcpscopes:write')],
          },
          {
            path: ':scopeId/reservations/:reservationId/edit',
            loadComponent: () =>
              import('./features/dhcp/scopes/reservations/dhcp-reservation-form/dhcp-reservation-form.component').then(
                (m) => m.DhcpReservationFormComponent,
              ),
            canActivate: [permissionGuard('dhcpscopes:write')],
          },
        ],
      },
      {
        // DHCP tag rules, top level because a rule's scope is optional. A rule's options are a
        // table on the rule form (DhcpTagRuleFormComponent), not a separate route.
        path: 'dhcp-tag-rules',
        canActivate: [permissionGuard('dhcptagrules:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/dhcp/tag-rules/dhcp-tag-rule-list/dhcp-tag-rule-list.component').then(
                (m) => m.DhcpTagRuleListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/dhcp/tag-rules/dhcp-tag-rule-form/dhcp-tag-rule-form.component').then(
                (m) => m.DhcpTagRuleFormComponent,
              ),
            canActivate: [permissionGuard('dhcptagrules:write')],
          },
          {
            path: ':tagRuleId/edit',
            loadComponent: () =>
              import('./features/dhcp/tag-rules/dhcp-tag-rule-form/dhcp-tag-rule-form.component').then(
                (m) => m.DhcpTagRuleFormComponent,
              ),
            canActivate: [permissionGuard('dhcptagrules:write')],
          },
        ],
      },
      {
        // DHCP leases. The DHCP server creates them, so there is no create or edit route. The
        // page hides the release action without `dhcpleases:manage`.
        path: 'dhcp-leases',
        loadComponent: () =>
          import('./features/dhcp/leases/dhcp-lease-list/dhcp-lease-list.component').then(
            (m) => m.DhcpLeaseListComponent,
          ),
        canActivate: [permissionGuard('dhcpleases:read')],
      },
      {
        // The server-wide upstream forwarders (`/api/forwarders`). Per-zone forwarder addresses
        // are set on the zone form instead.
        path: 'forwarders',
        canActivate: [permissionGuard('forwarders:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/forwarders/forwarder-list/forwarder-list.component').then(
                (m) => m.ForwarderListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/forwarders/forwarder-form/forwarder-form.component').then(
                (m) => m.ForwarderFormComponent,
              ),
            canActivate: [permissionGuard('forwarders:write')],
          },
          {
            path: ':id/edit',
            loadComponent: () =>
              import('./features/forwarders/forwarder-form/forwarder-form.component').then(
                (m) => m.ForwarderFormComponent,
              ),
            canActivate: [permissionGuard('forwarders:write')],
          },
        ],
      },
      {
        // DNS access control rules (`/api/access-control-rules`), one per network.
        path: 'access-control-rules',
        canActivate: [permissionGuard('accesscontrol:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/access-control-rules/access-control-rule-list/access-control-rule-list.component').then(
                (m) => m.AccessControlRuleListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/access-control-rules/access-control-rule-form/access-control-rule-form.component').then(
                (m) => m.AccessControlRuleFormComponent,
              ),
            canActivate: [permissionGuard('accesscontrol:write')],
          },
          {
            path: ':id/edit',
            loadComponent: () =>
              import('./features/access-control-rules/access-control-rule-form/access-control-rule-form.component').then(
                (m) => m.AccessControlRuleFormComponent,
              ),
            canActivate: [permissionGuard('accesscontrol:write')],
          },
        ],
      },
      {
        // The query log (`/api/query-log`). The server writes it, so there is no create or edit
        // route. The page hides "Clear log" without `querylog:write`.
        path: 'query-log',
        canActivate: [permissionGuard('querylog:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/query-log/query-log-list/query-log-list.component').then(
                (m) => m.QueryLogListComponent,
              ),
          },
        ],
      },
      {
        // Outbound webhook subscriptions (`/api/webhooks`) and their delivery history.
        path: 'webhooks',
        canActivate: [permissionGuard('webhooks:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/webhooks/webhook-list/webhook-list.component').then(
                (m) => m.WebhookListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/webhooks/webhook-form/webhook-form.component').then(
                (m) => m.WebhookFormComponent,
              ),
            canActivate: [permissionGuard('webhooks:write')],
          },
          {
            path: ':id/edit',
            loadComponent: () =>
              import('./features/webhooks/webhook-form/webhook-form.component').then(
                (m) => m.WebhookFormComponent,
              ),
            canActivate: [permissionGuard('webhooks:write')],
          },
          // Read-only delivery attempts for one subscription
          // (`GET /api/webhooks/{id}/delivery-attempts`), so only the parent's webhooks:read.
          {
            path: ':id/deliveries',
            loadComponent: () =>
              import('./features/webhooks/webhook-delivery-attempts/webhook-delivery-attempts.component').then(
                (m) => m.WebhookDeliveryAttemptsComponent,
              ),
          },
        ],
      },
      {
        // The settings area: SettingsShellComponent with one child page per settings controller.
        // `settings-nav.ts` lists the pages and their permissions. Each child route checks the
        // read permission of its controller; the shell opens for anyone holding any one of them.
        // The presets page at the root sends a user without `presets:read` on to a page they can
        // read (settingsLandingGuard).
        path: 'settings',
        loadComponent: () =>
          import('./features/settings/settings-shell/settings-shell.component').then(
            (m) => m.SettingsShellComponent,
          ),
        canActivate: [anyPermissionGuard(SETTINGS_TAB_PERMISSIONS)],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/settings/preset-list/preset-list.component').then(
                (m) => m.PresetListComponent,
              ),
            canActivate: [settingsLandingGuard],
          },
          {
            path: 'recursive',
            loadComponent: () =>
              import('./features/settings/recursive-settings/recursive-settings.component').then(
                (m) => m.RecursiveSettingsComponent,
              ),
            canActivate: [permissionGuard('recursivesettings:read')],
          },
          {
            path: 'transports',
            loadComponent: () =>
              import('./features/settings/transport-settings/transport-settings.component').then(
                (m) => m.TransportSettingsComponent,
              ),
            canActivate: [permissionGuard('transportsettings:read')],
          },
          {
            path: 'filtering',
            loadComponent: () =>
              import('./features/settings/filtering-settings/filtering-settings.component').then(
                (m) => m.FilteringSettingsComponent,
              ),
            canActivate: [permissionGuard('filteringsettings:read')],
          },
          {
            path: 'forwarding',
            loadComponent: () =>
              import('./features/settings/forwarding-settings/forwarding-settings.component').then(
                (m) => m.ForwardingSettingsComponent,
              ),
            canActivate: [permissionGuard('forwardingsettings:read')],
          },
          {
            path: 'rewrites',
            loadComponent: () =>
              import('./features/settings/rewrite-settings/rewrite-settings.component').then(
                (m) => m.RewriteSettingsComponent,
              ),
            canActivate: [permissionGuard('rewritesettings:read')],
          },
          {
            path: 'rate-limiting',
            loadComponent: () =>
              import('./features/settings/rate-limiting-settings/rate-limiting-settings.component').then(
                (m) => m.RateLimitingSettingsComponent,
              ),
            canActivate: [permissionGuard('ratelimiting:read')],
          },
          {
            path: 'outbound-query',
            loadComponent: () =>
              import('./features/settings/outbound-query-settings/outbound-query-settings.component').then(
                (m) => m.OutboundQuerySettingsComponent,
              ),
            canActivate: [permissionGuard('outboundquerysettings:read')],
          },
          {
            path: 'client-subnet',
            loadComponent: () =>
              import('./features/settings/client-subnet-settings/client-subnet-settings.component').then(
                (m) => m.ClientSubnetSettingsComponent,
              ),
            canActivate: [permissionGuard('clientsubnetsettings:read')],
          },
          {
            path: 'dns64',
            loadComponent: () =>
              import('./features/settings/dns64-settings/dns64-settings.component').then(
                (m) => m.Dns64SettingsComponent,
              ),
            canActivate: [permissionGuard('dns64settings:read')],
          },
          {
            path: 'cluster',
            loadComponent: () =>
              import('./features/settings/cluster-settings/cluster-settings.component').then(
                (m) => m.ClusterSettingsComponent,
              ),
            canActivate: [permissionGuard('clustersettings:read')],
          },
          {
            path: 'blocked-services',
            loadComponent: () =>
              import('./features/settings/blocked-services/blocked-services.component').then(
                (m) => m.BlockedServicesComponent,
              ),
            canActivate: [permissionGuard('filterlists:read')],
          },
          {
            path: 'safe-search',
            loadComponent: () =>
              import('./features/settings/safe-search/safe-search.component').then(
                (m) => m.SafeSearchComponent,
              ),
            canActivate: [permissionGuard('dnsrewrites:read')],
          },
          {
            path: 'event-broker',
            loadComponent: () =>
              import('./features/settings/event-broker-settings/event-broker-settings.component').then(
                (m) => m.EventBrokerSettingsComponent,
              ),
            canActivate: [permissionGuard('eventbroker:read')],
          },
          {
            path: 'dns-cookies',
            loadComponent: () =>
              import('./features/settings/dns-cookie-settings/dns-cookie-settings.component').then(
                (m) => m.DnsCookieSettingsComponent,
              ),
            canActivate: [permissionGuard('dnscookies:read')],
          },
          {
            path: 'query-logging',
            loadComponent: () =>
              import('./features/settings/query-logging-settings/query-logging-settings.component').then(
                (m) => m.QueryLoggingSettingsComponent,
              ),
            canActivate: [permissionGuard('querylog:read')],
          },
          {
            path: 'reverse-proxy',
            loadComponent: () =>
              import('./features/settings/reverse-proxy-settings/reverse-proxy-settings.component').then(
                (m) => m.ReverseProxySettingsComponent,
              ),
            canActivate: [permissionGuard('reverseproxy:read')],
          },
          {
            path: 'appearance',
            loadComponent: () =>
              import('./features/settings/appearance-settings/appearance-settings.component').then(
                (m) => m.AppearanceSettingsComponent,
              ),
            canActivate: [permissionGuard('appearance:read')],
          },
          {
            path: 'storage',
            loadComponent: () =>
              import('./features/settings/storage-settings/storage-settings.component').then(
                (m) => m.StorageSettingsComponent,
              ),
            canActivate: [permissionGuard('storage:read')],
          },
          {
            path: 'acme',
            loadComponent: () =>
              import('./features/settings/acme-settings/acme-settings.component').then(
                (m) => m.AcmeSettingsComponent,
              ),
            canActivate: [permissionGuard('acmesettings:read')],
          },
          {
            path: 'smtp',
            loadComponent: () =>
              import('./features/settings/smtp-settings/smtp-settings.component').then(
                (m) => m.SmtpSettingsComponent,
              ),
            canActivate: [permissionGuard('smtpsettings:read')],
          },
          {
            path: 'sso',
            loadComponent: () =>
              import('./features/settings/sso-settings/sso-settings.component').then(
                (m) => m.SsoSettingsComponent,
              ),
            canActivate: [permissionGuard('ssosettings:read')],
          },
          {
            path: 'dnssec-policy',
            loadComponent: () =>
              import('./features/settings/dnssec-policy-settings/dnssec-policy-settings.component').then(
                (m) => m.DnssecPolicySettingsComponent,
              ),
            canActivate: [permissionGuard('dnssecpolicysettings:read')],
          },
          {
            path: 'dnssec-key-rollover',
            loadComponent: () =>
              import('./features/settings/dnssec-key-rollover-settings/dnssec-key-rollover-settings.component').then(
                (m) => m.DnssecKeyRolloverSettingsComponent,
              ),
            canActivate: [permissionGuard('dnsseckeyrolloversettings:read')],
          },
          {
            path: 'identity',
            loadComponent: () =>
              import('./features/settings/identity-settings/identity-settings.component').then(
                (m) => m.IdentitySettingsComponent,
              ),
            canActivate: [permissionGuard('identitysettings:read')],
          },
          {
            path: 'web-listen',
            loadComponent: () =>
              import('./features/settings/web-listen-settings/web-listen-settings.component').then(
                (m) => m.WebListenSettingsComponent,
              ),
            canActivate: [permissionGuard('weblistensettings:read')],
          },
          {
            path: 'ptr-sync',
            loadComponent: () =>
              import('./features/settings/ptr-sync-settings/ptr-sync-settings.component').then(
                (m) => m.PtrSyncSettingsComponent,
              ),
            canActivate: [permissionGuard('ptrsyncsettings:read')],
          },
          {
            path: 'zone-signing',
            loadComponent: () =>
              import('./features/settings/zone-signing-settings/zone-signing-settings.component').then(
                (m) => m.ZoneSigningSettingsComponent,
              ),
            canActivate: [permissionGuard('zonesigningsettings:read')],
          },
          {
            path: 'notify',
            loadComponent: () =>
              import('./features/settings/notify-settings/notify-settings.component').then(
                (m) => m.NotifySettingsComponent,
              ),
            canActivate: [permissionGuard('notifysettings:read')],
          },
          {
            path: 'zone-transfer',
            loadComponent: () =>
              import('./features/settings/zone-transfer-settings/zone-transfer-settings.component').then(
                (m) => m.ZoneTransferSettingsComponent,
              ),
            canActivate: [permissionGuard('zonetransfersettings:read')],
          },
          {
            path: 'zone-transfer-client',
            loadComponent: () =>
              import('./features/settings/zone-transfer-client-settings/zone-transfer-client-settings.component').then(
                (m) => m.ZoneTransferClientSettingsComponent,
              ),
            canActivate: [permissionGuard('zonetransferclientsettings:read')],
          },
          {
            path: 'secondary-zone-refresh',
            loadComponent: () =>
              import('./features/settings/secondary-zone-refresh-settings/secondary-zone-refresh-settings.component').then(
                (m) => m.SecondaryZoneRefreshSettingsComponent,
              ),
            canActivate: [permissionGuard('secondaryzonerefreshsettings:read')],
          },
          {
            path: 'tsig',
            loadComponent: () =>
              import('./features/settings/tsig-settings/tsig-settings.component').then(
                (m) => m.TsigSettingsComponent,
              ),
            canActivate: [permissionGuard('tsigsettings:read')],
          },
          {
            path: 'webhook-delivery',
            loadComponent: () =>
              import('./features/settings/webhook-delivery-settings/webhook-delivery-settings.component').then(
                (m) => m.WebhookDeliverySettingsComponent,
              ),
            canActivate: [permissionGuard('webhookdeliverysettings:read')],
          },
          {
            path: 'pipeline',
            loadComponent: () =>
              import('./features/settings/dns-pipeline-settings/dns-pipeline-settings.component').then(
                (m) => m.DnsPipelineSettingsComponent,
              ),
            canActivate: [permissionGuard('dnspipelinesettings:read')],
          },
          {
            path: 'branding',
            loadComponent: () =>
              import('./features/settings/branding-settings/branding-settings.component').then(
                (m) => m.BrandingSettingsComponent,
              ),
            canActivate: [permissionGuard('branding:read')],
          },
          {
            path: 'admin-login-rate-limit',
            loadComponent: () =>
              import('./features/settings/admin-login-rate-limit-settings/admin-login-rate-limit-settings.component').then(
                (m) => m.AdminLoginRateLimitSettingsComponent,
              ),
            canActivate: [permissionGuard('adminloginratelimit:read')],
          },
          {
            path: 'client-presence',
            loadComponent: () =>
              import('./features/settings/client-presence-settings/client-presence-settings.component').then(
                (m) => m.ClientPresenceSettingsComponent,
              ),
            canActivate: [permissionGuard('clientpresencesettings:read')],
          },
          {
            path: 'mac-vendors',
            loadComponent: () =>
              import('./features/settings/mac-vendor-settings/mac-vendor-settings.component').then(
                (m) => m.MacVendorSettingsComponent,
              ),
            canActivate: [permissionGuard('macvendorsettings:read')],
          },
          {
            path: 'self-update',
            loadComponent: () =>
              import('./features/settings/self-update-settings/self-update-settings.component').then(
                (m) => m.SelfUpdateSettingsComponent,
              ),
            canActivate: [permissionGuard('selfupdatesettings:read')],
          },
          {
            path: 'dhcp',
            loadComponent: () =>
              import('./features/settings/dhcp-settings/dhcp-settings.component').then(
                (m) => m.DhcpSettingsComponent,
              ),
            canActivate: [permissionGuard('dhcpsettings:read')],
          },
        ],
      },
      {
        // User accounts (`/api/users`). Roles and direct permissions are edited on the user
        // form, not on nested routes.
        path: 'users',
        canActivate: [permissionGuard('users:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/users/user-list/user-list.component').then(
                (m) => m.UserListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/users/user-form/user-form.component').then(
                (m) => m.UserFormComponent,
              ),
            canActivate: [permissionGuard('users:write')],
          },
          {
            path: ':id/edit',
            loadComponent: () =>
              import('./features/users/user-form/user-form.component').then(
                (m) => m.UserFormComponent,
              ),
            canActivate: [permissionGuard('users:write')],
          },
        ],
      },
      {
        // Roles (`/api/roles`). A role's permissions are edited on the role form.
        path: 'roles',
        canActivate: [permissionGuard('roles:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/roles/role-list/role-list.component').then(
                (m) => m.RoleListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/roles/role-form/role-form.component').then(
                (m) => m.RoleFormComponent,
              ),
            canActivate: [permissionGuard('roles:write')],
          },
          {
            path: ':id/edit',
            loadComponent: () =>
              import('./features/roles/role-form/role-form.component').then(
                (m) => m.RoleFormComponent,
              ),
            canActivate: [permissionGuard('roles:write')],
          },
        ],
      },
      {
        // The signed-in user's API tokens. No permission guard: ApiTokensController needs only a
        // session and works on the caller's tokens alone (NAV_ITEMS has `permission: null`).
        // No edit route: a token cannot be changed after creation, only revoked from the list.
        path: 'api-tokens',
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/api-tokens/api-token-list/api-token-list.component').then(
                (m) => m.ApiTokenListComponent,
              ),
          },
          {
            path: 'new',
            loadComponent: () =>
              import('./features/api-tokens/api-token-form/api-token-form.component').then(
                (m) => m.ApiTokenFormComponent,
              ),
          },
        ],
      },
      {
        // The signed-in user's two-factor settings, opened from the user menu rather than the
        // nav. Only authGuard: TwoFactorController needs only a session and acts on the caller's
        // account.
        path: 'account/security',
        loadComponent: () =>
          import('./features/account/security/account-security.component').then(
            (m) => m.AccountSecurityComponent,
          ),
        canActivate: [authGuard],
      },
      {
        // The signed-in user's profile (display name, phone number, Gravatar avatar), opened
        // from the user menu. Only authGuard: `GET` and `PATCH /api/users/me` need only a
        // session and act on the caller's account.
        path: 'account/profile',
        loadComponent: () =>
          import('./features/account/profile/account-profile.component').then(
            (m) => m.AccountProfileComponent,
          ),
        canActivate: [authGuard],
      },
      {
        // Full backup and restore on one page; backups are downloaded, not stored as a list.
        // The route needs `backup:read`, which is what creating a backup requires. The page
        // hides the restore section without `backup:write`.
        path: 'backup',
        loadComponent: () =>
          import('./features/backup/backup.component').then((m) => m.BackupComponent),
        canActivate: [permissionGuard('backup:read')],
      },
      {
        // Start, stop and restart the server's services (`/api/services`). The services are a
        // fixed set registered in code. The route needs `services:read`; the page hides the
        // actions without `services:manage`.
        path: 'services',
        loadComponent: () =>
          import('./features/services/service-list/service-list.component').then(
            (m) => m.ServiceListComponent,
          ),
        canActivate: [permissionGuard('services:read')],
      },
    ],
  },
  { path: '**', redirectTo: '' },
];
