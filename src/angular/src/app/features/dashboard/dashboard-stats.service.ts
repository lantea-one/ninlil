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
import { QuickStat } from './dashboard.model';

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
   * one resource's read permission still sees the other tiles. It never rejects.
   */
  async fetchQuickStats(): Promise<QuickStat[]> {
    return Promise.all([
      this.fetchCount('Zones', 'cilLan', () => this.zonesApi.apiZonesGet({ skip: 0, take: 1 })),
      this.fetchCount('Forwarders', 'cilShareAll', () =>
        this.forwardersApi.apiForwardersGet({ skip: 0, take: 1 }),
      ),
      this.fetchCount('Filter Lists', 'cilBan', () =>
        this.filterListsApi.apiFilterListsGet({ skip: 0, take: 1 }),
      ),
      this.fetchCount('Webhooks', 'cilLink', () =>
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
