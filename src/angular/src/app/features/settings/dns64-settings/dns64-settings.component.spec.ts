import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { Dns64SettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { Dns64SettingsComponent } from './dns64-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the DNS64 page: loading, saving, a blank prefix and a save error. TestBed with a
 * spy-backed settings service and `NotificationService`.
 */
describe('Dns64SettingsComponent', () => {
  let apiSpy: {
    apiDns64SettingsGet: jasmine.Spy;
    apiDns64SettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('Dns64SettingsService', [
      'apiDns64SettingsGet',
      'apiDns64SettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiDns64SettingsGet.and.returnValue(
      of({
        enable: false,
        synthesisPrefix: '64:ff9b::/96',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [Dns64SettingsComponent],
      providers: [
        { provide: Dns64SettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(Dns64SettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiDns64SettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enable.value).toBeFalse();
    expect(fixture.componentInstance.form.controls.synthesisPrefix.value).toBe('64:ff9b::/96');
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the form', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDns64SettingsPatch.and.returnValue(
        of({ note: 'takes effect immediately -- no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ enable: true });
    await fixture.componentInstance.save();

    expect(apiSpy.apiDns64SettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({ enable: true, synthesisPrefix: '64:ff9b::/96' }),
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('does not save when the synthesis prefix is blank', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDns64SettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ synthesisPrefix: '' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiDns64SettingsPatch).not.toHaveBeenCalled();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiDns64SettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad prefix' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad prefix');
  });
});
