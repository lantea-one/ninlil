import { Component, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
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
import { DnsFilterListActionEnum, FilterListDetail, FilterListsService } from '../../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../../shared/confirm-dialog/confirm-dialog.component';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClusterSyncInfoComponent } from '../../../shared/cluster-sync-info/cluster-sync-info.component';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../shared/if-permission/has-permission.pipe';

/** How many filter lists one page of this list shows -- matches every other list page's own precedent. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every block/allow list this server knows about, backed by
 * `GET /api/filter-lists`'s own real `skip`/`take` pagination, with actions to manage a list's
 * own entries, edit its metadata, toggle it disabled (via `PATCH`), and delete it (behind a
 * confirmation dialog, since `FilterListsController.Delete` also removes any client-group
 * assignment still referencing it -- a fact surfaced directly in the confirmation message).
 */
@Component({
  selector: 'app-filter-list-list',
  imports: [
    DatePipe,
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
    ClusterSyncInfoComponent,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    EmptyStateComponent,
  ],
  templateUrl: './filter-list-list.component.html',
  styleUrl: './filter-list-list.component.scss',
})
export class FilterListListComponent {
  private readonly filterListsApi = inject(FilterListsService);
  private readonly notification = inject(NotificationService);

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = ['name', 'kind', 'action', 'source', 'disabled', 'sync', 'actions'];

  readonly lists = signal<FilterListDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadPage();
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method maps a list's own `action` to a badge color -- `Block` reads as a warning-adjacent danger, `Allow` as a positive success. */
  actionBadgeColor(action: DnsFilterListActionEnum | undefined): string {
    return action === DnsFilterListActionEnum.Allow ? 'success' : 'danger';
  }

  /** This method toggles a filter list's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
  async toggleDisabled(list: FilterListDetail): Promise<void> {
    if (!list.id) return;

    try {
      await firstValueFrom(
        this.filterListsApi.apiFilterListsIdPatch({
          id: list.id,
          body: { isDisabled: !list.isDisabled },
        }),
      );
      await this.loadPage();
      this.notification.success(list.isDisabled ? 'Filter list enabled.' : 'Filter list disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this filter list.'));
    }
  }

  /** This method confirms, then deletes, a filter list -- including any client-group assignment referencing it. */
  async deleteList(list: FilterListDetail): Promise<void> {
    if (!list.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${list.name}?`,
      message:
        'This permanently deletes the filter list, every entry it contains, and removes it from any client group it is currently assigned to. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.filterListsApi.apiFilterListsIdDelete({ id: list.id }));
      this.notification.success('Filter list deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this filter list.'));
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

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.filterListsApi.apiFilterListsGet({
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.lists.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load filter lists.'));
    } finally {
      this.loading.set(false);
    }
  }
}
