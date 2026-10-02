import { Component, computed, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
  AlertComponent,
  ButtonDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  DhcpReservationDetail,
  DhcpReservationsService,
  DhcpScopeKindEnum,
  DhcpScopesService,
} from '../../../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../../../shared/confirm-dialog/confirm-dialog.component';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../../../shared/if-permission/if-permission.directive';

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is a DHCP scope's reservations page (`/dhcp-scopes/:scopeId/reservations`,
 * `dhcpscopes:read`). It lists reservations a page at a time from
 * `GET /api/dhcp-scopes/{scopeId}/reservations` and, with `dhcpscopes:write`, links to add and
 * edit and deletes one (`DELETE .../reservations/{id}`).
 *
 * <p>RFC 8415 has no reservation for a delegated prefix, and the backend refuses one for a Prefix
 * Delegation scope. The page loads the scope's `kind` with its name ({@link loadScope}) and, for
 * such a scope, hides "Add reservation" and shows a notice instead.
 */
@Component({
  selector: 'app-dhcp-reservation-list',
  imports: [
    IfPermissionDirective,
    RouterLink,
    CdkTableModule,
    AlertComponent,
    ButtonDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    EmptyStateComponent,
  ],
  templateUrl: './dhcp-reservation-list.component.html',
  styleUrl: './dhcp-reservation-list.component.scss',
})
export class DhcpReservationListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly reservationsApi = inject(DhcpReservationsService);
  private readonly scopesApi = inject(DhcpScopesService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly scopeId = this.route.snapshot.paramMap.get('scopeId') ?? '';

  readonly displayedColumns = ['clientIdentifier', 'ipAddress', 'hostName', 'status', 'actions'];

  readonly scopeName = signal<string | null>(null);

  /** The scope's `kind`, or `null` before it loads. */
  private readonly scopeKind = signal<DhcpScopeKindEnum | null>(null);

  /** Whether the scope is a Prefix Delegation scope, which cannot have reservations. */
  readonly isPrefixDelegationScope = computed(
    () => this.scopeKind() === DhcpScopeKindEnum.PrefixDelegation,
  );

  readonly reservations = signal<DhcpReservationDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadScope();
    void this.loadPage();
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method asks for confirmation, then deletes a reservation. */
  async deleteReservation(reservation: DhcpReservationDetail): Promise<void> {
    if (!reservation.id) return;

    const confirmed = await this.confirm({
      title: 'Delete this reservation?',
      message: `This permanently removes the reservation for '${reservation.clientIdentifier}'. This cannot be undone.`,
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.reservationsApi.apiDhcpScopesScopeIdReservationsIdDelete({
          scopeId: this.scopeId,
          id: reservation.id,
        }),
      );
      this.notification.success('Reservation deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this reservation.'));
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

  private async loadScope(): Promise<void> {
    try {
      const scope = await firstValueFrom(this.scopesApi.apiDhcpScopesIdGet({ id: this.scopeId }));
      this.scopeName.set(scope.name ?? null);
      this.scopeKind.set(scope.kind ?? DhcpScopeKindEnum.AddressAssignment);
    } catch {
      // The name and kind are for the heading and notice; if the scope is missing, the
      // reservation load shows the error.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.reservationsApi.apiDhcpScopesScopeIdReservationsGet({
          scopeId: this.scopeId,
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.reservations.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load reservations.'));
    } finally {
      this.loading.set(false);
    }
  }
}
