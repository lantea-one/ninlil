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
 * This component is the DHCP Services page (`/dhcp-services`, `dhcpservices:read`). It lists the
 * four services `DhcpServiceControlController` controls (DHCPv4, DHCPv6, Router Advertisement and
 * TFTP) from `GET /api/dhcp-services`. Each row shows two separate badges: the configured `Enable`
 * flag, which decides whether the service starts at boot, and whether it is running now. They
 * can differ either way, because start and stop here change only the running state, not the flag.
 *
 * <p>It is separate from the general Services page ({@link ServiceListComponent}) because that
 * page's registration model cannot express "the flag governs only the start at boot". It looks
 * and behaves the same way: a risk banner, a confirmation naming each action's consequence
 * ({@link dhcpLifecycleConfirmation}), and start and stop buttons
 * (`POST /api/dhcp-services/{kind}/start` and `/stop`) shown only with `dhcpservices:manage`.
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

  /** Whether the start/stop confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly kindLabel = kindLabel;
  readonly runningBadgeColor = runningBadgeColor;
  readonly runningLabel = runningLabel;
  readonly enabledBadgeColor = enabledBadgeColor;
  readonly enabledLabel = enabledLabel;

  readonly displayedColumns = ['name', 'enabled', 'running', 'actions'];

  /** Whether the user holds `dhcpservices:manage`; the start and stop buttons show only then. */
  readonly canManage = this.authService.hasPermission('dhcpservices:manage');

  readonly services = signal<DhcpServiceStatus[]>([]);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** The service whose start or stop is in flight, if any. */
  readonly actingOn = signal<DhcpServiceKindEnum | null>(null);

  constructor() {
    void this.load();
  }

  /**
   * This method asks for confirmation ({@link dhcpLifecycleConfirmation}), then starts or stops
   * a service and puts the returned status in its row.
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

  /** This method hides the confirmation dialog and settles the pending {@link confirm} promise. */
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

  /** This method replaces one service's status in {@link services}, matched by kind. */
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

/** This function returns the past tense of an action, for the success notification. */
function pastTense(action: DhcpServiceLifecycleAction): string {
  return action === 'start' ? 'started' : 'stopped';
}
