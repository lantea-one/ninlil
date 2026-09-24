import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { ZoneSigningSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ZoneSigningSettingsComponent } from './zone-signing-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('ZoneSigningSettingsComponent', () => {
  let apiSpy: {
    apiZoneSigningSettingsGet: jasmine.Spy;
    apiZoneSigningSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('ZoneSigningSettingsService', [
      'apiZoneSigningSettingsGet',
      'apiZoneSigningSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiZoneSigningSettingsGet.and.returnValue(
      of({
        signatureValidityPeriod: '30.00:00:00',
        inceptionOffset: '01:00:00',
        refreshBeforeExpiration: '5.00:00:00',
        pollInterval: '06:00:00',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [ZoneSigningSettingsComponent],
      providers: [
        { provide: ZoneSigningSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(ZoneSigningSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiZoneSigningSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.pollInterval.value).toBe('06:00:00');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reloaded note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiZoneSigningSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ signatureValidityPeriod: '7.00:00:00' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiZoneSigningSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ signatureValidityPeriod: '7.00:00:00' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiZoneSigningSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
