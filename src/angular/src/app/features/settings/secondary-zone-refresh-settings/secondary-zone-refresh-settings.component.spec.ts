import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { SecondaryZoneRefreshSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { SecondaryZoneRefreshSettingsComponent } from './secondary-zone-refresh-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Secondary Zone Refresh page: loading, saving with the backend note and a
 * save error. TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('SecondaryZoneRefreshSettingsComponent', () => {
  let apiSpy: {
    apiSecondaryZoneRefreshSettingsGet: jasmine.Spy;
    apiSecondaryZoneRefreshSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('SecondaryZoneRefreshSettingsService', [
      'apiSecondaryZoneRefreshSettingsGet',
      'apiSecondaryZoneRefreshSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiSecondaryZoneRefreshSettingsGet.and.returnValue(
      of({ pollInterval: '00:00:15', defaultRetryInterval: '00:00:30' }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [SecondaryZoneRefreshSettingsComponent],
      providers: [
        { provide: SecondaryZoneRefreshSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SecondaryZoneRefreshSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiSecondaryZoneRefreshSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.pollInterval.value).toBe('00:00:15');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reloaded note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSecondaryZoneRefreshSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ pollInterval: '00:00:05' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiSecondaryZoneRefreshSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ pollInterval: '00:00:05' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSecondaryZoneRefreshSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
