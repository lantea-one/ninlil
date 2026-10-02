import { Component, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { ButtonDirective, SpinnerComponent, TableDirective } from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  ClientActivityService,
  ClientsService,
  DnsQueryLogEntryDetail,
} from '../../../../api-client';
import {
  CoreuiPaginationComponent,
  PageChangeEvent,
} from '../../../../shared/coreui-pagination/coreui-pagination.component';
import { EmptyStateComponent } from '../../../../shared/empty-state/empty-state.component';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';

/** The page size the list starts with, the same as the query log page. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component is a client's activity page (`/clients/:clientId/activity`). It shows the
 * client's entries from the query log a page at a time, from
 * `GET /api/clients/{clientId}/activity`, which needs `querylog:read` (the route checks it on top
 * of `clients:read`). It uses the query log page's columns, without its filters or "Clear log",
 * and is read-only.
 *
 * <p>The client identifier and protocol version columns are left out, since every row belongs to
 * the same client.
 */
@Component({
  selector: 'app-client-activity-list',
  imports: [
    DatePipe,
    RouterLink,
    CdkTableModule,
    ButtonDirective,
    SpinnerComponent,
    TableDirective,
    IconDirective,
    CoreuiPaginationComponent,
    EmptyStateComponent,
  ],
  templateUrl: './client-activity-list.component.html',
  styleUrl: './client-activity-list.component.scss',
})
export class ClientActivityListComponent {
  private readonly route = inject(ActivatedRoute);
  private readonly activityApi = inject(ClientActivityService);
  private readonly clientsApi = inject(ClientsService);

  readonly clientId = this.route.snapshot.paramMap.get('clientId') ?? '';

  readonly displayedColumns = [
    'occurredAtUtc',
    'clientAddress',
    'protocol',
    'queriedName',
    'queriedType',
    'responseCode',
    'answerCount',
  ];

  readonly clientLabel = signal<string | null>(null);
  readonly entries = signal<DnsQueryLogEntryDetail[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  constructor() {
    void this.loadClientLabel();
    void this.loadPage();
  }

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method reloads the current page, for the Refresh button. */
  async refresh(): Promise<void> {
    await this.loadPage();
  }

  private async loadClientLabel(): Promise<void> {
    try {
      const client = await firstValueFrom(this.clientsApi.apiClientsIdGet({ id: this.clientId }));
      this.clientLabel.set(client.displayName ?? client.addresses?.[0]?.clientIdentifier ?? null);
    } catch {
      // The label is only for the heading; if the client is missing, loadPage() shows the error.
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const page = await firstValueFrom(
        this.activityApi.apiClientsClientIdActivityGet({
          clientId: this.clientId,
          skip: this.pageIndex() * this.pageSize(),
          take: this.pageSize(),
        }),
      );
      this.entries.set(page.items ?? []);
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this client’s activity.'));
    } finally {
      this.loading.set(false);
    }
  }
}
