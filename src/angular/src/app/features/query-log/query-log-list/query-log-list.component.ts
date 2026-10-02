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
  FormSelectDirective,
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
import {
  EMPTY_QUERY_LOG_FILTERS,
  buildQueryLogSearchParams,
  clientIdentifierKind,
} from '../query-log.model';

const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is the Query Log page (`/query-log`, `querylog:read`). It searches the stored
 * query log that `DnsQueryLogSubscriber` writes for every answered query, unlike the dashboard's
 * live feed, which is capped and resets on reload. Log entries are written by the server, so the
 * page has no create or edit; its one write action, "Clear log" (`querylog:write`), is hidden
 * without that permission.
 *
 * <p>The log can hold millions of rows, so every filter and the paging go to the server
 * (`GET /api/query-log`) and a filter change runs a new search. The table is a CDK table styled
 * with CoreUI's `cTable`, with {@link CoreuiPaginationComponent}, because CoreUI's free tier has
 * no data grid.
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
    FormSelectDirective,
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

  /** Whether the clear-log confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = [
    'occurredAtUtc',
    'clientAddress',
    'clientIdentifier',
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

  /** Whether the user holds `querylog:write`; "Clear log" shows only then. */
  readonly canClear = this.authService.hasPermission('querylog:write');

  constructor() {
    void this.search();
  }

  /** This method searches again from the first page with the current filters. */
  async search(): Promise<void> {
    this.pageIndex.set(0);
    await this.loadPage();
  }

  /** Re-exported for the template, which cannot import a plain module function directly. */
  readonly clientIdentifierKind = clientIdentifierKind;

  /** This method narrows the log to one row's client identity, the way the per-client page does. */
  async filterByClientIdentifier(entry: DnsQueryLogEntryDetail): Promise<void> {
    if (!entry.clientIdentifier) return;

    this.filterForm.patchValue({
      clientIdentifier: entry.clientIdentifier,
      clientProtocolVersion: entry.clientProtocolVersion ?? '',
    });
    await this.search();
  }

  /** This method clears every filter field, then re-searches. */
  async resetFilters(): Promise<void> {
    this.filterForm.reset({ ...EMPTY_QUERY_LOG_FILTERS });
    await this.search();
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /**
   * This method asks for confirmation, then deletes the whole query log
   * (`DELETE /api/query-log`). This is separate from the scheduled retention cleanup.
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
