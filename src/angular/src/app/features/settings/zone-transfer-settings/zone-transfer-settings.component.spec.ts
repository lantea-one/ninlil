import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ZoneTransferSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ZoneTransferSettingsComponent } from './zone-transfer-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Zone Transfer page: loading, saving with the backend note and a save
 * error. TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('ZoneTransferSettingsComponent', () => {
  let apiSpy: {
    apiZoneTransferSettingsGet: jasmine.Spy;
    apiZoneTransferSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('ZoneTransferSettingsService', [
      'apiZoneTransferSettingsGet',
      'apiZoneTransferSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiZoneTransferSettingsGet.and.returnValue(of({ recordsPerMessage: 100 }));
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [ZoneTransferSettingsComponent],
      providers: [
        { provide: ZoneTransferSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ZoneTransferSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiZoneTransferSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.recordsPerMessage.value).toBe(100);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reloaded note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiZoneTransferSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ recordsPerMessage: 200 });
    await fixture.componentInstance.save();

    expect(apiSpy.apiZoneTransferSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ recordsPerMessage: 200 }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiZoneTransferSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
