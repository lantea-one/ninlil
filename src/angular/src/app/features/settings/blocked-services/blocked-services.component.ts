import { Component, Input, OnInit, inject, signal } from '@angular/core';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  SpinnerComponent,
} from '@coreui/angular';
import { RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { BlockedServiceDetail, BlockedServicesService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Blocked Services" settings page (`/settings/blocked-services`). It lists
 * the curated catalog from `GET /api/blocked-services` (`filterlists:read`) and, with
 * `filterlists:write`, blocks or unblocks a service with one click (`POST` or
 * `DELETE /api/blocked-services/{key}`). Blocking creates an ordinary subscribed filter list for
 * the service's domain list (see `BlockedServiceCatalog` for the data source and license). There
 * is no confirmation, since the same button undoes it.
 *
 * <p>The client and client group forms embed it as a panel with {@link clientId} or
 * {@link clientGroupId} set (at most one), which scopes every toggle to that target; see
 * `BlockedServicesController` for the backend rules. On the settings page neither is set and the
 * toggles act globally.
 */
@Component({
  selector: 'app-blocked-services',
  imports: [
    IfPermissionDirective,
    RouterLink,
    AlertComponent,
    BadgeComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    SpinnerComponent,
  ],
  templateUrl: './blocked-services.component.html',
  styleUrl: './blocked-services.component.scss',
})
export class BlockedServicesComponent implements OnInit {
  private readonly blockedServicesApi = inject(BlockedServicesService);
  private readonly notification = inject(NotificationService);

  /** The client every toggle targets, or `undefined` for the global setting. */
  @Input() clientId?: string;

  /** The client group every toggle targets, or `undefined` for the global setting. */
  @Input() clientGroupId?: string;

  readonly services = signal<BlockedServiceDetail[]>([]);
  readonly loading = signal(true);
  readonly togglingKey = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  /** This method blocks or unblocks one catalog entry, depending on its state, for this panel's target. */
  async toggle(service: BlockedServiceDetail): Promise<void> {
    const key = service.key;
    if (!key) return;

    this.togglingKey.set(key);
    this.errorMessage.set(null);

    try {
      if (service.isEnabled) {
        await firstValueFrom(
          this.blockedServicesApi.apiBlockedServicesKeyDelete({
            key,
            clientId: this.clientId,
            clientGroupId: this.clientGroupId,
          }),
        );
        this.notification.success(`${service.displayName} unblocked.`);
      } else {
        await firstValueFrom(
          this.blockedServicesApi.apiBlockedServicesKeyPost({
            key,
            clientId: this.clientId,
            clientGroupId: this.clientGroupId,
          }),
        );
        this.notification.success(`${service.displayName} blocked.`);
      }
      await this.load();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not update this service.'));
    } finally {
      this.togglingKey.set(null);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const services = await firstValueFrom(
        this.blockedServicesApi.apiBlockedServicesGet({
          clientId: this.clientId,
          clientGroupId: this.clientGroupId,
        }),
      );
      this.services.set(services ?? []);
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load the blocked services catalog.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
