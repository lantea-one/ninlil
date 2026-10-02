import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { ClientGroupsService, ClientsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClientFormComponent } from './client-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the client form: register and edit modes, the locked identifier in edit
 * mode, the vendor and icon suggestion, saving and errors. TestBed with spy-backed
 * `ClientsService` and `ClientGroupsService` and a stubbed route.
 */
describe('ClientFormComponent', () => {
  let clientsApiSpy: {
    apiClientsIdGet: jasmine.Spy;
    apiClientsPost: jasmine.Spy;
    apiClientsIdPut: jasmine.Spy;
  };
  let clientGroupsApiSpy: { apiClientGroupsGet: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(clientId: string | null, configureApis?: () => void) {
    clientsApiSpy = jasmine.createSpyObj('ClientsService', [
      'apiClientsIdGet',
      'apiClientsPost',
      'apiClientsIdPut',
    ]);
    clientGroupsApiSpy = jasmine.createSpyObj('ClientGroupsService', ['apiClientGroupsGet']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    clientGroupsApiSpy.apiClientGroupsGet.and.returnValue(
      of({ items: [{ id: 'group-1', name: 'Trusted LAN' }], skip: 0, take: 500, totalCount: 1 }),
    );

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [ClientFormComponent],
      providers: [
        provideRouter([]),
        { provide: ClientsService, useValue: clientsApiSpy },
        { provide: ClientGroupsService, useValue: clientGroupsApiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap(clientId ? { clientId } : {}) } },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(ClientFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: does not load any client, and leaves identity fields editable', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(clientsApiSpy.apiClientsIdGet).not.toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.clientIdentifier.disabled).toBeFalse();
    expect(fixture.componentInstance.form.controls.protocolVersion.disabled).toBeFalse();
  });

  it('edit mode: loads the existing client, populates every field, and locks its identity fields', async () => {
    const fixture = await createComponent('client-1', () => {
      clientsApiSpy.apiClientsIdGet.and.returnValue(
        of({
          id: 'client-1',
          addresses: [
            { id: 'address-1', clientIdentifier: 'AA:BB:CC:DD:EE:FF', protocolVersion: 'IPv4' },
          ],
          displayName: 'Living Room TV',
          clientGroupId: 'group-1',
          isDisabled: false,
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.clientIdentifier.value).toBe(
      'AA:BB:CC:DD:EE:FF',
    );
    expect(fixture.componentInstance.form.controls.displayName.value).toBe('Living Room TV');
    expect(fixture.componentInstance.form.controls.clientGroupId.value).toBe('group-1');
    expect(fixture.componentInstance.form.controls.clientIdentifier.disabled).toBeTrue();
    expect(fixture.componentInstance.form.controls.protocolVersion.disabled).toBeTrue();
  });

  it('save() in create mode POSTs the built request and navigates back to the list', async () => {
    const fixture = await createComponent(null, () => {
      clientsApiSpy.apiClientsPost.and.returnValue(of({ id: 'new-client' }));
    });
    fixture.componentInstance.form.patchValue({
      clientIdentifier: 'AA:BB:CC:DD:EE:FF',
      protocolVersion: 'IPv4',
    });

    await fixture.componentInstance.save();

    expect(clientsApiSpy.apiClientsPost).toHaveBeenCalledWith({
      clientCreateRequest: {
        clientIdentifier: 'AA:BB:CC:DD:EE:FF',
        protocolVersion: 'IPv4',
        displayName: null,
        clientGroupId: null,
        isDisabled: false,
        comments: null,
        deviceIconKey: null,
        isFavorite: false,
      },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/clients']);
  });

  it('save() in edit mode PUTs only the editable fields, never the identity', async () => {
    const fixture = await createComponent('client-1', () => {
      clientsApiSpy.apiClientsIdGet.and.returnValue(
        of({
          id: 'client-1',
          addresses: [
            { id: 'address-1', clientIdentifier: 'AA:BB:CC:DD:EE:FF', protocolVersion: 'IPv4' },
          ],
          displayName: 'Living Room TV',
          clientGroupId: null,
          isDisabled: false,
        }),
      );
      clientsApiSpy.apiClientsIdPut.and.returnValue(of(undefined));
    });
    fixture.componentInstance.form.patchValue({ displayName: 'Renamed TV' });

    await fixture.componentInstance.save();

    expect(clientsApiSpy.apiClientsIdPut).toHaveBeenCalledWith({
      id: 'client-1',
      clientWriteRequest: {
        displayName: 'Renamed TV',
        clientGroupId: null,
        isDisabled: false,
        comments: null,
        deviceIconKey: null,
        isFavorite: false,
      },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/clients']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      clientsApiSpy.apiClientsPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 409,
              error: 'A client already exists for this identifier.',
            }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({
      clientIdentifier: 'AA:BB:CC:DD:EE:FF',
      protocolVersion: 'IPv4',
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe(
      'A client already exists for this identifier.',
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ clientIdentifier: '' });

    await fixture.componentInstance.save();

    expect(clientsApiSpy.apiClientsPost).not.toHaveBeenCalled();
  });
  it('edit mode: shows the read-time vendor and hands the icon suggestion to the picker', async () => {
    const fixture = await createComponent('client-1', () => {
      clientsApiSpy.apiClientsIdGet.and.returnValue(
        of({
          id: 'client-1',
          addresses: [
            { id: 'address-1', clientIdentifier: 'b8:27:eb:12:34:56', protocolVersion: 'IPv4' },
          ],
          vendor: 'Raspberry Pi Foundation',
          suggestedDeviceIconKey: 'SingleBoardComputer',
          deviceIconKey: 'Television',
          isFavorite: true,
        }),
      );
    });
    const element: HTMLElement = fixture.nativeElement;

    expect(element.querySelector('[data-testid="client-vendor"]')?.textContent).toContain(
      'Raspberry Pi Foundation',
    );
    expect(fixture.componentInstance.suggestedDeviceIconKey()).toBe('SingleBoardComputer');
    expect(fixture.componentInstance.form.controls.deviceIconKey.value).toBe('Television');
    expect(fixture.componentInstance.form.controls.isFavorite.value).toBeTrue();
    expect(
      element.querySelector('[data-icon-key="Television"]')?.getAttribute('aria-checked'),
    ).toBe('true');
    expect(element.textContent).toContain('Single-board computer');
  });

  it('edit mode: reports a randomized MAC distinctly rather than as an unknown vendor', async () => {
    const fixture = await createComponent('client-1', () => {
      clientsApiSpy.apiClientsIdGet.and.returnValue(
        of({ id: 'client-1', addresses: [], vendor: null, isRandomizedMacAddress: true }),
      );
    });

    expect(
      fixture.nativeElement.querySelector('[data-testid="client-vendor"]')?.textContent,
    ).toContain('Randomized MAC address');
  });

  it('save() sends a picked icon and the favorite star', async () => {
    const fixture = await createComponent(null, () => {
      clientsApiSpy.apiClientsPost.and.returnValue(of({ id: 'client-new' }));
    });
    const element: HTMLElement = fixture.nativeElement;
    fixture.componentInstance.form.patchValue({ clientIdentifier: 'b8:27:eb:12:34:56' });
    (element.querySelector('[data-icon-key="Laptop"]') as HTMLButtonElement).click();
    (element.querySelector('#client-favorite') as HTMLInputElement).click();
    fixture.detectChanges();

    await fixture.componentInstance.save();

    expect(clientsApiSpy.apiClientsPost).toHaveBeenCalledWith({
      clientCreateRequest: jasmine.objectContaining({
        deviceIconKey: 'Laptop',
        isFavorite: true,
      }),
    });
  });
});
