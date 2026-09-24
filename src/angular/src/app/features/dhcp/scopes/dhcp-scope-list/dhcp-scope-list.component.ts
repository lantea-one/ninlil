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

/** How many DHCP scopes one page of this list shows -- matches every other list page's own precedent. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every DHCP scope (address pool) this server manages, backed by
 * `GET /api/dhcp-scopes`'s own real `skip`/`take` pagination, with actions to manage a scope's own
 * static reservations and custom options, edit its metadata, and delete it (behind a confirmation
 * dialog, since `DhcpScopesController.Delete` also removes every reservation and custom option the
 * scope holds) -- the same "one parent, several nested child collections" shape
 * `ClientGroupListComponent` already establishes for a client group's own networks and list
 * assignments.
 *
 * <p>Each row also shows a compact, real "Utilization" summary -- this closes the "no utilization
 * view anywhere" gap: `DhcpScopesController.Statistics` (`GET /api/dhcp-scopes/{id}/statistics`)
 * is fetched once per scope on the current page, in parallel, right after the page itself loads,
 * so the figures shown are always freshly computed against this scope's own real, current
 * lease/reservation data -- never a cached or approximate one. A failed per-scope fetch (a
 * transient error, never expected in practice) simply leaves that one row's own utilization blank
 * rather than failing the whole list.
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

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
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

  /** This page's own current scopes' real, freshly-computed utilization, keyed by scope identifier. */
  readonly statisticsByScopeId = signal<ReadonlyMap<string, DhcpScopeStatisticsDetail>>(new Map());

  /** Whether this page's own per-scope statistics fetch is still in flight. */
  readonly statisticsLoading = signal(true);

  constructor() {
    void this.loadPage();
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method toggles a scope's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
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

  /** This method confirms, then deletes, a DHCP scope -- including every reservation and custom option it holds. */
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

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  /**
   * This method formats one scope's own utilization -- `${leased} leased, ${available}
   * available of ${total}`, with the offered (awaiting the client's confirmation) and reserved
   * counts added only when there are any, so a typical row stays short -- with every figure
   * grouped via `bigint.toLocaleString()`, which
   * formats losslessly at any size (an IPv6 `AddressAssignment` scope's own total can genuinely
   * exceed `Number.MAX_SAFE_INTEGER` by many orders of magnitude, e.g. 2^64 for an ordinary
   * `/64`) since `DhcpScopeStatisticsDetail.totalSlots`/`availableSlots` are always decimal
   * strings for exactly this reason.
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
   * This method picks a badge color reflecting how full a scope's own pool currently is --
   * `secondary` for an unset/broken range (nothing to report at all), `danger` under 5% free,
   * `warning` under 20% free, `success` otherwise. Compared via exact `bigint` arithmetic
   * (`available * 20n < total` rather than a floating-point `available / total < 0.05`) so this
   * never loses precision at the astronomical scale an IPv6 pool's own total can reach.
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
   * This method fetches every currently-listed scope's own real, freshly-computed utilization, in
   * parallel, right after {@link loadPage} loads that page -- see this class's own doc comment for
   * why a failed per-scope fetch never fails the whole list.
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
