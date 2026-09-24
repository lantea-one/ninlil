import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import {
  SmtpSettingsService as SmtpSettingsApiService,
  TransportSettingsService as TransportSettingsApiService,
  UsersService as UsersApiService,
  ZonesService as ZonesApiService,
} from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { GettingStartedItem } from '../dashboard.model';

/**
 * This service resolves the 4 fixed rows of the Dashboard's "Getting Started" checklist against
 * this session's own real, current server state -- deliberately kept to exactly 4 (add a zone,
 * create a non-admin user, enable a DNS transport beyond Plain DNS, configure SMTP), since a
 * longer list stops reading as "getting started."
 * <p>
 * Every row is gated by its own real backend permission first, matching this app's existing
 * `NAV_ITEMS`/`SettingsShellComponent.SETTINGS_TABS` filtering precedent: a caller lacking a
 * row's underlying `*:read` permission never sees that row at all -- there is no way for such a
 * caller to ever observe it change to "complete," so showing it would just be permanently
 * misleading, not merely unhelpful. A row whose own fetch fails despite holding that permission
 * (a transient network error, a permission revoked between the guard check and the request)
 * still renders, marked incomplete rather than thrown away entirely -- the same "a denied/failed
 * read degrades one tile, never the whole dashboard" precedent {@link DashboardStatsService}'s own
 * `fetchCount` already established for the quick-stat tiles.
 */
@Injectable({ providedIn: 'root' })
export class GettingStartedChecklistService {
  private readonly authService = inject(AuthService);
  private readonly zonesApi = inject(ZonesApiService);
  private readonly usersApi = inject(UsersApiService);
  private readonly transportSettingsApi = inject(TransportSettingsApiService);
  private readonly smtpSettingsApi = inject(SmtpSettingsApiService);

  /**
   * This method resolves every permission-visible row concurrently (not one after another, since
   * no row's own completion check depends on another's) and returns them in the checklist's own
   * fixed display order, regardless of which rows a given session can see -- a caller missing the
   * `users:read` permission, say, simply gets a 3-row array back, never a gap or a placeholder.
   */
  async fetchItems(): Promise<GettingStartedItem[]> {
    const checks: Promise<GettingStartedItem>[] = [];

    if (this.authService.hasPermission('zones:read')) {
      checks.push(
        this.resolveItem('add-zone', 'Add your first zone', '/zones', async () => {
          const page = await firstValueFrom(this.zonesApi.apiZonesGet({ skip: 0, take: 1 }));
          return (page.totalCount ?? 0) > 0;
        }),
      );
    }

    if (this.authService.hasPermission('users:read')) {
      checks.push(
        this.resolveItem('create-user', 'Create a non-admin user', '/users', async () => {
          // > 1, not > 0 -- every server always has at least the bootstrap administrator account,
          // so this row is satisfied only once a second, real account exists alongside it.
          const page = await firstValueFrom(this.usersApi.apiUsersGet({ skip: 0, take: 1 }));
          return (page.totalCount ?? 0) > 1;
        }),
      );
    }

    if (this.authService.hasPermission('transportsettings:read')) {
      checks.push(
        this.resolveItem(
          'enable-transport',
          'Enable a DNS transport beyond Plain DNS',
          '/settings/transports',
          async () => {
            // Plain DNS itself is never checked here -- it is the one transport already on by
            // default, so this row is deliberately about the other 4, and is satisfied the
            // moment any single one of them is switched on, not all of them.
            const [dot, doh, doq, dnscrypt] = await Promise.all([
              firstValueFrom(this.transportSettingsApi.apiTransportSettingsDnsOverTlsGet()),
              firstValueFrom(this.transportSettingsApi.apiTransportSettingsDnsOverHttpsGet()),
              firstValueFrom(this.transportSettingsApi.apiTransportSettingsDnsOverQuicGet()),
              firstValueFrom(this.transportSettingsApi.apiTransportSettingsDnscryptGet()),
            ]);
            return Boolean(dot.enable || doh.enable || doq.enable || dnscrypt.enable);
          },
        ),
      );
    }

    if (this.authService.hasPermission('smtpsettings:read')) {
      checks.push(
        this.resolveItem(
          'configure-smtp',
          'Configure SMTP for password-reset e-mails',
          '/settings/smtp',
          async () => {
            const settings = await firstValueFrom(this.smtpSettingsApi.apiSmtpSettingsGet());
            return (settings.smtpHost ?? '').trim().length > 0;
          },
        ),
      );
    }

    return Promise.all(checks);
  }

  /**
   * This method wraps one row's own `check` in a `try`/`catch` so a single failed request marks
   * only that row incomplete, never rejects the whole {@link fetchItems} call.
   */
  private async resolveItem(
    id: string,
    label: string,
    routerLink: string,
    check: () => Promise<boolean>,
  ): Promise<GettingStartedItem> {
    try {
      return { id, label, routerLink, complete: await check() };
    } catch {
      return { id, label, routerLink, complete: false };
    }
  }
}
