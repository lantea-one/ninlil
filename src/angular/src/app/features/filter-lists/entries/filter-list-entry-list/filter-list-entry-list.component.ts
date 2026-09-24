import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { ActivatedRoute, RouterLink } from '@angular/router';
import {
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

/** How many entries one page of this list shows -- matches every other list page's own precedent. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component lists every entry stored in one filter list, backed by
 * `GET /api/filter-lists/{listId}/entries`'s own real `skip`/`take` pagination.
 *
 * Surfaces `FilterListEntriesController`'s own real `409 Conflict` rule as genuine UI state,
 * rather than letting an operator attempt a write only to be rejected after the fact: once the
 * owning list's own `source` is loaded, every add/edit/delete control on this page is disabled
 * outright (with a tooltip/banner explaining that the list's entries are managed by its own
 * scheduled refresh) whenever {@link isSubscribedList} reports `true`. `GET`/`List` are never
 * restricted -- inspecting a subscribed list's own currently-fetched entries is always useful.
 */
@Component({
  selector: 'app-filter-list-entry-list',
  imports: [
    IfPermissionDirective,
    RouterLink,
    CdkTableModule,
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

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending entry delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly listId = this.route.snapshot.paramMap.get('listId') ?? '';

  readonly displayedColumns = ['value', 'kind', 'comment', 'sync', 'actions'];

  readonly listName = signal<string | null>(null);
  /** Whether the owning list is subscribed -- when `true`, every write control on this page is disabled. */
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

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method confirms, then deletes, an entry -- blocked entirely for a subscribed list. */
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

  private async loadOwningList(): Promise<void> {
    try {
      const list = await firstValueFrom(
        this.filterListsApi.apiFilterListsIdGet({ id: this.listId }),
      );
      this.listName.set(list.name ?? null);
      this.isReadOnly.set(isSubscribedList(list.source));
    } catch {
      // A missing list name/source is only cosmetic here -- the entry list load below surfaces
      // its own real error if the owning list itself doesn't exist.
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
