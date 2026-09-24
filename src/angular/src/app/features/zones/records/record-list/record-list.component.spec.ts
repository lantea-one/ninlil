import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of } from 'rxjs';
import { DnsResourceRecordTypeEnum, RecordsService, ZonesService } from '../../../../api-client';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { RecordListComponent } from './record-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('RecordListComponent', () => {
  // Loosely-typed spies, not `jasmine.SpyObj<RecordsService>`/`<ZonesService>` -- see
  // `zone-list.component.spec.ts`'s own identical note for why (the generated services' own
  // multi-overload methods otherwise infer the wrong, `HttpEvent`-wrapped return type).
  let recordsApiSpy: {
    apiZonesZoneIdRecordsGet: jasmine.Spy;
    apiZonesZoneIdRecordsIdPatch: jasmine.Spy;
    apiZonesZoneIdRecordsIdDelete: jasmine.Spy;
  };
  let zonesApiSpy: { apiZonesIdGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    recordsApiSpy = jasmine.createSpyObj('RecordsService', [
      'apiZonesZoneIdRecordsGet',
      'apiZonesZoneIdRecordsIdPatch',
      'apiZonesZoneIdRecordsIdDelete',
    ]);
    zonesApiSpy = jasmine.createSpyObj('ZonesService', ['apiZonesIdGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    zonesApiSpy.apiZonesIdGet.and.returnValue(of({ id: 'zone-1', name: 'example.com' }));
    recordsApiSpy.apiZonesZoneIdRecordsGet.and.returnValue(
      of({
        items: [
          {
            id: 'record-1',
            zoneId: 'zone-1',
            name: '@',
            type: DnsResourceRecordTypeEnum.Soa,
            timeToLiveSeconds: 3600,
            isDisabled: false,
          },
          {
            id: 'record-2',
            zoneId: 'zone-1',
            name: 'www',
            type: DnsResourceRecordTypeEnum.A,
            timeToLiveSeconds: 300,
            isDisabled: false,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 2,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [RecordListComponent],
      providers: [
        provideRouter([]),
        { provide: RecordsService, useValue: recordsApiSpy },
        { provide: ZonesService, useValue: zonesApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ zoneId: 'zone-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(RecordListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the zone name and the first page of records on init', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.zoneName()).toBe('example.com');
    expect(fixture.componentInstance.records().length).toBe(2);
    expect(fixture.componentInstance.totalCount()).toBe(2);
  });

  it('onPageChange() requests the correctly offset skip/take for this zone', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.onPageChange({ pageIndex: 1, pageSize: 10 });

    expect(recordsApiSpy.apiZonesZoneIdRecordsGet).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      skip: 10,
      take: 10,
    });
  });

  it("deleteRecord() refuses to even prompt for the zone's own SOA record", async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.deleteRecord(fixture.componentInstance.records()[0]);

    expect(fixture.componentInstance.confirmDialogVisible()).toBeFalse();
    expect(recordsApiSpy.apiZonesZoneIdRecordsIdDelete).not.toHaveBeenCalled();
  });

  it('deleteRecord() deletes a non-SOA record once confirmed, then reloads the page', async () => {
    const fixture = await createComponent();
    recordsApiSpy.apiZonesZoneIdRecordsIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteRecord(
      fixture.componentInstance.records()[1],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(recordsApiSpy.apiZonesZoneIdRecordsIdDelete).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      id: 'record-2',
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('toggleDisabled() sends a single-field merge patch, then reloads the page', async () => {
    const fixture = await createComponent();
    recordsApiSpy.apiZonesZoneIdRecordsIdPatch.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.records()[1]);

    expect(recordsApiSpy.apiZonesZoneIdRecordsIdPatch).toHaveBeenCalledWith({
      zoneId: 'zone-1',
      id: 'record-2',
      body: { isDisabled: true },
    });
  });
});
