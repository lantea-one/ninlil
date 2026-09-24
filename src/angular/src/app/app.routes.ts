import { Routes } from '@angular/router';
import { authGuard } from './core/guards/auth.guard';
import { permissionGuard } from './core/guards/permission.guard';
import { AppShellComponent } from './layout/app-shell/app-shell.component';
import { ForbiddenComponent } from './layout/forbidden/forbidden.component';

/**
 * This constant contains this app's full route table. Every route under {@link AppShellComponent}
 * requires at least a signed-in session ({@link authGuard} on the shell route itself, checked
 * once for the whole subtree); several also require a specific permission
 * ({@link permissionGuard}), checked again on that specific child route so a direct navigation
 * (a typed-in URL, a bookmark) is blocked exactly like a nav click would be -- this app's own
 * "hidden in nav AND blocked by a guard, not just visually hidden" requirement. Every permission
 * string here matches {@link NAV_ITEMS}'s own real, already-checked-against-the-backend value.
 *
 * <p>Every page is lazy-loaded (`loadComponent`), so the initial bundle carries only the shell,
 * the guards, and what the first screen needs; each feature page downloads as its own chunk the
 * first time it is visited. Only {@link AppShellComponent} (the frame every signed-in page
 * renders inside) and {@link ForbiddenComponent} (tiny, and reached precisely when navigation is
 * being refused) stay eager. Add new pages the same way -- an eager `component:` import here
 * pulls the page, and everything it imports, back into the initial bundle.
 */
