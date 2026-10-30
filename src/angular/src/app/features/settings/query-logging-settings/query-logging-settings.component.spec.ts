import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { QueryLogPrivacyLevelEnum, QueryLoggingSettingsService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { QueryLoggingSettingsComponent } from './query-logging-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Query logging page: loading, saving only the changed fields with the
 * backend note, Save disabled without a change, the baseline moving after a save, the privacy
 * level description and a save error. TestBed with a spy-backed settings service and
 * `NotificationService`.
 */
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

    // An administrator's view: every permission held, so the write-only footer renders.
    spyOn(TestBed.inject(AuthService), 'hasPermission').and.returnValue(true);
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

  it('save() PATCHes only the changed field, then surfaces the restart-required note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiQueryLoggingSettingsPatch.and.returnValue(
        of({ note: 'Written to the configuration file. ... a restart is required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ enable: false });
    await fixture.componentInstance.save();

    expect(apiSpy.apiQueryLoggingSettingsPatch).toHaveBeenCalledOnceWith({
      body: { enable: false },
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

    fixture.componentInstance.form.patchValue({ enable: false });
    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
    // A failed save keeps the edit pending.
    expect(fixture.componentInstance.hasChanges()).toBeTrue();
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

  it('save() sends privacyLevel alone, without the restart-only fields, and surfaces a mixed-reload note verbatim', async () => {
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

    expect(apiSpy.apiQueryLoggingSettingsPatch).toHaveBeenCalledOnceWith({
      body: { privacyLevel: QueryLogPrivacyLevelEnum.ClientAnonymized },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('disables Save with a tooltip while nothing changed and does not send an empty patch', async () => {
    const fixture = await createComponent();
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      'c-card-footer button',
    ) as HTMLButtonElement;

    expect(button.disabled).toBeTrue();
    expect(button.getAttribute('title')).toBe('No changes to save');

    await fixture.componentInstance.save();
    expect(apiSpy.apiQueryLoggingSettingsPatch).not.toHaveBeenCalled();

    fixture.componentInstance.form.patchValue({ enable: false });
    fixture.detectChanges();
    expect(button.disabled).toBeFalse();
    expect(button.hasAttribute('title')).toBeFalse();
  });

  it('save() moves the baseline, so the next save sends only what changed since', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiQueryLoggingSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ enable: false });
    await fixture.componentInstance.save();
    expect(fixture.componentInstance.hasChanges()).toBeFalse();

    fixture.componentInstance.form.patchValue({ cleanupInterval: '02:00:00' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiQueryLoggingSettingsPatch.calls.mostRecent().args[0]).toEqual({
      body: { cleanupInterval: '02:00:00' },
    });
  });
});
