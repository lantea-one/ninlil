import { Injectable, inject } from '@angular/core';
import { Observable, firstValueFrom } from 'rxjs';
import {
  FilterListsService as FilterListsApiService,
  ForwardersService as ForwardersApiService,
  NinlilWebService,
  StatusResponse,
  WebhooksService as WebhooksApiService,
  ZonesService as ZonesApiService,
} from '../../api-client';
import { navItemIcon } from '../../layout/app-shell/nav-item.model';
import { QuickStat } from './dashboard.model';

/*
 * The four tile icons below are read when this file loads, so a wrong nav path fails every spec at
 * once and `fetchQuickStats` never has to throw.
 */

/** This constant is the Zones tile's icon: the Zones menu entry's. */
const ZONES_ICON = navItemIcon('zones');

/** This constant is the Forwarders tile's icon: the Forwarders menu entry's. */
const FORWARDERS_ICON = navItemIcon('forwarders');

/** This constant is the Filter Lists tile's icon: the Filter Lists menu entry's. */
const FILTER_LISTS_ICON = navItemIcon('filter-lists');

/** This constant is the Webhooks tile's icon: the Webhooks menu entry's. */
const WEBHOOKS_ICON = navItemIcon('webhooks');

/**
 * This interface is the one field the count needs from every generated `*PagedResult` model, so
 * `fetchCount` can take any of the four responses without importing each model.
 */
interface CountedPage {
  totalCount?: number;
}

/**
 * This service fetches the Dashboard's data: the `GET /status` health response and four quick-stat
 * counts (zones, forwarders, filter lists and webhooks).
 */
@Injectable({ providedIn: 'root' })
export class DashboardStatsService {
  private readonly statusApi = inject(NinlilWebService);
  private readonly zonesApi = inject(ZonesApiService);
  private readonly forwardersApi = inject(ForwardersApiService);
  private readonly filterListsApi = inject(FilterListsApiService);
  private readonly webhooksApi = inject(WebhooksApiService);

  /** This method fetches `GET /status`. Per-component detail is included only with `status:read`. */
  async fetchStatus(): Promise<StatusResponse> {
    return firstValueFrom(this.statusApi.statusGet());
  }

  /**
   * This method requests one `skip=0&take=1` page from each of the four list endpoints and reads
   * `totalCount`. Each request catches its error and yields `count: null`, so a user without
   * one resource's read permission still sees the other tiles. It never rejects. Each tile's icon
   * is the one of the menu entry of the same page.
   */
  async fetchQuickStats(): Promise<QuickStat[]> {
    return Promise.all([
      this.fetchCount('Zones', ZONES_ICON, () => this.zonesApi.apiZonesGet({ skip: 0, take: 1 })),
      this.fetchCount('Forwarders', FORWARDERS_ICON, () =>
        this.forwardersApi.apiForwardersGet({ skip: 0, take: 1 }),
      ),
      this.fetchCount('Filter Lists', FILTER_LISTS_ICON, () =>
        this.filterListsApi.apiFilterListsGet({ skip: 0, take: 1 }),
      ),
      this.fetchCount('Webhooks', WEBHOOKS_ICON, () =>
        this.webhooksApi.apiWebhooksGet({ skip: 0, take: 1 }),
      ),
    ]);
  }

  private async fetchCount(
    label: string,
    icon: string,
    request: () => Observable<CountedPage>,
  ): Promise<QuickStat> {
    try {
      const page = await firstValueFrom(request());
      return { label, icon, count: page.totalCount ?? 0 };
    } catch {
      return { label, icon, count: null };
    }
  }
}
