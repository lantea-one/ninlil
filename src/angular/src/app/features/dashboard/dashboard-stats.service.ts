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
 * This interface describes the one field every one of the 4 paged-list responses this service
 * reads actually needs -- every generated `*DetailPagedResult` model shape carries it, so this
 * narrower structural type lets {@link fetchCount} stay generic over all 4 without importing
 * each specific generated model just to read a single shared field.
 */
interface CountedPage {
  totalCount?: number;
}

/**
 * This service fetches the Dashboard's own data: the additive `GET /status` health response, and
 * 4 derived "quick stat" counts -- zones, forwarders, filter lists, and webhooks, picked as
 * whichever REST list endpoints are the most immediately meaningful to an operator landing on
 * this page.
 */
@Injectable({ providedIn: 'root' })
export class DashboardStatsService {
  private readonly statusApi = inject(NinlilWebService);
  private readonly zonesApi = inject(ZonesApiService);
  private readonly forwardersApi = inject(ForwardersApiService);
  private readonly filterListsApi = inject(FilterListsApiService);
  private readonly webhooksApi = inject(WebhooksApiService);

  /** This method fetches `GET /status`. */
  async fetchStatus(): Promise<StatusResponse> {
    return firstValueFrom(this.statusApi.statusGet());
  }

  /**
   * This method fetches one lightweight `skip=0&take=1` page from each of the 4 list endpoints
   * named above, reading only `PagedResult.totalCount` -- never a full page of data just to count
   * it. Each request is resolved independently, not as one failure-propagating `Promise.all`, so
   * a caller lacking one
   * resource's own `*:read` permission (a real possibility: the dashboard route itself requires
   * no specific permission at all) still sees every *other* stat tile, with the denied one
   * rendered as unavailable rather than the whole dashboard failing to load.
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
