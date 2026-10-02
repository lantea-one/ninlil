import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { RouterLink } from '@angular/router';
import {
  BadgeComponent,
  ButtonDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  DhcpScopeDetail,
  DhcpScopeStatisticsDetail,
  DhcpScopesService,
} from '../../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../../shared/confirm-dialog/confirm-dialog.component';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../../shared/if-permission/has-permission.pipe';

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the DHCP Scopes page (`/dhcp-scopes`, `dhcpscopes:read`). It lists scopes a
 * page at a time from `GET /api/dhcp-scopes` and links to each scope's reservations. With
 * `dhcpscopes:write` it offers create, edit (which includes the custom options), delete and an
 * enable/disable toggle (`PATCH`); without it the toggle is disabled and the rest hidden.
 * Deleting a scope also deletes its reservations and custom options, but not its leases.
 *
 * <p>Each row shows the scope's utilization from `GET /api/dhcp-scopes/{id}/statistics`, fetched
 * for every scope on the page in parallel after the page loads. A failed fetch leaves only that
 * row's utilization blank.
 */
@Component({
  selector: 'app-dhcp-scope-list',
  imports: [
    IfPermissionDirective,
    HasPermissionPipe,
    RouterLink,
    CdkTableModule,
    BadgeComponent,
    ButtonDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    EmptyStateComponent,
  ],
  templateUrl: './dhcp-scope-list.component.html',
  styleUrl: './dhcp-scope-list.component.scss',
})
export class DhcpScopeListComponent {
  private readonly scopesApi = inject(DhcpScopesService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = [
    'name',
    'protocolVersion',
    'kind',
    'range',
    'utilization',
    'status',
    'actions',
  ];

  readonly scopes = signal<DhcpScopeDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** The utilization of the scopes on this page, by scope id. */
  readonly statisticsByScopeId = signal<ReadonlyMap<string, DhcpScopeStatisticsDetail>>(new Map());

  /** Whether the per-scope statistics are still loading. */
  readonly statisticsLoading = signal(true);

  constructor() {
    void this.loadPage();
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method flips a scope's `isDisabled` with a one-field merge patch (RFC 7396). */
  async toggleDisabled(scope: DhcpScopeDetail): Promise<void> {
    if (!scope.id) return;

    try {
      await firstValueFrom(
        this.scopesApi.apiDhcpScopesIdPatch({
          id: scope.id,
          body: { isDisabled: !scope.isDisabled },
        }),
      );
      await this.loadPage();
      this.notification.success(scope.isDisabled ? 'Scope enabled.' : 'Scope disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this scope.'));
    }
  }

  /** This method asks for confirmation, then deletes a scope with its reservations and custom options. */
  async deleteScope(scope: DhcpScopeDetail): Promise<void> {
    if (!scope.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${scope.name}?`,
      message:
        'This permanently deletes the scope, every reservation it contains, and every custom option it contains. Any lease already recorded against it is left untouched. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.scopesApi.apiDhcpScopesIdDelete({ id: scope.id }));
      this.notification.success('Scope deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this scope.'));
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

  /**
   * This method formats a scope's utilization as "N leased, A available of T", adding the offered
   * (awaiting the client's confirmation) and reserved counts only when non-zero. Totals arrive as
   * decimal strings and are formatted as `bigint`, because an IPv6 pool (2^64 for a /64) exceeds
   * `Number.MAX_SAFE_INTEGER`.
   */
  utilizationText(statistics: DhcpScopeStatisticsDetail): string {
    const total = BigInt(statistics.totalSlots ?? '0');
    const available = BigInt(statistics.availableSlots ?? '0');
    const parts = [`${statistics.leasedCount ?? 0} leased`];
    if (statistics.offeredCount) parts.push(`${statistics.offeredCount} offered`);
    if (statistics.reservedCount) parts.push(`${statistics.reservedCount} reserved`);
    parts.push(`${available.toLocaleString()} available of ${total.toLocaleString()}`);
    return parts.join(', ');
  }

  /**
   * This method picks the utilization badge color: `secondary` for an empty range, `danger` under
   * 5% free, `warning` under 20% free, else `success`. It compares with `bigint` arithmetic
   * (`available * 20n < total`) so IPv6 pool sizes lose no precision.
   */
  utilizationColor(
    statistics: DhcpScopeStatisticsDetail,
  ): 'secondary' | 'danger' | 'warning' | 'success' {
    const total = BigInt(statistics.totalSlots ?? '0');
    if (total === 0n) return 'secondary';

    const available = BigInt(statistics.availableSlots ?? '0');
    if (available * 20n < total) return 'danger';
    if (available * 5n < total) return 'warning';
    return 'success';
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.scopesApi.apiDhcpScopesGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.scopes.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load DHCP scopes.'));
    } finally {
      this.loading.set(false);
    }

    await this.loadStatistics();
  }

  /**
   * This method fetches the utilization of every scope on the page in parallel. A failed fetch
   * leaves out that scope.
   */
  private async loadStatistics(): Promise<void> {
    this.statisticsLoading.set(true);

    const entries = await Promise.all(
      this.scopes().map(
        async (scope): Promise<readonly [string, DhcpScopeStatisticsDetail] | null> => {
          if (!scope.id) return null;

          try {
            const statistics = await firstValueFrom(
              this.scopesApi.apiDhcpScopesIdStatisticsGet({ id: scope.id }),
            );
            return [scope.id, statistics] as const;
          } catch {
            return null;
          }
        },
      ),
    );

    this.statisticsByScopeId.set(
      new Map(
        entries.filter(
          (entry): entry is readonly [string, DhcpScopeStatisticsDetail] => entry !== null,
        ),
      ),
    );
    this.statisticsLoading.set(false);
  }
}
