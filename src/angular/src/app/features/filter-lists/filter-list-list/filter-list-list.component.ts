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

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the Filter Lists page (`/filter-lists`, `filterlists:read`). It lists block
 * and allow lists a page at a time from `GET /api/filter-lists` and links to each list's entries.
 * With `filterlists:write` it offers create, edit, delete and an enable/disable toggle (`PATCH`);
 * without it the toggle is disabled and the rest hidden. Deleting a list also removes its client
 * group assignments.
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

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
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

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method returns the badge color of a list's action: `danger` for Block, `success` for Allow. */
  actionBadgeColor(action: DnsFilterListActionEnum | undefined): string {
    return action === DnsFilterListActionEnum.Allow ? 'success' : 'danger';
  }

  /** This method flips a list's `isDisabled` with a one-field merge patch (RFC 7396). */
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

  /** This method asks for confirmation, then deletes a list. */
  async deleteList(list: FilterListDetail): Promise<void> {
    if (!list.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${list.name}?`,
      message:
        'This permanently deletes the filter list and every entry it contains, and removes it from every client and client group it is assigned to. This cannot be undone.',
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
