import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { DnsCookieSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { DnsCookieSettingsComponent } from './dns-cookie-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the DNS Cookies page: loading, saving with the restart note and a save error.
 * TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('DnsCookieSettingsComponent', () => {
  let apiSpy: { apiDnsCookieSettingsGet: jasmine.Spy; apiDnsCookieSettingsPatch: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('DnsCookieSettingsService', [
      'apiDnsCookieSettingsGet',
      'apiDnsCookieSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiDnsCookieSettingsGet.and.returnValue(
      of({ enable: true, require: false, secretRotationInterval: '1.00:00:00' }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [DnsCookieSettingsComponent],
      providers: [
        { provide: DnsCookieSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DnsCookieSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiDnsCookieSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enable.value).toBeTrue();
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the restart-required note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDnsCookieSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... a restart is required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ require: true });
    await fixture.componentInstance.save();

    expect(apiSpy.apiDnsCookieSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ require: true }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('a restart is required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDnsCookieSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
