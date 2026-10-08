import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { SmtpSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { SmtpSettingsComponent } from './smtp-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the SMTP page: loading, saving with and without a new password, validation and
 * a save error. TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('SmtpSettingsComponent', () => {
  let apiSpy: {
    apiSmtpSettingsGet: jasmine.Spy;
    apiSmtpSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('SmtpSettingsService', [
      'apiSmtpSettingsGet',
      'apiSmtpSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiSmtpSettingsGet.and.returnValue(
      of({
        smtpHost: 'smtp.example.com',
        smtpPort: 587,
        useTls: true,
        username: 'notifications@example.com',
        passwordConfigured: true,
        fromAddress: 'notifications@example.com',
        fromDisplayName: 'Ninlil',
        publicBaseUrl: 'https://dns.example.com',
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [SmtpSettingsComponent],
      providers: [
        { provide: SmtpSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SmtpSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiSmtpSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.smtpHost.value).toBe('smtp.example.com');
    expect(fixture.componentInstance.form.controls.smtpPort.value).toBe(587);
    expect(fixture.componentInstance.form.controls.publicBaseUrl.value).toBe(
      'https://dns.example.com',
    );
    expect(fixture.componentInstance.passwordConfigured()).toBeTrue();
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the form without the password key when left blank, then surfaces the hot-reload note', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSmtpSettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({ smtpHost: 'smtp2.example.com' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiSmtpSettingsPatch).toHaveBeenCalledWith({
      body: jasmine.objectContaining({
        smtpHost: 'smtp2.example.com',
        publicBaseUrl: 'https://dns.example.com',
      }),
    });
    const sentBody = apiSpy.apiSmtpSettingsPatch.calls.mostRecent().args[0].body;
    expect('password' in sentBody).toBeFalse();
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('takes effect immediately'),
    );
  });

  it('save() includes the password key when the operator actually typed a new one, then clears the field', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSmtpSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ password: 'a-real-new-password' });
    await fixture.componentInstance.save();

    const sentBody = apiSpy.apiSmtpSettingsPatch.calls.mostRecent().args[0].body;
    expect(sentBody.password).toBe('a-real-new-password');
    // The password field is cleared, not filled from the response.
    expect(fixture.componentInstance.form.controls.password.value).toBe('');
    expect(fixture.componentInstance.passwordConfigured()).toBeTrue();
  });

  it('rejects a blank SMTP host', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ smtpHost: '' });

    expect(fixture.componentInstance.form.controls.smtpHost.valid).toBeFalse();
    expect(fixture.componentInstance.form.invalid).toBeTrue();
  });

  it('rejects an out-of-range SMTP port', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ smtpPort: 70000 });

    expect(fixture.componentInstance.form.controls.smtpPort.valid).toBeFalse();
  });

  it('rejects a malformed from address', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ fromAddress: 'not-an-email' });

    expect(fixture.componentInstance.form.controls.fromAddress.valid).toBeFalse();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSmtpSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });
});
