import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ClientSubnetSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClientSubnetSettingsComponent } from './client-subnet-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Client Subnet page: loading, saving (blank override address as null, others
 * trimmed) and a save error. TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('ClientSubnetSettingsComponent', () => {
  let apiSpy: {
    apiClientSubnetSettingsGet: jasmine.Spy;
    apiClientSubnetSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('ClientSubnetSettingsService', [
      'apiClientSubnetSettingsGet',
      'apiClientSubnetSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiClientSubnetSettingsGet.and.returnValue(
      of({
        enable: false,
        sourcePrefixLengthIPv4: 24,
        sourcePrefixLengthIPv6: 56,
        overrideAddress: null,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [ClientSubnetSettingsComponent],
      providers: [
        { provide: ClientSubnetSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ClientSubnetSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiClientSubnetSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enable.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.sourcePrefixLengthIPv4.value).toBe(24);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the form, converting a blank override address to null', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiClientSubnetSettingsPatch.and.returnValue(
        of({ note: 'takes effect immediately -- no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ enable: true, overrideAddress: '  ' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiClientSubnetSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ enable: true, overrideAddress: null }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('save() trims a real override address rather than clearing it', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiClientSubnetSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ overrideAddress: ' 203.0.113.5 ' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiClientSubnetSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ overrideAddress: '203.0.113.5' }),
    });
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiClientSubnetSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
