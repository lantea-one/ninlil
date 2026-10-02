import { NgTemplateOutlet } from '@angular/common';
import { Component, computed, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  ManagedServiceCategoryEnum,
  ManagedServiceSummary,
  ServicesService,
} from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/confirm-dialog/confirm-dialog.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  categoryLabel,
  lifecycleConfirmation,
  ServiceLifecycleAction,
  statusBadgeColor,
  statusLabel,
} from '../service.model';

/**
 * This component is the Services page (`/services`, `services:read`). It lists the services
 * `IManagedServiceRegistry` tracks, from `GET /api/services`: the DNS transport listeners and the
 * background maintenance loops, in two sections, each with a running badge. The set is fixed in
 * code, so the page only starts, stops and restarts services
 * (`POST /api/services/{name}/start`, `/stop`, `/restart`).
 *
 * <p>These actions are dangerous: stopping a transport cuts off DNS for its clients at once, and
 * stopping a background loop can let DNSSEC signatures expire, a secondary zone go stale or a
 * filter list stop refreshing. So the page shows a risk banner, asks for confirmation with text
 * naming the consequence ({@link lifecycleConfirmation}), and shows the buttons only with
 * `services:manage`.
 */
@Component({
  selector: 'app-service-list',
  imports: [
    NgTemplateOutlet,
    CdkTableModule,
    AlertComponent,
    BadgeComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardHeaderComponent,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    ConfirmDialogComponent,
  ],
  templateUrl: './service-list.component.html',
  styleUrl: './service-list.component.scss',
})
export class ServiceListComponent {
  private readonly servicesApi = inject(ServicesService);
  private readonly authService = inject(AuthService);
  private readonly notification = inject(NotificationService);

  /** Whether the action confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly categoryLabel = categoryLabel;
  readonly statusBadgeColor = statusBadgeColor;
  readonly statusLabel = statusLabel;

  readonly displayedColumns = ['name', 'status', 'actions'];

  /** Whether the user holds `services:manage`; the action buttons show only then. */
  readonly canManage = this.authService.hasPermission('services:manage');

  readonly services = signal<ManagedServiceSummary[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** The service whose action is in flight, if any. */
  readonly actingOn = signal<string | null>(null);

  /** The transport services, in backend order. */
  readonly transportServices = computed(() =>
    this.services().filter((service) => service.category === ManagedServiceCategoryEnum.Transport),
  );

  /** The background services, in backend order. */
  readonly backgroundServices = computed(() =>
    this.services().filter((service) => service.category === ManagedServiceCategoryEnum.Background),
  );

  constructor() {
    void this.load();
  }

  /**
   * This method asks for confirmation ({@link lifecycleConfirmation}), then runs the action and
   * puts the returned status in the service's row.
   */
  async invoke(action: ServiceLifecycleAction, service: ManagedServiceSummary): Promise<void> {
    if (!service.name) return;

    const { title, message, confirmLabel } = lifecycleConfirmation(action, service);
    const confirmed = await this.confirm({ title, message, confirmLabel });
    if (!confirmed) return;

    this.actingOn.set(service.name);
    try {
      const updated = await firstValueFrom(this.callAction(action, service.name));
      this.replaceService(updated);
      this.notification.success(`${service.name} ${pastTense(action)}.`);
    } catch (error) {
      this.notification.error(extractErrorMessage(error, `Could not ${action} "${service.name}".`));
    } finally {
      this.actingOn.set(null);
    }
  }

  /**
   * This method shows {@link ConfirmDialogComponent} with the given text and resolves once the
   * operator confirms or dismisses it.
   */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method hides the confirmation dialog and settles the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private callAction(action: ServiceLifecycleAction, name: string) {
    switch (action) {
      case 'start':
        return this.servicesApi.apiServicesNameStartPost({ name });
      case 'stop':
        return this.servicesApi.apiServicesNameStopPost({ name });
      case 'restart':
        return this.servicesApi.apiServicesNameRestartPost({ name });
    }
  }

  /** This method replaces one service in {@link services}, matched by name. */
  private replaceService(updated: ManagedServiceSummary): void {
    this.services.update((services) =>
      services.map((service) => (service.name === updated.name ? updated : service)),
    );
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const services = await firstValueFrom(this.servicesApi.apiServicesGet());
      this.services.set(services);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load the registered services.'));
    } finally {
      this.loading.set(false);
    }
  }
}

/** This function returns the past tense of an action, for the success notification. */
function pastTense(action: ServiceLifecycleAction): string {
  switch (action) {
    case 'start':
      return 'started';
    case 'stop':
      return 'stopped';
    case 'restart':
      return 'restarted';
  }
}
