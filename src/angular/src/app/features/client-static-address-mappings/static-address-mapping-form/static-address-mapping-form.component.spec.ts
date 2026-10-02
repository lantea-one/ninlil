import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap, provideRouter } from '@angular/router';
import { of, throwError } from 'rxjs';
import { HttpErrorResponse } from '@angular/common/http';
import { DnsClientStaticAddressMappingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { StaticAddressMappingFormComponent } from './static-address-mapping-form.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the static mapping form: create and edit modes, saving, errors and
 * validation. TestBed with a spy-backed `DnsClientStaticAddressMappingsService` and a stubbed
 * route.
 */
describe('StaticAddressMappingFormComponent', () => {
  let apiSpy: {
    apiClientStaticAddressMappingsIdGet: jasmine.Spy;
    apiClientStaticAddressMappingsPost: jasmine.Spy;
    apiClientStaticAddressMappingsIdPut: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let navigateSpy: jasmine.Spy;

  async function createComponent(mappingId: string | null, configureApis?: () => void) {
    apiSpy = jasmine.createSpyObj('DnsClientStaticAddressMappingsService', [
      'apiClientStaticAddressMappingsIdGet',
      'apiClientStaticAddressMappingsPost',
      'apiClientStaticAddressMappingsIdPut',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    configureApis?.();

    await TestBed.configureTestingModule({
      imports: [StaticAddressMappingFormComponent],
      providers: [
        provideRouter([]),
        { provide: DnsClientStaticAddressMappingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: { paramMap: convertToParamMap(mappingId ? { id: mappingId } : {}) },
          },
        },
      ],
    }).compileComponents();

    navigateSpy = spyOn(TestBed.inject(Router), 'navigate').and.resolveTo(true);

    const fixture = TestBed.createComponent(StaticAddressMappingFormComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('create mode: does not load any mapping', async () => {
    const fixture = await createComponent(null);

    expect(fixture.componentInstance.isEditMode).toBeFalse();
    expect(apiSpy.apiClientStaticAddressMappingsIdGet).not.toHaveBeenCalled();
  });

  it('edit mode: loads the existing mapping', async () => {
    const fixture = await createComponent('mapping-1', () => {
      apiSpy.apiClientStaticAddressMappingsIdGet.and.returnValue(
        of({
          id: 'mapping-1',
          cidr: '10.99.0.42/32',
          clientIdentifier: 'aa:bb:cc:dd:ee:ff',
          protocolVersion: 'IPv4',
          isDisabled: false,
          comments: 'Guest VLAN printer',
        }),
      );
    });

    expect(fixture.componentInstance.form.controls.cidr.value).toBe('10.99.0.42/32');
    expect(fixture.componentInstance.form.controls.clientIdentifier.value).toBe(
      'aa:bb:cc:dd:ee:ff',
    );
    expect(fixture.componentInstance.form.controls.protocolVersion.value).toBe('IPv4');
    expect(fixture.componentInstance.form.controls.comments.value).toBe('Guest VLAN printer');
  });

  it('save() in create mode POSTs the built request', async () => {
    const fixture = await createComponent(null, () => {
      apiSpy.apiClientStaticAddressMappingsPost.and.returnValue(of({ id: 'new-mapping' }));
    });
    fixture.componentInstance.form.patchValue({
      cidr: '10.99.0.0/24',
      clientIdentifier: '11:22:33:44:55:66',
      protocolVersion: 'IPv4',
    });

    await fixture.componentInstance.save();

    expect(apiSpy.apiClientStaticAddressMappingsPost).toHaveBeenCalledWith({
      dnsClientStaticAddressMappingWriteRequest: {
        cidr: '10.99.0.0/24',
        clientIdentifier: '11:22:33:44:55:66',
        protocolVersion: 'IPv4',
        isDisabled: false,
        comments: null,
      },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/client-static-address-mappings']);
  });

  it('save() in edit mode PUTs the built request', async () => {
    const fixture = await createComponent('mapping-1', () => {
      apiSpy.apiClientStaticAddressMappingsIdGet.and.returnValue(
        of({
          id: 'mapping-1',
          cidr: '10.99.0.42/32',
          clientIdentifier: 'aa:bb:cc:dd:ee:ff',
          protocolVersion: 'IPv4',
          isDisabled: false,
          comments: null,
        }),
      );
      apiSpy.apiClientStaticAddressMappingsIdPut.and.returnValue(of(undefined));
    });

    await fixture.componentInstance.save();

    expect(apiSpy.apiClientStaticAddressMappingsIdPut).toHaveBeenCalledWith({
      id: 'mapping-1',
      dnsClientStaticAddressMappingWriteRequest: {
        cidr: '10.99.0.42/32',
        clientIdentifier: 'aa:bb:cc:dd:ee:ff',
        protocolVersion: 'IPv4',
        isDisabled: false,
        comments: null,
      },
    });
    expect(navigateSpy).toHaveBeenCalledWith(['/client-static-address-mappings']);
  });

  it('shows an error message and does not navigate when saving fails', async () => {
    const fixture = await createComponent(null, () => {
      apiSpy.apiClientStaticAddressMappingsPost.and.returnValue(
        throwError(
          () =>
            new HttpErrorResponse({
              status: 400,
              error: "'not a cidr' is not a valid IP address or CIDR network.",
            }),
        ),
      );
    });
    fixture.componentInstance.form.patchValue({
      cidr: 'not a cidr',
      clientIdentifier: 'aa:bb:cc:dd:ee:ff',
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe(
      "'not a cidr' is not a valid IP address or CIDR network.",
    );
    expect(navigateSpy).not.toHaveBeenCalled();
  });

  it('save() does nothing while the form is invalid', async () => {
    const fixture = await createComponent(null);
    fixture.componentInstance.form.patchValue({ cidr: '', clientIdentifier: '' });

    await fixture.componentInstance.save();

    expect(apiSpy.apiClientStaticAddressMappingsPost).not.toHaveBeenCalled();
  });
});
