import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { OidcSsoSettingsService } from '../../../api-client';
import { NotificationService } from '../../../shared/notification/notification.service';
import { SsoSettingsComponent } from './sso-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Single Sign-On page: loading, saving with and without a new client secret,
 * validation and a save error. TestBed with a spy-backed settings service and `NotificationService`.
 */
describe('SsoSettingsComponent', () => {
  let apiSpy: {
    apiSsoSettingsGet: jasmine.Spy;
    apiSsoSettingsPatch: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(configureApi?: () => void) {
    apiSpy = jasmine.createSpyObj('OidcSsoSettingsService', [
      'apiSsoSettingsGet',
      'apiSsoSettingsPatch',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    apiSpy.apiSsoSettingsGet.and.returnValue(
      of({
        enable: true,
        displayName: 'Sign in with Authelia',
        authority: 'https://auth.example.com',
        clientId: 'ninlil-dns',
        clientSecretConfigured: true,
        scopes: ['openid', 'profile', 'email'],
      }),
    );
    configureApi?.();

    await TestBed.configureTestingModule({
      imports: [SsoSettingsComponent],
      providers: [
        { provide: OidcSsoSettingsService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(SsoSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current effective settings on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiSsoSettingsGet).toHaveBeenCalled();
    expect(fixture.componentInstance.form.controls.enable.value).toBeTrue();
    expect(fixture.componentInstance.form.controls.displayName.value).toBe('Sign in with Authelia');
    expect(fixture.componentInstance.form.controls.authority.value).toBe(
      'https://auth.example.com',
    );
    expect(fixture.componentInstance.form.controls.clientId.value).toBe('ninlil-dns');
    expect(fixture.componentInstance.form.controls.scopesText.value).toBe('openid profile email');
    expect(fixture.componentInstance.clientSecretConfigured()).toBeTrue();
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('save() PATCHes the form without the clientSecret key when left blank, splitting scopes into an array', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSsoSettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. This setting takes effect on the very next read of OidcSsoOptions, but the OpenID Connect authentication scheme itself is only ever registered once, while the application is starting up -- a restart is required.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({
      displayName: 'Sign in with Keycloak',
      scopesText: 'openid, profile,  email',
    });
    await fixture.componentInstance.save();

    const sentBody = apiSpy.apiSsoSettingsPatch.calls.mostRecent().args[0].body;
    expect('clientSecret' in sentBody).toBeFalse();
    expect(sentBody.displayName).toBe('Sign in with Keycloak');
    expect(sentBody.scopes).toEqual(['openid', 'profile', 'email']);
    expect(notificationSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('restart'));
  });

  it('save() includes the clientSecret key when the operator actually typed one, then clears the field', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSsoSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ clientSecret: 'a-real-new-secret' });
    await fixture.componentInstance.save();

    const sentBody = apiSpy.apiSsoSettingsPatch.calls.mostRecent().args[0].body;
    expect(sentBody.clientSecret).toBe('a-real-new-secret');
    // The secret field is cleared, not filled from the response.
    expect(fixture.componentInstance.form.controls.clientSecret.value).toBe('');
    expect(fixture.componentInstance.clientSecretConfigured()).toBeTrue();
  });

  it('rejects a blank authority', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ authority: '' });

    expect(fixture.componentInstance.form.controls.authority.valid).toBeFalse();
    expect(fixture.componentInstance.form.invalid).toBeTrue();
  });

  it('rejects a blank client id', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ clientId: '' });

    expect(fixture.componentInstance.form.controls.clientId.valid).toBeFalse();
  });

  it('rejects a blank scopes field', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ scopesText: '' });

    expect(fixture.componentInstance.form.controls.scopesText.valid).toBeFalse();
  });

  it('shows an error message when saving fails', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSsoSettingsPatch.and.returnValue(
        throwError(() => new HttpErrorResponse({ status: 400, error: 'bad value' })),
      );
    });

    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
  });

  it('shows no override or unreadable callout when neither flag is set', async () => {
    const fixture = await createComponent();

    expect(fixture.nativeElement.textContent).not.toContain('The client secret comes from N');
    expect(fixture.nativeElement.textContent).not.toContain('cannot be decrypted');
  });

  it('shows the override callout when the secret comes from the environment', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSsoSettingsGet.and.returnValue(
        of({ clientSecretFromEnvironment: true, clientSecretConfigured: true }),
      );
    });

    expect(fixture.nativeElement.textContent).toContain(
      'The client secret comes from NINLIL_IDENTITY_SSO_CLIENT_SECRET',
    );
    expect(fixture.nativeElement.textContent).not.toContain('cannot be decrypted');
  });

  it('shows the warning callout when the saved secret is unreadable', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSsoSettingsGet.and.returnValue(of({ clientSecretUnreadable: true }));
    });

    expect(fixture.nativeElement.textContent).toContain(
      'The saved client secret cannot be decrypted',
    );
    expect(fixture.nativeElement.textContent).not.toContain('The client secret comes from N');
  });
});
