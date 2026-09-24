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
 * This component lists every service `IManagedServiceRegistry` tracks -- every DNS transport
 * listener and every background maintenance loop this server runs -- grouped into its own
 * {@link ManagedServiceCategoryEnum.Transport}/{@link ManagedServiceCategoryEnum.Background}
 * section, each showing a real running-state badge sourced from `GET /api/services`. This is a
 * fixed, server-defined set with no create/edit/delete of its own (a service is registered in
 * code, not authored by an operator), so this feature has only this one list view -- start/stop/
 * restart are the entirety of what this page offers, matching `ServicesController`'s own three
 * write actions exactly.
 *
 * <p>`ServicesController`'s own doc comment calls this "one of the most operationally dangerous
 * endpoints in this entire REST API": stopping the wrong transport cuts off real client DNS
 * resolution immediately, and stopping the wrong background loop can leave DNSSEC signatures to
 * silently expire, a secondary zone to drift stale, or a filter list to stop refreshing. This page
 * reflects that directly rather than rendering a plain list with buttons -- a persistent risk
 * banner, a dedicated confirmation dialog per action/category pairing (see
 * {@link lifecycleConfirmation}) naming the real consequence in its own words, and every
 * start/stop/restart button hidden outright (not merely disabled) for a session lacking
 * `services:manage`, mirroring {@link QueryLogListComponent}'s own "hide, don't merely disable, a
 * destructive action the session cannot use" precedent. The route itself is gated on the lighter
 * `services:read` (see `app.routes.ts`), since this list only reveals which services exist and
 * whether each is running -- no secret or configuration data.
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

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending lifecycle action. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly categoryLabel = categoryLabel;
  readonly statusBadgeColor = statusBadgeColor;
  readonly statusLabel = statusLabel;

  readonly displayedColumns = ['name', 'status', 'actions'];

  /** Whether the signed-in session holds `services:manage` -- gates every start/stop/restart button. */
  readonly canManage = this.authService.hasPermission('services:manage');

  readonly services = signal<ManagedServiceSummary[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** The name of whichever single service currently has a lifecycle action in flight, if any. */
  readonly actingOn = signal<string | null>(null);

  /** Every {@link ManagedServiceCategoryEnum.Transport} service, in the order the backend returned. */
  readonly transportServices = computed(() =>
    this.services().filter((service) => service.category === ManagedServiceCategoryEnum.Transport),
  );

  /** Every {@link ManagedServiceCategoryEnum.Background} service, in the order the backend returned. */
  readonly backgroundServices = computed(() =>
    this.services().filter((service) => service.category === ManagedServiceCategoryEnum.Background),
  );

  constructor() {
    void this.load();
  }

  /**
   * This method runs one lifecycle action against one service, but only after the operator
   * confirms a dialog naming the action's own real, category-specific risk -- see
   * {@link lifecycleConfirmation}.
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
   * operator confirms or dismisses it -- the declarative analogue of the old
   * `MatDialog.open(ConfirmDialogComponent, { data }).afterClosed()` this project used before
   * migrating onto CoreUI for Angular.
   */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
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

  /** This method swaps one updated service summary into {@link services} in place, by name. */
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

/** This function renders one lifecycle action's own past-tense verb, for a success notification. */
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
