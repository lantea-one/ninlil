import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ZoneTransferClientSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ZoneTransferClientSettingsComponent } from './zone-transfer-client-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ZoneTransferClientSettingsComponent', () => {
  let apiSpy: {
    apiZoneTransferClientSettingsGet: jasmine.Spy;
    apiZoneTransferClientSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('ZoneTransferClientSettingsService', [
      'apiZoneTransferClientSettingsGet',
      'apiZoneTransferClientSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiZoneTransferClientSettingsGet.and.returnValue(
      of({
        primaryPort: 53,
        connectTimeout: '00:00:05',
        readTimeout: '00:00:10',
        soaQueryTimeout: '00:00:03',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [ZoneTransferClientSettingsComponent],
      providers: [
        { provide: ZoneTransferClientSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ZoneTransferClientSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiZoneTransferClientSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.readTimeout.value).toBe('00:00:10');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reloaded note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiZoneTransferClientSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ primaryPort: 5300 });
    await fixture.componentInstance.save();

    expect(apiSpy.apiZoneTransferClientSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ primaryPort: 5300 }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiZoneTransferClientSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
