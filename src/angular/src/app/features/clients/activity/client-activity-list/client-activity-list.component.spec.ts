import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { ClientActivityService, ClientsService } from '../../../../api-client';
import { ClientActivityListComponent } from './client-activity-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover a client's activity page: loading a page, paging, refresh and errors.
 * TestBed with spy-backed `ClientActivityService` and `ClientsService` and a stubbed route.
 */
describe('ClientActivityListComponent', () => {
  let activityApiSpy: { apiClientsClientIdActivityGet: jasmine.Spy };
  let clientsApiSpy: { apiClientsIdGet: jasmine.Spy };

  async function createComponent() {
    activityApiSpy = jasmine.createSpyObj('ClientActivityService', [
      'apiClientsClientIdActivityGet',
    ]);
    clientsApiSpy = jasmine.createSpyObj('ClientsService', ['apiClientsIdGet']);

    clientsApiSpy.apiClientsIdGet.and.returnValue(
      of({
        id: 'client-1',
        addresses: [
          { id: 'address-1', clientIdentifier: 'AA:BB:CC:DD:EE:FF', protocolVersion: 'IPv4' },
        ],
        displayName: 'Living Room TV',
      }),
    );
    activityApiSpy.apiClientsClientIdActivityGet.and.returnValue(
      of({
        items: [
          {
            id: 'entry-1',
            occurredAtUtc: '2026-09-17T12:00:00Z',
            clientAddress: '203.0.113.10',
            protocol: 'Udp',
            queriedName: 'example.com',
            queriedType: 'A',
            responseCode: 'NoError',
            answerCount: 1,
            clientIdentifier: 'AA:BB:CC:DD:EE:FF',
            clientProtocolVersion: 'IPv4',
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [ClientActivityListComponent],
      providers: [
        provideRouter([]),
        { provide: ClientActivityService, useValue: activityApiSpy },
        { provide: ClientsService, useValue: clientsApiSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ clientId: 'client-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClientActivityListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads this clients own activity, correctly paginated', async () => {
    const fixture = await createComponent();

    expect(activityApiSpy.apiClientsClientIdActivityGet).toHaveBeenCalledWith({
      clientId: 'client-1',
      skip: 0,
      take: 25,
    });
    expect(fixture.componentInstance.entries().length).toBe(1);
    expect(fixture.componentInstance.entries()[0].queriedName).toBe('example.com');
    expect(fixture.componentInstance.totalCount()).toBe(1);
    expect(fixture.componentInstance.clientLabel()).toBe('Living Room TV');
  });

  it('onPageChange() reloads the requested page', async () => {
    const fixture = await createComponent();
    activityApiSpy.apiClientsClientIdActivityGet.calls.reset();

    await fixture.componentInstance.onPageChange({ pageIndex: 2, pageSize: 10 });

    expect(activityApiSpy.apiClientsClientIdActivityGet).toHaveBeenCalledWith({
      clientId: 'client-1',
      skip: 20,
      take: 10,
    });
  });

  it('refresh() re-issues the same page request', async () => {
    const fixture = await createComponent();
    activityApiSpy.apiClientsClientIdActivityGet.calls.reset();

    await fixture.componentInstance.refresh();

    expect(activityApiSpy.apiClientsClientIdActivityGet).toHaveBeenCalledWith({
      clientId: 'client-1',
      skip: 0,
      take: 25,
    });
  });

  it('surfaces a real error message when the load fails', async () => {
    activityApiSpy = jasmine.createSpyObj('ClientActivityService', [
      'apiClientsClientIdActivityGet',
    ]);
    clientsApiSpy = jasmine.createSpyObj('ClientsService', ['apiClientsIdGet']);
    clientsApiSpy.apiClientsIdGet.and.returnValue(of({ id: 'client-1' }));
    activityApiSpy.apiClientsClientIdActivityGet.and.returnValue(
      throwError(() => new Error('boom')),
    );

    await TestBed.configureTestingModule({
      imports: [ClientActivityListComponent],
      providers: [
        provideRouter([]),
        { provide: ClientActivityService, useValue: activityApiSpy },
        { provide: ClientsService, useValue: clientsApiSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ clientId: 'client-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClientActivityListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();

    expect(fixture.componentInstance.errorMessage()).toBeTruthy();
    expect(fixture.componentInstance.entries().length).toBe(0);
  });
});
