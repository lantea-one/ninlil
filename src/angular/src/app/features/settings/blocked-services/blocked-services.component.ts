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
import { firstValueFrom } from 'rxjs';
import { BlockedServiceDetail, BlockedServicesService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component lists Ninlil's own curated "blocked services" catalog
 * (`BlockedServiceCatalog`/`BlockedServicesController`) and lets an operator toggle each named
 * consumer service on or off with a single click. Enabling a service creates (behind the API) an
 * ordinary, subscribed filter list for that service's own real, independently-sourced domain
 * list -- see `BlockedServiceCatalog`'s own doc comment for the data source and license. No
 * confirmation dialog: unlike applying a preset, toggling one service here is a small, easily
 * reversed action (the same toggle disables it again), so this mirrors the lighter-weight pattern
 * an ordinary settings checkbox already uses rather than {@link PresetListComponent}'s own
 * confirm-first apply flow.
 *
 * <p>Reused as a scoped panel, not just the global Settings page: an optional {@link clientId}/
 * {@link clientGroupId} input (mutually exclusive; set at most one) narrows every toggle to that
 * single target instead of the shared, global default -- see `BlockedServicesController`'s own
 * doc comment for the exact backend contract this thin wrapper follows. Neither set (the global
 * Settings page's own usage) reproduces this component's pre-scoping behavior exactly.
 */
@Component({
  selector: 'app-blocked-services',
  imports: [
    IfPermissionDirective,
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

  /** The single client this panel's every toggle targets, or `undefined` for the shared, global default. */
  @Input() clientId?: string;

  /** The single client group this panel's every toggle targets, or `undefined` for the shared, global default. */
  @Input() clientGroupId?: string;

  readonly services = signal<BlockedServiceDetail[]>([]);
  readonly loading = signal(true);
  readonly togglingKey = signal<string | null>(null);
  readonly errorMessage = signal<string | null>(null);

  ngOnInit(): void {
    void this.load();
  }

  /** This method enables or disables a single catalog entry, per its own current state, within this panel's own target scope (if any). */
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
