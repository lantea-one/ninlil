import { Component, computed, inject, signal } from '@angular/core';
import { CdkTableModule } from '@angular/cdk/table';
import { DatePipe } from '@angular/common';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import {
  BadgeComponent,
  ButtonDirective,
  FormSelectDirective,
  SpinnerComponent,
  TableDirective,
  TooltipDirective,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { firstValueFrom } from 'rxjs';
import {
  ApiClientsGetRequestParams,
  ClientAddressDetail,
  ClientDetail,
  ClientGroupDetail,
  ClientGroupsService,
  ClientsService,
  ClientSummaryDetail,
  DnsClientOriginEnum,
  DnsClientPresenceFilterEnum,
  DnsClientSortOrderEnum,
  InternetProtocolVersionEnum,
} from '../../../api-client';
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
import { ClientSummaryCardsComponent } from '../client-summary-cards/client-summary-cards.component';
import {
  CLIENT_LIST_FILTERS,
  CLIENT_SORT_OPTIONS,
  ClientListFilter,
} from '../shared/client-list-filter.model';
import { DeviceIconOption } from '../shared/device-icon.model';
import { resolveDisplayedDeviceIcon } from '../shared/device-icon.util';
import {
  describeTimeSpan,
  formatRelativeTime,
  startOfLocalDayIso,
} from '../shared/relative-time.util';

/** How many known clients one page of this list shows -- matches every other list page's own precedent. */
const DEFAULT_PAGE_SIZE = 25;

/**
 * This interface pairs one raw client with everything this list derives from it for display: its
 * assigned group's name, the icon it is drawn with, the name it is shown by, and its addresses in
 * display order.
 */
interface ClientRow {
  readonly client: ClientDetail;
  readonly groupName: string | null;
  readonly icon: DeviceIconOption;
  readonly title: string;
  readonly hasDisplayName: boolean;
  readonly lastSeenRelative: string | null;
  readonly addresses: readonly ClientAddressDetail[];
}

/**
 * This component is the Client Management overview: a row of summary cards (total, online,
 * offline, new today, favorites -- `GET /api/clients/summary`) above a device list backed by
 * `GET /api/clients`'s own pushed-down pagination, presence/favorite/new filters, and ordering.
 *
 * <p>Each device is one row regardless of how many identifiers it answers to: every address it
 * owns -- IPv4 and IPv6 alike -- is listed under that one row, with its last-known IP address and
 * the identifier behind it. A row also shows the device's icon (the operator's explicit choice,
 * else the backend's vendor-based suggestion), its hardware vendor (or that its MAC address is
 * randomized), whether it is online now and when it was last seen, and a favorite star that
 * toggles in place.
 *
 * <p>The selected card (the filter) and ordering are mirrored into the URL's query string
 * (`?filter=online&sort=LastSeen`), so a filtered view survives a reload and can be linked to. The
 * "new today" card counts from the viewer's own local midnight, and the list it opens filters from
 * that exact same instant, so the card's count and the list always agree.
 *
 * <p>Since `ClientDetail` carries only a raw, optional `clientGroupId`, this component resolves
 * each client's group name by fetching every client group once and looking each id up in that set
 * -- acceptable at this project's own homelab/small-org target scale.
 */
@Component({
  selector: 'app-client-list',
  imports: [
    RouterLink,
    DatePipe,
    CdkTableModule,
    BadgeComponent,
    ButtonDirective,
    FormSelectDirective,
    SpinnerComponent,
    TableDirective,
    TooltipDirective,
    IconDirective,
    CoreuiPaginationComponent,
    ConfirmDialogComponent,
    EmptyStateComponent,
    ClientSummaryCardsComponent,
  ],
  templateUrl: './client-list.component.html',
  styleUrl: './client-list.component.scss',
})
export class ClientListComponent {
  private readonly clientsApi = inject(ClientsService);
  private readonly clientGroupsApi = inject(ClientGroupsService);
  private readonly notification = inject(NotificationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  /** Exposes the enum to this component's own template, for the origin badge. */
  readonly DnsClientOriginEnum = DnsClientOriginEnum;

  /** Exposes the enum to this component's own template, for each address's protocol badge. */
  readonly InternetProtocolVersionEnum = InternetProtocolVersionEnum;

  /** The orderings the sort control offers. */
  readonly sortOptions = CLIENT_SORT_OPTIONS;

  /** Whether {@link ConfirmDialogComponent} is currently shown for this list's pending delete. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  readonly displayedColumns = [
    'favorite',
    'device',
    'presence',
    'addresses',
    'group',
    'status',
    'actions',
  ];

  readonly rows = signal<ClientRow[]>([]);
  readonly totalCount = signal(0);
  readonly pageSize = signal(DEFAULT_PAGE_SIZE);
  readonly pageIndex = signal(0);
  readonly loading = signal(true);
  readonly errorMessage = signal<string | null>(null);

  /** The registry's headline counts, or `null` while loading or when they could not be fetched. */
  readonly summary = signal<ClientSummaryDetail | null>(null);

  /** The configured online window in plain words, for the toolbar's explanation of "online". */
  readonly onlineWindowText = computed(() => describeTimeSpan(this.summary()?.onlineWindow));

  /** The quick filter currently applied (one per summary card). */
  readonly filter = signal<ClientListFilter>('all');

  /** The ordering currently applied. */
  readonly sort = signal<DnsClientSortOrderEnum>(CLIENT_SORT_OPTIONS[0].value);

  /**
   * The instant "new today" is counted from, fixed for one load so the summary card and a "new"
   * filtered list use exactly the same cutoff.
   */
  private newSinceUtc = startOfLocalDayIso();

  constructor() {
    // Restore a filter/ordering from the URL, ignoring any value this page does not recognize.
    const params = this.route.snapshot.queryParamMap;
    const filter = params.get('filter') as ClientListFilter | null;
    if (filter && CLIENT_LIST_FILTERS.includes(filter)) this.filter.set(filter);
    const sort = params.get('sort') as DnsClientSortOrderEnum | null;
    if (sort && CLIENT_SORT_OPTIONS.some((option) => option.value === sort)) this.sort.set(sort);

    void this.reload();
  }

  /** This method applies a summary card's filter, returning to the first page. */
  async selectFilter(filter: ClientListFilter): Promise<void> {
    if (filter === this.filter()) return;
    this.filter.set(filter);
    this.pageIndex.set(0);
    this.syncUrl();
    await this.loadPage();
  }

  /** This method applies a new ordering from the sort control, returning to the first page. */
  async changeSort(value: string): Promise<void> {
    this.sort.set(value as DnsClientSortOrderEnum);
    this.pageIndex.set(0);
    this.syncUrl();
    await this.loadPage();
  }

  /** This method reloads the current page after a {@link CoreuiPaginationComponent} page/size change. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method re-fetches both the summary counts and the current page, e.g. to pick up new sightings. */
  async reload(): Promise<void> {
    this.newSinceUtc = startOfLocalDayIso();
    await Promise.all([this.loadSummary(), this.loadPage()]);
  }

  /** This method toggles a client's favorite star via a single-field RFC 7396 merge patch. */
  async toggleFavorite(row: ClientRow): Promise<void> {
    if (!row.client.id) return;

    try {
      await firstValueFrom(
        this.clientsApi.apiClientsIdPatch({
          id: row.client.id,
          body: { isFavorite: !row.client.isFavorite },
        }),
      );
      await this.reload();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this client.'));
    }
  }

  /** This method toggles a client's own `isDisabled` flag via a single-field RFC 7396 merge patch. */
  async toggleDisabled(row: ClientRow): Promise<void> {
    if (!row.client.id) return;

    try {
      await firstValueFrom(
        this.clientsApi.apiClientsIdPatch({
          id: row.client.id,
          body: { isDisabled: !row.client.isDisabled },
        }),
      );
      await this.loadPage();
      this.notification.success(row.client.isDisabled ? 'Client enabled.' : 'Client disabled.');
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not update this client.'));
    }
  }

  /** This method confirms, then deletes, a known client. */
  async deleteClient(row: ClientRow): Promise<void> {
    if (!row.client.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${row.title}?`,
      message:
        'This permanently removes this client, and every address it answers to, from the known-client registry. This cannot be undone.',
    });

    if (!confirmed) return;

    try {
      await firstValueFrom(this.clientsApi.apiClientsIdDelete({ id: row.client.id }));
      this.notification.success('Client deleted.');
      await this.reload();
    } catch (error) {
      this.notification.error(extractErrorMessage(error, 'Could not delete this client.'));
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

  /** This method mirrors the current filter/ordering into the URL without adding a history entry. */
  private syncUrl(): void {
    void this.router.navigate([], {
      relativeTo: this.route,
      queryParams: {
        filter: this.filter() === 'all' ? null : this.filter(),
        sort: this.sort() === CLIENT_SORT_OPTIONS[0].value ? null : this.sort(),
      },
      queryParamsHandling: 'merge',
      replaceUrl: true,
    });
  }

  /** This method translates the current filter/ordering/page into `GET /api/clients` parameters. */
  private buildListRequest(): ApiClientsGetRequestParams {
    const request: ApiClientsGetRequestParams = {
      skip: this.pageIndex() * this.pageSize(),
      take: this.pageSize(),
      sort: this.sort(),
    };

    switch (this.filter()) {
      case 'online':
        request.presence = DnsClientPresenceFilterEnum.Online;
        break;
      case 'offline':
        request.presence = DnsClientPresenceFilterEnum.Offline;
        break;
      case 'new':
        request.createdSinceUtc = this.newSinceUtc;
        break;
      case 'favorites':
        request.isFavorite = true;
        break;
    }

    return request;
  }

  /** This method fetches the summary counts; a failure leaves the cards showing dashes, never blocking the list. */
  private async loadSummary(): Promise<void> {
    try {
      this.summary.set(
        await firstValueFrom(
          this.clientsApi.apiClientsSummaryGet({ newSinceUtc: this.newSinceUtc }),
        ),
      );
    } catch {
      this.summary.set(null);
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [page, allGroups] = await Promise.all([
        firstValueFrom(this.clientsApi.apiClientsGet(this.buildListRequest())),
        firstValueFrom(this.clientGroupsApi.apiClientGroupsGet({ skip: 0, take: 500 })),
      ]);

      const groupNamesById = new Map(
        (allGroups.items ?? []).map((group: ClientGroupDetail) => [
          group.id,
          group.name ?? group.id ?? '',
        ]),
      );
      const now = new Date();

      this.rows.set(
        (page.items ?? []).map((client) => {
          // IPv4 addresses first, then IPv6, each in the order the device gained them.
          const addresses = [...(client.addresses ?? [])].sort(
            (left, right) =>
              Number(left.protocolVersion === InternetProtocolVersionEnum.Ipv6) -
              Number(right.protocolVersion === InternetProtocolVersionEnum.Ipv6),
          );
          const hasDisplayName = !!client.displayName;

          return {
            client,
            groupName: client.clientGroupId
              ? (groupNamesById.get(client.clientGroupId) ?? '(unknown group)')
              : null,
            icon: resolveDisplayedDeviceIcon(client.deviceIconKey, client.suggestedDeviceIconKey),
            // An unnamed device is shown by its most recent IP address, else its first identifier.
            title:
              client.displayName ||
              addresses.find((address) => address.address)?.address ||
              addresses[0]?.clientIdentifier ||
              'Unnamed device',
            hasDisplayName,
            lastSeenRelative: formatRelativeTime(client.lastSeenUtc, now),
            addresses,
          };
        }),
      );
      this.totalCount.set(page.totalCount ?? 0);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load clients.'));
    } finally {
      this.loading.set(false);
    }
  }
}
