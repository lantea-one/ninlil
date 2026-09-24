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

/** How many activity entries one page of this list shows -- matches `QueryLogListComponent`'s own default. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This component shows one known client's own slice of the persistent query log, backed by
 * `GET /api/clients/{clientId}/activity`'s own real `skip`/`take` pagination -- the client-level
 * analogue of `QueryLogListComponent`, reusing the exact same column set/table conventions rather
 * than inventing a second, parallel table. Unlike that component, this one has no filter form and
 * no "clear log" action at all: it is scoped to exactly one client by its own route, and a log
 * entry is server-written history, never an operator-editable resource (the same read-only,
 * "server-written data" posture `QueryLogListComponent`'s own doc comment already establishes).
 * `DnsQueryLogEntryDetail.clientIdentifier`/`clientProtocolVersion` are deliberately not shown as
 * their own columns here -- every row is already known to belong to this one client by
 * construction, so repeating its own identity on every row would add noise, not information.
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

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method reloads the current page from the server -- exposed for a manual "refresh" action. */
  async refresh(): Promise<void> {
    await this.loadPage();
  }

  private async loadClientLabel(): Promise<void> {
    try {
      const client = await firstValueFrom(this.clientsApi.apiClientsIdGet({ id: this.clientId }));
      this.clientLabel.set(client.displayName ?? client.addresses?.[0]?.clientIdentifier ?? null);
    } catch {
      // A missing client label is only cosmetic here -- the activity load below surfaces its own
      // real error if the owning client itself doesn't exist.
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
