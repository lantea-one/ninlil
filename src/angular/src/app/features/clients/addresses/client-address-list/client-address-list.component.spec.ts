import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import {
  ClientAddressDetail,
  ClientAddressesService,
  ClientsService,
} from '../../../../api-client';
import { AuthService } from '../../../../core/auth/auth.service';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { ClientAddressListComponent } from './client-address-list.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

const IPV4_ADDRESS: ClientAddressDetail = {
  id: 'address-4',
  clientId: 'client-1',
  clientIdentifier: 'b8:27:eb:12:34:56',
  protocolVersion: 'IPv4',
  address: '192.0.2.42',
  firstSeenUtc: '2026-09-10T09:00:00Z',
  lastSeenUtc: new Date(Date.now() - 2 * 60_000).toISOString(),
  macAddress: 'b8:27:eb:12:34:56',
  vendor: 'Raspberry Pi Foundation',
};

const IPV6_ADDRESS: ClientAddressDetail = {
  id: 'address-6',
  clientId: 'client-1',
  clientIdentifier: '00:03:00:01:b8:27:eb:12:34:56',
  protocolVersion: 'IPv6',
  address: null,
  firstSeenUtc: '2026-09-11T09:00:00Z',
  lastSeenUtc: null,
  macAddress: 'b8:27:eb:12:34:56',
  vendor: 'Raspberry Pi Foundation',
};

describe('ClientAddressListComponent', () => {
  let addressesApiSpy: {
    apiClientsClientIdAddressesGet: jasmine.Spy;
    apiClientsClientIdAddressesPost: jasmine.Spy;
    apiClientsClientIdAddressesIdDelete: jasmine.Spy;
  };
  let clientsApiSpy: { apiClientsIdGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(
    addresses: ClientAddressDetail[] = [IPV4_ADDRESS, IPV6_ADDRESS],
    canWrite = true,
  ) {
    addressesApiSpy = jasmine.createSpyObj('ClientAddressesService', [
      'apiClientsClientIdAddressesGet',
      'apiClientsClientIdAddressesPost',
      'apiClientsClientIdAddressesIdDelete',
    ]);
    clientsApiSpy = jasmine.createSpyObj('ClientsService', ['apiClientsIdGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    const authServiceSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authServiceSpy.hasPermission.and.returnValue(canWrite);

    addressesApiSpy.apiClientsClientIdAddressesGet.and.returnValue(
      of({ items: addresses, skip: 0, take: 100, totalCount: addresses.length }),
    );
    clientsApiSpy.apiClientsIdGet.and.returnValue(
      of({ id: 'client-1', displayName: 'Living Room TV', addresses }),
    );

    await TestBed.configureTestingModule({
      imports: [ClientAddressListComponent],
      providers: [
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ clientId: 'client-1' }) } },
        },
        { provide: ClientAddressesService, useValue: addressesApiSpy },
        { provide: ClientsService, useValue: clientsApiSpy },
        { provide: AuthService, useValue: authServiceSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClientAddressListComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it("lists every one of the device's identifiers with its vendor and sightings", async () => {
    const fixture = await createComponent();
    const text: string = fixture.nativeElement.textContent;

    expect(addressesApiSpy.apiClientsClientIdAddressesGet).toHaveBeenCalledWith({
      clientId: 'client-1',
      skip: 0,
      take: 100,
    });
    expect(fixture.componentInstance.clientLabel()).toBe('Living Room TV');
    expect(text).toContain('b8:27:eb:12:34:56');
    expect(text).toContain('00:03:00:01:b8:27:eb:12:34:56');
    expect(text).toContain('192.0.2.42');
    expect(text).toContain('Raspberry Pi Foundation');
    expect(text).toContain('Last 2 min ago');
    expect(text).toContain('Never seen');
  });

  it('adds an identifier with a trimmed value, then reloads and clears the form', async () => {
    const fixture = await createComponent();
    addressesApiSpy.apiClientsClientIdAddressesPost.and.returnValue(of(IPV6_ADDRESS));
    const component = fixture.componentInstance;
    component.form.setValue({
      clientIdentifier: '  00:03:00:01:aa:bb:cc:dd:ee:ff  ',
      protocolVersion: 'IPv6',
    });

    await component.addAddress();

    expect(addressesApiSpy.apiClientsClientIdAddressesPost).toHaveBeenCalledWith({
      clientId: 'client-1',
      clientAddressWriteRequest: {
        clientIdentifier: '00:03:00:01:aa:bb:cc:dd:ee:ff',
        protocolVersion: 'IPv6',
      },
    });
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(addressesApiSpy.apiClientsClientIdAddressesGet).toHaveBeenCalledTimes(2);
    expect(component.form.controls.clientIdentifier.value).toBe('');
  });

  it('does not submit an empty identifier', async () => {
    const fixture = await createComponent();

    await fixture.componentInstance.addAddress();

    expect(addressesApiSpy.apiClientsClientIdAddressesPost).not.toHaveBeenCalled();
  });

  it("shows the server's own refusal of a duplicate identifier", async () => {
    const fixture = await createComponent();
    addressesApiSpy.apiClientsClientIdAddressesPost.and.returnValue(
      throwError(() => new HttpErrorResponse({ status: 409, error: 'already registered' })),
    );
    fixture.componentInstance.form.controls.clientIdentifier.setValue('b8:27:eb:12:34:56');

    await fixture.componentInstance.addAddress();

    expect(notificationSpy.error).toHaveBeenCalledWith('already registered');
  });

  it('removes an identifier once the confirmation is accepted', async () => {
    const fixture = await createComponent();
    addressesApiSpy.apiClientsClientIdAddressesIdDelete.and.returnValue(of(undefined));

    const removal = fixture.componentInstance.removeAddress(IPV6_ADDRESS);
    fixture.componentInstance.onConfirmDialogResolved(true);
    await removal;

    expect(addressesApiSpy.apiClientsClientIdAddressesIdDelete).toHaveBeenCalledWith({
      clientId: 'client-1',
      id: 'address-6',
    });
  });

  it("disables removing a device's last identifier", async () => {
    const fixture = await createComponent([IPV4_ADDRESS]);
    const remove: HTMLButtonElement = fixture.nativeElement.querySelector(
      '[data-testid="remove-address"]',
    );

    expect(remove.disabled).toBeTrue();
  });

  it('hides the add form and remove actions without clients:write', async () => {
    const fixture = await createComponent(undefined, false);

    expect(fixture.nativeElement.querySelector('form')).toBeNull();
    expect(fixture.nativeElement.querySelector('[data-testid="remove-address"]')).toBeNull();
  });
});
