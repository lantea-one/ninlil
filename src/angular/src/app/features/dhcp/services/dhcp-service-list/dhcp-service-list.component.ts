import { Component, inject, signal } from '@angular/core';
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
  DhcpServiceControlService,
  DhcpServiceKindEnum,
  DhcpServiceStatus,
} from '../../../../api-client';
import { AuthService } from '../../../../core/auth/auth.service';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../../shared/confirm-dialog/confirm-dialog.component';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';
import {
  dhcpLifecycleConfirmation,
  DhcpServiceLifecycleAction,
  enabledBadgeColor,
  enabledLabel,
  kindLabel,
  runningBadgeColor,
  runningLabel,
} from '../dhcp-service.model';

/**
 * This component lists the four DHCP-family services `DhcpServiceControlController` administers --
 * DHCPv4, DHCPv6, Router Advertisement, and TFTP -- sourced from `GET /api/dhcp-services`, each
 * showing two deliberately distinct signals side by side rather than collapsed into one badge: its
 * configured `Enabled` flag (whether it starts automatically at the next host boot) and its live
 * `IsRunning` signal (whether its socket is genuinely bound and serving traffic right now). These
 * two can legitimately differ in either direction -- a service can be configured off yet started
 * live through this same page, or configured on yet stopped live -- which is the entire point of
 * `IDhcpProtocolService`'s own real, in-process hot start/stop design (see that interface's own
 * doc comment, and `DhcpServiceControlController`'s own doc comment for why this is a separate REST
 * surface, and therefore a separate page, from this app's general Service Lifecycle Management page).
 *
 * <p>This page is deliberately **not** merged into {@link ServiceListComponent} (`features/services/`)
 * for that same structural reason -- `IManagedServiceRegistration` has no way to express "configured
 * `Enable` governs only the automatic boot-time start, never a later live one." It does, however,
 * reuse that page's own established visual/interaction idiom end to end, so an operator experiences
 * one consistent "service control" pattern across both pages even though they are backed by
 * different controllers: a persistent risk banner, a real running-state badge per row (here, a
 * second, distinctly colored badge for the configured flag alongside it), a dedicated confirmation
 * dialog naming each action's own real, service-specific consequence (see
 * {@link dhcpLifecycleConfirmation}), and every start/stop button hidden outright -- not merely
 * disabled -- for a session lacking `dhcpservices:manage`, mirroring
 * {@link ServiceListComponent}'s own "hide, don't merely disable, a destructive action the session
 * cannot use" precedent. The route itself is gated on the lighter `dhcpservices:read` (see
 * `app.routes.ts`), matching {@link List}'s own permission, since this list only reveals which
 * services exist, their configured flag, and whether each is genuinely running -- no secret or
 * configuration data.
 */
@Component({
  selector: 'app-dhcp-service-list',
  imports: [
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
  templateUrl: './dhcp-service-list.component.html',
  styleUrl: './dhcp-service-list.component.scss',
})
export class DhcpServiceListComponent {
  private readonly dhcpServicesApi = inject(DhcpServiceControlService);
  private readonly authService = inject(AuthService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending lifecycle action. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly kindLabel = kindLabel;
  readonly runningBadgeColor = runningBadgeColor;
  readonly runningLabel = runningLabel;
  readonly enabledBadgeColor = enabledBadgeColor;
  readonly enabledLabel = enabledLabel;

  readonly displayedColumns = ['name', 'enabled', 'running', 'actions'];

  /** Whether the signed-in session holds `dhcpservices:manage` -- gates every start/stop button. */
  readonly canManage = this.authService.hasPermission('dhcpservices:manage');

  readonly services = signal<DhcpServiceStatus[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** The kind of whichever single service currently has a lifecycle action in flight, if any. */
  readonly actingOn = signal<DhcpServiceKindEnum | null>(null);

  constructor() {
    void this.load();
  }

  /**
   * This method runs one lifecycle action against one DHCP-family service, but only after the
   * operator confirms a dialog naming that action's own real, service-specific consequence -- see
   * {@link dhcpLifecycleConfirmation}.
   */
  async invoke(action: DhcpServiceLifecycleAction, service: DhcpServiceStatus): Promise<void> {
    if (!service.kind) return;
    const kind = service.kind;

    const { title, message, confirmLabel } = dhcpLifecycleConfirmation(action, kind);
    const confirmed = await this.confirm({ title, message, confirmLabel });
    if (!confirmed) return;

    this.actingOn.set(kind);
    try {
      const updated = await firstValueFrom(this.callAction(action, kind));
      this.replaceService(updated);
      this.notification.success(`${kindLabel(kind)} ${pastTense(action)}.`);
    } catch (error) {
      this.notification.error(
        extractErrorMessage(error, `Could not ${action} ${kindLabel(kind)}.`),
      );
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

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  private callAction(action: DhcpServiceLifecycleAction, kind: DhcpServiceKindEnum) {
    return action === 'start'
      ? this.dhcpServicesApi.apiDhcpServicesKindStartPost({ kind })
      : this.dhcpServicesApi.apiDhcpServicesKindStopPost({ kind });
  }

  /** This method swaps one updated service status into {@link services} in place, by kind. */
  private replaceService(updated: DhcpServiceStatus): void {
    this.services.update((services) =>
      services.map((service) => (service.kind === updated.kind ? updated : service)),
    );
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const services = await firstValueFrom(this.dhcpServicesApi.apiDhcpServicesGet());
      this.services.set(services);
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load the DHCP-family service status.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}

/** This function renders one lifecycle action's own past-tense verb, for a success notification. */
function pastTense(action: DhcpServiceLifecycleAction): string {
  return action === 'start' ? 'started' : 'stopped';
}
