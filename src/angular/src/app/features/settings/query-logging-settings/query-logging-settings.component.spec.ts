import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { QueryLogPrivacyLevelEnum, QueryLoggingSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { QueryLoggingSettingsComponent } from './query-logging-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

describe('QueryLoggingSettingsComponent', () => {
  let apiSpy: {
    apiQueryLoggingSettingsGet: jasmine.Spy;
    apiQueryLoggingSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('QueryLoggingSettingsService', [
      'apiQueryLoggingSettingsGet',
      'apiQueryLoggingSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiQueryLoggingSettingsGet.and.returnValue(
      of({
        enable: true,
        retentionPeriod: '30.00:00:00',
        maxRetainedEntries: 1000000,
        cleanupInterval: '01:00:00',
        privacyLevel: QueryLogPrivacyLevelEnum.Full,
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [QueryLoggingSettingsComponent],
      providers: [
        { provide: QueryLoggingSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(QueryLoggingSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiQueryLoggingSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.retentionPeriod.value).toBe('30.00:00:00');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the full form, then surfaces the restart-required note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiQueryLoggingSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... a restart is required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ enable: false });
    await fixture.componentInstance.save();

    expect(apiSpy.apiQueryLoggingSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ enable: false, retentionPeriod: '30.00:00:00' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('a restart is required'),
    );
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiQueryLoggingSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });

  it('defaults the privacy level to Full and shows its own description', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.form.controls.privacyLevel.value).toBe(
      QueryLogPrivacyLevelEnum.Full,
    );
    expect(fixture.componentInstance.selectedPrivacyLevelDescription()).toContain(
      'stored exactly as observed',
    );
  });

  it('reflects a changed privacy level selection in its own description', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({
      privacyLevel: QueryLogPrivacyLevelEnum.FullyAnonymous,
    });

    expect(fixture.componentInstance.selectedPrivacyLevelDescription()).toContain(
      'aggregate counts',
    );
  });

  it('save() includes privacyLevel in the PATCH body and surfaces a mixed-reload note verbatim', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiQueryLoggingSettingsPatch.and.returnValue(
        of({
          note: "Written to the configuration file. 'privacyLevel' took effect immediately, no restart required.",
        }),
      );
    });

    fixture.componentInstance.form.patchValue({
      privacyLevel: QueryLogPrivacyLevelEnum.ClientAnonymized,
    });
    await fixture.componentInstance.save();

    expect(apiSpy.apiQueryLoggingSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ privacyLevel: QueryLogPrivacyLevelEnum.ClientAnonymized }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });
});