export const routes: Routes = [
  {
    path: 'login',
    loadComponent: () =>
      import('./features/auth/login/login.component').then((m) => m.LoginComponent),
  },
  {
    // This project's public, self-service "request a password reset e-mail" page --
    // `LoginComponent`'s own "Forgot your password?" link leads here. A top-level route sibling
    // to `login`/`reset-password`, with no `authGuard` (matching both their shape exactly), since
    // a visitor reaching this page has, by definition, no session to guard.
    path: 'forgot-password',
    loadComponent: () =>
      import('./features/auth/forgot-password/forgot-password.component').then(
        (m) => m.ForgotPasswordComponent,
      ),
  },
  {
    // This project's shared, public "set new password" landing page -- see
    // `SetNewPasswordComponent`'s own doc comment for why it exists as a standalone,
    // authentication-free route (no `authGuard`, matching `login`'s own shape exactly, both
    // reachable by a caller with no session at all) serving both an administrator-driven
    // `EmailLink` reset e-mail and a genuinely self-service Forgot Password request raised via
    // `forgot-password` above, unchanged. `userId`/`token` arrive as query params, not route
    // params, since a mailed link is the only way this route is ever reached.
    path: 'reset-password',
    loadComponent: () =>
      import('./features/auth/reset-password/set-new-password.component').then(
        (m) => m.SetNewPasswordComponent,
      ),
  },
  {
    // The landing page a completed OpenID Connect sign-in redirects the browser back to, tokens
    // in the URL fragment -- see `SsoCallbackComponent`'s own doc comment. A top-level route
    // sibling to `login`/`forgot-password`/`reset-password`, with no `authGuard`, matching all
    // three exactly: a caller reaching this page has no session yet, by definition -- applying
    // the tokens this page parses is what creates one.
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
        // This app's own zone/record management area -- one flat children list, since a route
        // like ':zoneId/records/new' already carries every param it needs (`zoneId`) without a
        // further level of route nesting. `zones:read` gates the whole subtree (matching every
        // other top-level nav destination's own single guard on its parent route); the 3
        // write-capable routes (create/edit a zone, create/edit a record) additionally require
        // `zones:write`, mirroring `ZonesController`/`RecordsController`'s own permission split.
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
          // This app's own DNSSEC key management, nested here rather than under a separate
          // top-level nav item -- DnssecKeysController itself requires the exact same
          // zones:read/zones:write permission pair RecordsController does, since a key belongs
          // to exactly one zone (DnssecKeyEntity.ZoneId), not a resource of its own.
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
        // This app's own DNS rewrite management -- flat/standalone, matching
        // DnsRewritesController's own non-nested `/api/dns-rewrites` shape exactly (a rewrite
        // has no zone/parent relationship at all, unlike a zone's own records).
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
        // This app's own TSIG key management -- standalone (not zone-scoped), matching
        // TsigKeysController's own `/api/tsig-keys` shape exactly.
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
        // This app's own cluster peer management -- standalone (not settings-scoped), matching
        // ClusterController's own `/api/cluster/nodes` shape. See ClusterSettingsComponent
        // (under settings/cluster) for the Enable/AdvertisedHost toggle an operator must set
        // before either action here can succeed.
        path: 'cluster',
        loadComponent: () =>
          import('./features/cluster/cluster.component').then((m) => m.ClusterComponent),
        canActivate: [permissionGuard('clusternodes:read')],
      },
      {
        // This app's own cloud provider credential management -- standalone, matching
        // CloudProviderCredentialsController's own `/api/cloud-provider-credentials` shape.
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
        // This app's own filtering area: filter lists (standalone) with their own entries
        // nested underneath, following the identical shape ZonesController/RecordsController
        // already established. filterlists:read gates the whole subtree; filterlists:write
        // additionally guards every create/edit route, mirroring every other resource area.
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
        // The known-client registry: the client itself (the top-level resource) with its own
        // identifiers (addresses), direct filter-list assignments, time-of-day parental-control schedules, and read-only
        // activity view nested underneath -- mirroring exactly the same "one parent, a nested
        // child collection, separate routes rather than tabs" shape client-groups establishes
        // below for a group's own networks/list assignments/schedules. Safe Search/instant pause
        // are inline panels on the edit form itself instead, since neither has a real paginated
        // collection of its own to list.
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
            // Add/remove only, on one page -- an identifier is immutable once added, so there is
            // no edit route; the page hides its own write controls without clients:write.
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
            // No edit route here -- ClientListAssignmentsController itself has no PUT/PATCH;
            // changing which list is assigned means deleting one assignment and creating
            // another, so this "new" route is the only write route this sub-resource ever needs
            // -- mirroring ClientGroupListAssignmentsController's own identical contract below.
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
          // Read-only -- no "new"/"edit" routes at all, since an activity entry is server-written
          // history, never an operator-authored resource, mirroring QueryLogListComponent's own
          // identical posture.
          {
            path: ':clientId/activity',
            loadComponent: () =>
              import('./features/clients/activity/client-activity-list/client-activity-list.component').then(
                (m) => m.ClientActivityListComponent,
              ),
          },
        ],
      },
      {
        // This app's own manual static address-to-client-identity mapping set --
        // flat/standalone, matching DnsClientStaticAddressMappingsController's own non-nested
        // `/api/client-static-address-mappings` shape exactly, the same route-nesting posture
        // access-control-rules already establishes below for its own comparable flat entity.
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
        // This app's own client-group area: client groups (standalone) with their own networks,
        // filter-list assignments, and time-of-day parental-control schedules nested underneath
        // -- three separate nested sub-resources, matching
        // ClientGroupNetworksController/ClientGroupListAssignmentsController/
        // ClientGroupSchedulesController's own separate REST shapes rather than combining them
        // into one page.
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
            // No edit route here -- ClientGroupListAssignmentsController itself has no
            // PUT/PATCH; changing which list is assigned means deleting one assignment and
            // creating another, so this "new" route is the only write route this sub-resource
            // ever needs.
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
        // DHCP-family service control's own real hot start/stop surface -- flat/standalone,
        // matching DhcpServiceControlController's own non-nested `/api/dhcp-services` shape,
        // the same "no create/edit/delete route, a fixed set nothing an operator authors" posture
        // ServiceListComponent's own `services` route establishes below for its general
        // Service Lifecycle Management surface. Deliberately kept separate from that page (see
        // DhcpServiceListComponent's own doc comment for the real structural reason) but gated the
        // identical way: the route itself needs only the lighter `dhcpservices:read` (matching
        // `List`'s own permission), with the far more consequential start/stop actions
        // additionally hidden, in-component, from a session lacking `dhcpservices:manage`.
        path: 'dhcp-services',
        loadComponent: () =>
          import('./features/dhcp/services/dhcp-service-list/dhcp-service-list.component').then(
            (m) => m.DhcpServiceListComponent,
          ),
        canActivate: [permissionGuard('dhcpservices:read')],
      },
      {
        // This app's own DHCP scope area: scopes (the top-level resource) with their own
        // reservations nested underneath, matching DhcpReservationsController's own nested REST
        // shape, the same "one parent, several nested child collections" pattern client-groups
        // establishes above for a group's own networks/list assignments. A scope's own custom
        // options are no longer a separate nested route at all -- DhcpScopeFormComponent manages
        // them inline, as a dynamic table, directly on this same create/edit page (see that
        // class's own doc comment).
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
        // This app's own DHCP tag-rule area: rules (global, since a rule's own scopeId is
        // nullable). A rule's own contributed options are no longer a separate nested route
        // either -- DhcpTagRuleFormComponent manages them inline the same way DhcpScopeFormComponent
        // manages a scope's own custom options (see that class's own doc comment).
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
        // This app's own DHCP lease table -- flat/read-only, matching DhcpLeasesController's own
        // "a lease is a live, engine-owned artifact, never operator-created" contract exactly
        // (no "new"/"edit" routes at all, the same posture the query-log route above establishes).
        path: 'dhcp-leases',
        loadComponent: () =>
          import('./features/dhcp/leases/dhcp-lease-list/dhcp-lease-list.component').then(
            (m) => m.DhcpLeaseListComponent,
          ),
        canActivate: [permissionGuard('dhcpleases:read')],
      },
      {
        // This app's own forwarder area -- flat/standalone, matching ForwardersController's own
        // non-nested `/api/forwarders` shape exactly (this is this server's own global upstream-
        // forwarder set, entirely separate from a zone's own per-zone forwarderAddresses
        // override, which ZoneFormComponent already manages).
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
        // This app's own real per-client/network ACL rule set -- flat/standalone, matching
        // DnsAccessControlRulesController's own non-nested `/api/access-control-rules` shape
        // exactly (one row per network+capability set, no owning parent).
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
        // The persistent query log -- flat/standalone, matching QueryLogController's own
        // non-nested `/api/query-log` shape exactly. There is no "new"/"edit" route here at all
        // (a log entry is written by the server itself, never authored by an operator) -- the
        // list component's own "Clear log" action is gated in-component on `querylog:write`
        // rather than by a second, dedicated route, since there is no separate route for it to
        // guard.
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
        // This app's own outbound webhook subscription management -- flat/standalone, matching
        // WebhooksController's own non-nested `/api/webhooks` shape exactly. The event-type
        // picker inside WebhookFormComponent is deliberately restricted to the 7 curated event
        // types WebhookDeliverySubscriber<TEvent> actually delivers (see `webhook.model.ts`), not
        // every name WebhooksController's own validation would technically accept.
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
          // Read-only delivery-attempt history for one subscription -- matches
          // WebhooksController's own GET /api/webhooks/{id}/delivery-attempts shape. Gated only
          // by the parent route's own webhooks:read guard above (no separate ':write' route
          // exists here, since there is nothing on this page an operator can write).
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
        // The settings area: a small tabbed shell (SettingsShellComponent) hosting one sub-page
        // per REST surface -- presets (PresetsController), the recursive resolver
        // (RecursiveSettingsController), every DNS transport (TransportSettingsController),
        // block/allow-list filtering's own engine toggle (FilteringSettingsController), upstream
        // forwarding's own engine toggle (ForwardingSettingsController), DNS rewrites' own engine
        // toggle (RewriteSettingsController), rate limiting (RateLimitingSettingsController), the
        // message-broker event transport (EventBrokerSettingsController), DNS Cookies
        // (DnsCookieSettingsController), query logging (QueryLoggingSettingsController),
        // reverse-proxy trust (ReverseProxySettingsController), the global default appearance
        // (AppearanceSettingsController), the live storage-engine switch wizard
        // (StorageController), ACME/Let's Encrypt issuance plus the real per-domain
        // certificate inventory (AcmeSettingsController), the SMTP server this host already
        // sends e-mail-based two-factor authentication codes through (SmtpSettingsController),
        // which DNSSEC algorithms a new signing key may use (DnssecPolicySettingsController),
        // ASP.NET Core Identity's own password-complexity/lockout policy
        // (IdentitySettingsController), the admin web listener's own port/certificate
        // (WebListenSettingsController, confirm-then-apply-on-restart, never live-apply -- see
        // that controller's own doc comment), the global PTR/reverse-zone sync default
        // (PtrSyncSettingsController), DNSSEC signature validity/inception/refresh timing
        // (ZoneSigningSettingsController), and the admin-login brute-force limiter's own attempt
        // threshold/lockout window (AdminLoginRateLimitSettingsController -- genuinely distinct
        // from, and independent of, the "Rate limiting" tab's own DNS-query limiter). Each
        // sub-page requires its own distinct permission
        // (not the shared `presets:read` the parent nav item itself uses), matching each
        // controller's own real `[RequirePermission(...)]` attribute -- see
        // SettingsShellComponent's own doc comment for why.
        path: 'settings',
        loadComponent: () =>
          import('./features/settings/settings-shell/settings-shell.component').then(
            (m) => m.SettingsShellComponent,
          ),
        canActivate: [permissionGuard('presets:read')],
        children: [
          {
            path: '',
            loadComponent: () =>
              import('./features/settings/preset-list/preset-list.component').then(
                (m) => m.PresetListComponent,
              ),
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
        // This app's own user-account management -- standalone, matching UsersController's own
        // `/api/users` shape. Role assignment and direct-permission-claim assignment both live
        // inside UserFormComponent itself (edit mode only -- a user must exist first), rather
        // than as further nested routes, since neither has a REST shape distinct enough to
        // justify its own page the way, say, a zone's own records do.
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
        // This app's own role management -- standalone, matching RolesController's own
        // `/api/roles` shape. Permission-claim assignment lives inside RoleFormComponent itself
        // (edit mode only), the same reasoning as `users` above.
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
        // This app's own self-service API token management -- no permission guard at all, per
        // NAV_ITEMS's own `permission: null`: ApiTokensController itself carries no
        // [RequirePermission(...)], scoped server-side to the calling user's own tokens only.
        // No edit route -- a token has nothing editable after creation, only revocation, which
        // ApiTokenListComponent's own row action already covers.
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
        // This app's own self-service account-security page -- reachable from the app shell's
        // own user menu, not a NAV_ITEMS entry (no nav slot was reserved for it). Gated only by
        // authGuard (any signed-in session, the same bar the shell route above it already sets)
        // since TwoFactorController itself carries no [RequirePermission(...)] either -- every
        // action on this page acts on the caller's own account, never anyone else's.
        path: 'account/security',
        loadComponent: () =>
          import('./features/account/security/account-security.component').then(
            (m) => m.AccountSecurityComponent,
          ),
        canActivate: [authGuard],
      },
      {
        // This app's own self-service Profile page -- display name, phone number, and a
        // Gravatar-backed avatar, distinct from `AccountSecurityComponent` above. Reachable from
        // the app shell's own user menu, not a NAV_ITEMS entry, mirroring that route's own shape
        // exactly: gated only by authGuard, since UsersController's own GetMine/PatchMine carry
        // no [RequirePermission(...)] either -- every action here acts on the caller's own
        // account, never anyone else's.
        path: 'account/profile',
        loadComponent: () =>
          import('./features/account/profile/account-profile.component').then(
            (m) => m.AccountProfileComponent,
          ),
        canActivate: [authGuard],
      },
      {
        // The full-system backup/restore page -- a single, standalone route with no
        // create/edit split, since there is exactly one backup archive an operator can create
        // at a time and exactly one restore action, neither a persisted, listable resource.
        // Gated on `backup:read` alone (the permission required to create a backup); the far
        // more destructive restore section within the page itself is additionally hidden,
        // in-component, from a session that lacks `backup:write` -- matching
        // `QueryLogListComponent`'s own "hide, don't merely disable, a destructive action the
        // session cannot use" precedent, since there is no separate route to guard for it.
        path: 'backup',
        loadComponent: () =>
          import('./features/backup/backup.component').then((m) => m.BackupComponent),
        canActivate: [permissionGuard('backup:read')],
      },
      {
        // Service Lifecycle Management's own real hot start/stop/restart surface -- flat/
        // standalone, matching ServicesController's own non-nested `/api/services` shape. No
        // create/edit/delete route exists here at all -- every service is registered in code, a
        // fixed set with nothing an operator authors. Gated on the lighter `services:read`
        // (matching `List`'s own permission); the far more dangerous start/stop/restart actions
        // are additionally hidden, in-component, from a session lacking `services:manage` --
        // mirroring `BackupComponent`'s own "hide, don't merely disable" precedent, since there
        // is no separate route to guard for them either.
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
