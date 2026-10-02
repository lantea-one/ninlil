import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { IdentitySettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IdentitySettingsComponent } from './identity-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Identity page: loading, saving with the backend note and a save error.
 * TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('IdentitySettingsComponent', () => {
  let apiSpy: {
    apiIdentitySettingsGet: jasmine.Spy;
    apiIdentitySettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('IdentitySettingsService', [
      'apiIdentitySettingsGet',
      'apiIdentitySettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiIdentitySettingsGet.and.returnValue(
      of({
        requiredLength: 12,
        requiredUniqueChars: 1,
        requireDigit: true,
        requireLowercase: true,
        requireUppercase: true,
        requireNonAlphanumeric: true,
        defaultLockoutTimeSpan: '00:15:00',
        maxFailedAccessAttempts: 5,
        allowedForNewUsers: true,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [IdentitySettingsComponent],
      providers: [
        { provide: IdentitySettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(IdentitySettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiIdentitySettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.requiredLength.value).toBe(12);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the "next request" note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiIdentitySettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. ... takes effect immediately -- no restart required.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({ requiredLength: 16 });
    await fixture.componentInstance.save();

    expect(apiSpy.apiIdentitySettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ requiredLength: 16 }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiIdentitySettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
