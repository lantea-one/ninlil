import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { NotifySettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { NotifySettingsComponent } from './notify-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the NOTIFY page: loading, saving with the backend note and a save error.
 * TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('NotifySettingsComponent', () => {
  let apiSpy: {
    apiNotifySettingsGet: jasmine.Spy;
    apiNotifySettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('NotifySettingsService', [
      'apiNotifySettingsGet',
      'apiNotifySettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiNotifySettingsGet.and.returnValue(
      of({
        maxAttempts: 5,
        responseTimeout: '00:00:03',
        retryDelay: '00:00:03',
        secondaryPort: 53,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [NotifySettingsComponent],
      providers: [
        { provide: NotifySettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(NotifySettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiNotifySettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.maxAttempts.value).toBe(5);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reloaded note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiNotifySettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ maxAttempts: 8 });
    await fixture.componentInstance.save();

    expect(apiSpy.apiNotifySettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ maxAttempts: 8 }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiNotifySettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
