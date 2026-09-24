import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { DnsClientStaticAddressMappingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { StaticAddressMappingListComponent } from './static-address-mapping-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('StaticAddressMappingListComponent', () => {
  let apiSpy: {
    apiClientStaticAddressMappingsGet: jasmine.Spy;
    apiClientStaticAddressMappingsIdPut: jasmine.Spy;
    apiClientStaticAddressMappingsIdDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent() {
    apiSpy = jasmine.createSpyObj('DnsClientStaticAddressMappingsService', [
      'apiClientStaticAddressMappingsGet',
      'apiClientStaticAddressMappingsIdPut',
      'apiClientStaticAddressMappingsIdDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiClientStaticAddressMappingsGet.and.returnValue(
      of({
        items: [
          {
            id: 'mapping-1',
            cidr: '10.99.0.42/32',
            clientIdentifier: 'aa:bb:cc:dd:ee:ff',
            protocolVersion: 'IPv4',
            isDisabled: false,
            comments: 'Guest VLAN printer',
            createdAtUtc: '2026-09-18T00:00:00Z',
            updatedAtUtc: null,
          },
        ],
        skip: 0,
        take: 25,
        totalCount: 1,
      }),
    );

    await TestBed.configureTestingModule({
      imports: [StaticAddressMappingListComponent],
      providers: [
        provideRouter([]),
        { provide: DnsClientStaticAddressMappingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(StaticAddressMappingListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the first page of mappings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiClientStaticAddressMappingsGet).toHaveBeenCalledWith({ skip: 0, take: 25 });
    expect(fixture.componentInstance.mappings().length).toBe(1);
    expect(fixture.componentInstance.mappings()[0].clientIdentifier).toBe('aa:bb:cc:dd:ee:ff');
  });

  it('toggleDisabled() sends a full PUT preserving every other field, then reloads the page', async () => {
    const fixture = await createComponent();
    apiSpy.apiClientStaticAddressMappingsIdPut.and.returnValue(of(undefined));

    await fixture.componentInstance.toggleDisabled(fixture.componentInstance.mappings()[0]);

    expect(apiSpy.apiClientStaticAddressMappingsIdPut).toHaveBeenCalledWith({
      id: 'mapping-1',
      dnsClientStaticAddressMappingWriteRequest: {
        cidr: '10.99.0.42/32',
        clientIdentifier: 'aa:bb:cc:dd:ee:ff',
        protocolVersion: 'IPv4',
        isDisabled: true,
        comments: 'Guest VLAN printer',
      },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteMapping() deletes and reloads the page once the confirmation dialog is accepted', async () => {
    const fixture = await createComponent();
    apiSpy.apiClientStaticAddressMappingsIdDelete.and.returnValue(of(undefined));

    const deletePromise = fixture.componentInstance.deleteMapping(
      fixture.componentInstance.mappings()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(apiSpy.apiClientStaticAddressMappingsIdDelete).toHaveBeenCalledWith({
      id: 'mapping-1',
    });
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('deleteMapping() does nothing when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent();

    const deletePromise = fixture.componentInstance.deleteMapping(
      fixture.componentInstance.mappings()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(false);
    await deletePromise;

    expect(apiSpy.apiClientStaticAddressMappingsIdDelete).not.toHaveBeenCalled();
  });

  it('shows an error notification when delete fails', async () => {
    const fixture = await createComponent();
    apiSpy.apiClientStaticAddressMappingsIdDelete.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'in use' })),
    );

    const deletePromise = fixture.componentInstance.deleteMapping(
      fixture.componentInstance.mappings()[0],
    );
    fixture.componentInstance.onConfirmDialogResolved(true);
    await deletePromise;

    expect(notificationSpy.error).toHaveBeenCalledWith('in use');
  });
});
