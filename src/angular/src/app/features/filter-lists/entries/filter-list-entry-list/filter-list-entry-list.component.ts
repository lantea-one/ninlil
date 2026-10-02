import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { ActivatedRoute, RouterLink } from '@angular/router';
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
  FilterListEntryDetail,
  FilterListEntriesService,
  FilterListsService,
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
import { ClusterSyncInfoComponent } from '../../../../shared/cluster-sync-info/cluster-sync-info.component';
import { isSubscribedList } from '../../filter-list.model';
import { IfPermissionDirective } from '../../../../shared/if-permission/if-permission.directive';

/** The page size the list starts with, as on the other list pages. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is a filter list's entries page (`/filter-lists/:listId/entries`,
 * `filterlists:read`). It lists entries a page at a time from
 * `GET /api/filter-lists/{listId}/entries` and, with `filterlists:write`, links to add and edit
 * and deletes one (`DELETE .../entries/{id}`).
 *
 * <p>A subscribed list's entries come from its scheduled refresh, and the backend answers 409 to
 * a manual change. Once the list's source loads, the page disables every add, edit and delete
 * control for such a list and says why; its entries can still be browsed.
 */
@Component({
  selector: 'app-filter-list-entry-list',
  imports: [
    IfPermissionDirective,
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
    ClusterSyncInfoComponent,
    EmptyStateComponent,
  ],
  templateUrl: './filter-list-entry-list.component.html',
  styleUrl: './filter-list-entry-list.component.scss',
})
export class FilterListEntryListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly entriesApi = inject(FilterListEntriesService);
  private readonly filterListsApi = inject(FilterListsService);
  private readonly notification = inject(NotificationService);

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly listId = this.route.snapshot.paramMap.get('listId') ?? '';

  readonly displayedColumns = ['value', 'kind', 'comment', 'sync', 'actions'];

  readonly listName = signal<string | null>(null);
  /** Whether the list is subscribed; then every write control is disabled. */
  readonly isReadOnly = signal(false);
  readonly entries = signal<FilterListEntryDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadOwningList();
    void this.loadPage();
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method asks for confirmation, then deletes an entry; it does nothing for a subscribed list. */
  async deleteEntry(entry: FilterListEntryDetail): Promise<void> {
    if (!entry.id || this.isReadOnly()) return;

    const confirmed = await this.confirm({
      title: 'Delete this entry?',
      message: `This permanently removes '${entry.value}' from the list. This cannot be undone.`,
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(
        this.entriesApi.apiFilterListsListIdEntriesIdDelete({ listId: this.listId, id: entry.id }),
      );
      this.notification.success('Entry deleted.');
      await this.loadPage();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this entry.'));
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

  private async loadOwningList(): Promise<void> {
    try {
      const list = await firstValueFrom(
        this.filterListsApi.apiFilterListsIdGet({ id: this.listId }),
      );
      this.listName.set(list.name ?? null);
      this.isReadOnly.set(isSubscribedList(list.source));
    } catch {
      // If the list is missing, the entry load shows the error. Without the source, the write
      // controls stay enabled and the backend's 409 still applies.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.entriesApi.apiFilterListsListIdEntriesGet({
          listId: this.listId,
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.entries.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load entries.'));
    } finally {
      this.loading.set(false);
    }
  }
}
