import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { AdminLoginRateLimitSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { AdminLoginRateLimitSettingsComponent } from './admin-login-rate-limit-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('AdminLoginRateLimitSettingsComponent', () => {
  let apiSpy: {
    apiAdminLoginRateLimitSettingsGet: jasmine.Spy;
    apiAdminLoginRateLimitSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('AdminLoginRateLimitSettingsService', [
      'apiAdminLoginRateLimitSettingsGet',
      'apiAdminLoginRateLimitSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiAdminLoginRateLimitSettingsGet.and.returnValue(
      of({
        enable: true,
        permitLimit: 5,
        window: '00:05:00',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [AdminLoginRateLimitSettingsComponent],
      providers: [
        { provide: AdminLoginRateLimitSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(AdminLoginRateLimitSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiAdminLoginRateLimitSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enable.value).toBeTrue();
    expect(fixture.componentInstance.form.controls.permitLimit.value).toBe(5);
    expect(fixture.componentInstance.form.controls.window.value).toBe('00:05:00');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the restart-required note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiAdminLoginRateLimitSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... a restart is required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ permitLimit: 3 });
    await fixture.componentInstance.save();

    expect(apiSpy.apiAdminLoginRateLimitSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ permitLimit: 3, enable: true, window: '00:05:00' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('a restart is required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiAdminLoginRateLimitSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });

  it('does not submit an invalid form (permitLimit below the minimum)', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiAdminLoginRateLimitSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ permitLimit: 0 });
    await fixture.componentInstance.save();

    expect(apiSpy.apiAdminLoginRateLimitSettingsPatch).not.toHaveBeenCalled();
  });
});
