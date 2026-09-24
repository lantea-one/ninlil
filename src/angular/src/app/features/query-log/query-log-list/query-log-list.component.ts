import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { DatePipe } from '@angular/common';
import {
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import { DnsQueryLogEntryDetail, QueryLogService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
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
import { EMPTY_QUERY_LOG_FILTERS, buildQueryLogSearchParams } from '../query-log.model';

const DEFAULT_PAGE_SIZE = 25;

/**
 * This component searches and displays the persistent query log
 * `Server.QueryLog.DnsQueryLogSubscriber` writes to for every answered query -- a real,
 * searchable historical record, unlike the dashboard's own live SignalR feed, which is capped and
 * resets on every reload. Unlike every other list component in this workspace, this one has no
 * "new"/"edit" routes at all (a log entry is written by the server itself, never authored by an
 * operator) -- its only write action is clearing the log outright, which this component gates on
 * the `querylog:write` permission directly (mirroring `SettingsShellComponent`'s own precedent
 * for hiding, not merely disabling, a control the current session cannot actually use), since
 * there is no dedicated route of its own for a route guard to protect the way every other
 * feature's own write actions are protected.
 *
 * Every filter this component's own form exposes is pushed all the way down into
 * `GET /api/query-log`'s own real, server-side `skip`/`take` pagination and search criteria --
 * unlike this project's own much lower-cardinality entities, where an in-memory list is an
 * accepted tradeoff, a query log can genuinely reach millions of rows, so a filter change always
 * re-issues a fresh search rather than filtering an already-fetched page client-side. The table
 * itself uses Angular CDK's `CdkTableModule` styled with CoreUI's `cTable` classes, plus
 * {@link CoreuiPaginationComponent} for page chrome -- the confirmed fallback for a
 * sortable/paginated/filterable data grid, since CoreUI for Angular's free tier gates a real
 * data-grid component behind its commercial tier.
 */
@Component({
  selector: 'app-query-log-list',
  imports: [
    DatePipe,
    ReactiveFormsModule,
    CdkTableModule,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    EmptyStateComponent,
  ],
  templateUrl: './query-log-list.component.html',
  styleUrl: './query-log-list.component.scss',
})
export class QueryLogListComponent {
  private readonly queryLogApi = inject(QueryLogService);
  private readonly authService = inject(AuthService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** Whether {@link ConfirmDialogComponent} is currently shown for the pending clear-log action. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = [
    'occurredAtUtc',
    'clientAddress',
    'protocol',
    'queriedName',
    'queriedType',
    'responseCode',
    'answerCount',
  ];

  readonly filterForm = this.formBuilder.group({ ...EMPTY_QUERY_LOG_FILTERS });

  readonly entries = signal<DnsQueryLogEntryDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly clearing = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether the current session holds `querylog:write`, gating the "Clear log" action. */
  readonly canClear = this.authService.hasPermission('querylog:write');

  constructor() {
    void this.search();
  }

  /** This method re-runs the search from the first page, using the filter form's own current values. */
  async search(): Promise<void> {
    this.pageIndex.set(0);
    await this.loadPage();
  }

  /** This method clears every filter field, then re-searches. */
  async resetFilters(): Promise<void> {
    this.filterForm.reset({ ...EMPTY_QUERY_LOG_FILTERS });
    await this.search();
  }

  /** This method reloads the current search's own page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /**
   * This method confirms, then permanently clears the entire query log -- an explicit,
   * operator-triggered action distinct from the automatic, retention-driven cleanup this server
   * already runs on its own schedule.
   */
  async clearLog(): Promise<void> {
    const confirmed = await this.confirm({
      title: 'Clear the entire query log?',
      message:
        'This permanently deletes every logged query. This cannot be undone. Newly answered queries will still be logged going forward.',
      confirmLabel: 'Clear log',
    });

    if (!confirmed) return;

    this.clearing.set(true);

    try {
      const result = await firstValueFrom(this.queryLogApi.apiQueryLogDelete());
      this.notification.success(`Deleted ${result.deletedCount ?? 0} log entries.`);
      await this.search();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not clear the query log.'));
    } finally {
      this.clearing.set(false);
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
        this.queryLogApi.apiQueryLogGet(
          buildQueryLogSearchParams(
            this.filterForm.getRawValue(),
            this.pageIndex() * this.pageSize(),
            this.pageSize(),
          ),
        ),
      );
      this.entries.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load the query log.'));
    } finally {
      this.loading.set(false);
    }
  }
}
