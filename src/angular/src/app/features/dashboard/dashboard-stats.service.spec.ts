import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import {
  FilterListsService as FilterListsApiService,
  ForwardersService as ForwardersApiService,
  NinlilWebService,
  WebhooksService as WebhooksApiService,
  ZonesService as ZonesApiService,
} from '../../api-client';
import { DashboardStatsService } from './dashboard-stats.service';

/**
 * These tests cover the dashboard data service: `/status` with and without detail, the four
 * `take=1` counts and a failed count becoming `null`. TestBed with spy-backed API services.
 */
describe('DashboardStatsService', () => {
  let service: DashboardStatsService;
  let statusApiSpy: { statusGet: jasmine.Spy };
  let zonesApiSpy: { apiZonesGet: jasmine.Spy };
  let forwardersApiSpy: { apiForwardersGet: jasmine.Spy };
  let filterListsApiSpy: { apiFilterListsGet: jasmine.Spy };
  let webhooksApiSpy: { apiWebhooksGet: jasmine.Spy };

  beforeEach(() => {
    statusApiSpy = { statusGet: jasmine.createSpy('statusGet') };
    zonesApiSpy = { apiZonesGet: jasmine.createSpy('apiZonesGet') };
    forwardersApiSpy = { apiForwardersGet: jasmine.createSpy('apiForwardersGet') };
    filterListsApiSpy = { apiFilterListsGet: jasmine.createSpy('apiFilterListsGet') };
    webhooksApiSpy = { apiWebhooksGet: jasmine.createSpy('apiWebhooksGet') };

    TestBed.configureTestingModule({
      providers: [
        DashboardStatsService,
        { provide: NinlilWebService, useValue: statusApiSpy },
        { provide: ZonesApiService, useValue: zonesApiSpy },
        { provide: ForwardersApiService, useValue: forwardersApiSpy },
        { provide: FilterListsApiService, useValue: filterListsApiSpy },
        { provide: WebhooksApiService, useValue: webhooksApiSpy },
      ],
    });

    service = TestBed.inject(DashboardStatsService);
  });

  it('fetchStatus() returns the /status response', async () => {
    statusApiSpy.statusGet.and.returnValue(
      of({
        status: 'Healthy',
        components: [{ name: 'storage', status: 'Healthy', description: 'OK', error: null }],
      }),
    );

    const result = await service.fetchStatus();

    expect(result.status).toBe('Healthy');
    expect(result.components?.[0].name).toBe('storage');
  });

  it('fetchStatus() propagates a summary-only response (components: null) unchanged', async () => {
    statusApiSpy.statusGet.and.returnValue(of({ status: 'Healthy', components: null }));

    const result = await service.fetchStatus();

    expect(result.components).toBeNull();
  });

  it('fetchQuickStats() reads totalCount from each of the 4 list endpoints, requesting skip=0&take=1', async () => {
    zonesApiSpy.apiZonesGet.and.returnValue(of({ items: [], skip: 0, take: 1, totalCount: 12 }));
    forwardersApiSpy.apiForwardersGet.and.returnValue(
      of({ items: [], skip: 0, take: 1, totalCount: 3 }),
    );
    filterListsApiSpy.apiFilterListsGet.and.returnValue(
      of({ items: [], skip: 0, take: 1, totalCount: 2 }),
    );
    webhooksApiSpy.apiWebhooksGet.and.returnValue(
      of({ items: [], skip: 0, take: 1, totalCount: 0 }),
    );

    const stats = await service.fetchQuickStats();

    expect(zonesApiSpy.apiZonesGet).toHaveBeenCalledWith({ skip: 0, take: 1 });
    expect(forwardersApiSpy.apiForwardersGet).toHaveBeenCalledWith({ skip: 0, take: 1 });
    expect(filterListsApiSpy.apiFilterListsGet).toHaveBeenCalledWith({ skip: 0, take: 1 });
    expect(webhooksApiSpy.apiWebhooksGet).toHaveBeenCalledWith({ skip: 0, take: 1 });

    expect(stats).toEqual([
      { label: 'Zones', icon: 'cilLan', count: 12 },
      { label: 'Forwarders', icon: 'cilShareAll', count: 3 },
      { label: 'Filter Lists', icon: 'cilBan', count: 2 },
      { label: 'Webhooks', icon: 'cilLink', count: 0 },
    ]);
  });

  it('fetchQuickStats() renders a stat as unavailable (count: null) rather than failing the whole batch', async () => {
    zonesApiSpy.apiZonesGet.and.returnValue(of({ items: [], skip: 0, take: 1, totalCount: 12 }));
    forwardersApiSpy.apiForwardersGet.and.returnValue(throwError(() => new Error('403 Forbidden')));
    filterListsApiSpy.apiFilterListsGet.and.returnValue(
      of({ items: [], skip: 0, take: 1, totalCount: 2 }),
    );
    webhooksApiSpy.apiWebhooksGet.and.returnValue(
      of({ items: [], skip: 0, take: 1, totalCount: 0 }),
    );

    const stats = await service.fetchQuickStats();

    expect(stats.find((stat) => stat.label === 'Forwarders')?.count).toBeNull();
    expect(stats.find((stat) => stat.label === 'Zones')?.count).toBe(12);
  });
});
