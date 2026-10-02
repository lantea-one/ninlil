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
 * This service works out the rows of the Dashboard's "Getting Started" checklist from the current
 * server state. There are four steps (add a zone, create a second user, enable a DNS transport
 * besides plain DNS, configure SMTP); a longer list would stop being "getting started".
 *
 * <p>A row appears only when the user holds the read permission its check needs (`zones:read`,
 * `users:read`, `transportsettings:read`, `smtpsettings:read`). Without it the row could never
 * turn complete, so it is left out. A check that fails anyway (a network error) shows its row as
 * incomplete, and the other rows still load.
 */
@Injectable({ providedIn: 'root' })
export class GettingStartedChecklistService {
  private readonly authService = inject(AuthService);
  private readonly zonesApi = inject(ZonesApiService);
  private readonly usersApi = inject(UsersApiService);
  private readonly transportSettingsApi = inject(TransportSettingsApiService);
  private readonly smtpSettingsApi = inject(SmtpSettingsApiService);

  /**
   * This method runs the checks of the rows the user may see in parallel and returns those rows
   * in display order. A user without `users:read`, for example, gets three rows. It never
   * rejects.
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
          // More than one, because the bootstrap administrator account always exists.
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
            // Plain DNS is on by default, so this checks the other four; any one of them being
            // enabled completes the row.
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
   * This method runs one row's `check`. A failure marks that row incomplete instead of rejecting
   * {@link fetchItems}.
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
