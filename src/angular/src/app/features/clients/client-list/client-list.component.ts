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
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import { HasPermissionPipe } from '../../../shared/if-permission/has-permission.pipe';
import { AuthService } from '../../../core/auth/auth.service';

/** The page size the list starts with, as on the other list pages. */
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
 * This component is the Clients page (`/clients`, `clients:read`). Summary cards (total, online,
 * offline, new today, favorites, from `GET /api/clients/summary`) sit above the device list from
 * `GET /api/clients`, which the server pages, filters (presence, favorites, new) and sorts.
 *
 * <p>Each device is one row, however many identifiers it has: every IPv4 and IPv6 address is
 * listed in that row with its last IP address and identifier. A row also shows the device icon
 * (the operator's choice, else the backend's vendor-based suggestion), the vendor or a randomized
 * MAC note, whether it is online and when it was last seen, and a favorite star. With
 * `clients:write` the star and the enable/disable button work in place (`PATCH /api/clients/{id}`)
 * and edit and delete are offered; without it they are disabled or hidden.
 *
 * <p>The selected card and the sort are kept in the query string (`?filter=online&sort=LastSeen`),
 * so a filtered view survives a reload and can be linked to. "New today" counts from the viewer's
 * local midnight, and the filtered list uses the same instant, so the count and the list agree.
 *
 * <p>A client carries only a `clientGroupId`, so the page loads the client groups once to show
 * names. Without `clientgroups:read` the Group column is left out.
 */
@Component({
  selector: 'app-client-list',
  imports: [
    IfPermissionDirective,
    HasPermissionPipe,
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
  private readonly authService = inject(AuthService);
  private readonly notification = inject(NotificationService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);

  /** The enum, exposed to the template for the origin badge. */
  readonly DnsClientOriginEnum = DnsClientOriginEnum;

  /** The enum, exposed to the template for each address's protocol badge. */
  readonly InternetProtocolVersionEnum = InternetProtocolVersionEnum;

  /** The orderings the sort control offers. */
  readonly sortOptions = CLIENT_SORT_OPTIONS;

  /** Whether the delete confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /**
   * Whether client group names couldn't be loaded (usually no `clientgroups:read`). The Group
   * column is left out then, rather than showing every grouped client as "(unknown group)".
   */
  readonly groupsUnavailable = signal(false);

  readonly displayedColumns = computed(() => [
    'favorite',
    'device',
    'presence',
    'addresses',
    ...(this.groupsUnavailable() ? [] : ['group']),
    'status',
    'actions',
  ]);

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
   * The instant "new today" counts from. It is fixed per reload, so the summary card and the
   * "new" filter use the same cutoff.
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

  /** This method loads the page the pagination bar asks for. */
  async onPageChange(event: PageChangeEvent): Promise<void> {
    this.pageIndex.set(event.pageIndex);
    this.pageSize.set(event.pageSize);
    await this.loadPage();
  }

  /** This method reloads the summary counts and the current page, e.g. to pick up new sightings. */
  async reload(): Promise<void> {
    this.newSinceUtc = startOfLocalDayIso();
    await Promise.all([this.loadSummary(), this.loadPage()]);
  }

  /** This method flips a client's favorite star with a one-field merge patch (RFC 7396). */
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

  /** This method flips a client's `isDisabled` with a one-field merge patch (RFC 7396). */
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

  /** This method asks for confirmation, then deletes a client. */
  async deleteClient(row: ClientRow): Promise<void> {
    if (!row.client.id) return;

    const confirmed = await this.confirm({
      title: `Delete ${row.title}?`,
      message:
        'This permanently removes this client, its addresses, schedules, filter-list assignments and Safe Search overrides. This cannot be undone.',
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

  /** This method hides the confirmation dialog and settles the pending {@link confirm} promise. */
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

  /** This method fetches the summary counts. On failure the cards show dashes and the list still loads. */
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

  /**
   * This method fetches the client groups (up to 500) to name each client's group. It returns
   * `null` without `clientgroups:read` (the API would refuse) or when the request fails; the
   * client list loads either way.
   */
  private async loadGroups(): Promise<ClientGroupDetail[] | null> {
    if (!this.authService.hasPermission('clientgroups:read')) return null;

    try {
      const allGroups = await firstValueFrom(
        this.clientGroupsApi.apiClientGroupsGet({ skip: 0, take: 500 }),
      );
      return allGroups.items ?? [];
    } catch {
      return null;
    }
  }

  private async loadPage(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const [page, allGroups] = await Promise.all([
        firstValueFrom(this.clientsApi.apiClientsGet(this.buildListRequest())),
        this.loadGroups(),
      ]);

      this.groupsUnavailable.set(allGroups === null);
      const groupNamesById = new Map(
        (allGroups ?? []).map((group: ClientGroupDetail) => [
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
            groupName:
              client.clientGroupId && allGroups !== null
                ? (groupNamesById.get(client.clientGroupId) ?? '(unknown group)')
                : null,
            icon: resolveDisplayedDeviceIcon(client.deviceIconKey, client.suggestedDeviceIconKey),
            // An unnamed device is shown by its first known IP address (IPv4 first), else its
            // first identifier.
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
