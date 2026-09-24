import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { DnssecKeyRolloverSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { DnssecKeyRolloverSettingsComponent } from './dnssec-key-rollover-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('DnssecKeyRolloverSettingsComponent', () => {
  let apiSpy: {
    apiDnssecKeyRolloverSettingsGet: jasmine.Spy;
    apiDnssecKeyRolloverSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('DnssecKeyRolloverSettingsService', [
      'apiDnssecKeyRolloverSettingsGet',
      'apiDnssecKeyRolloverSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiDnssecKeyRolloverSettingsGet.and.returnValue(
      of({
        propagationDelay: '01:00:00',
        externalParentDsTtl: '1.00:00:00',
        pollInterval: '00:15:00',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [DnssecKeyRolloverSettingsComponent],
      providers: [
        { provide: DnssecKeyRolloverSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(DnssecKeyRolloverSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiDnssecKeyRolloverSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.externalParentDsTtl.value).toBe('1.00:00:00');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the hot-reloaded note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDnssecKeyRolloverSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ externalParentDsTtl: '12:00:00' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiDnssecKeyRolloverSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ externalParentDsTtl: '12:00:00' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDnssecKeyRolloverSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
