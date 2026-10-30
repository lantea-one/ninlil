import { TestBed } from '@angular/core/testing';
import { HttpErrorResponse } from '@angular/common/http';
import { of, throwError } from 'rxjs';
import { OidcSsoSettingsService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { SsoSettingsComponent } from './sso-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Single Sign-On page: loading, saving only the changed fields with and
 * without a new client secret, Save disabled without a change, the baseline moving after a save,
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

    // An administrator's view: every permission held, so the write-only footer renders.
    spyOn(TestBed.inject(AuthService), 'hasPermission').and.returnValue(true);
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

  it('save() PATCHes the changed fields only, without the clientSecret key when left blank, splitting scopes into an array', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSsoSettingsPatch.and.returnValue(
        of({
          note: 'Written to the configuration file. This setting takes effect on the very next read of OidcSsoOptions, but the OpenID Connect authentication scheme itself is only ever registered once, while the application is starting up -- a restart is required.',
        }),
      );
    });

    fixture.componentInstance.form.patchValue({
      displayName: 'Sign in with Keycloak',
      scopesText: 'openid, profile,  email, groups',
    });
    await fixture.componentInstance.save();

    expect(apiSpy.apiSsoSettingsPatch).toHaveBeenCalledOnceWith({
      body: {
        displayName: 'Sign in with Keycloak',
        scopes: ['openid', 'profile', 'email', 'groups'],
      },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(jasmine.stringContaining('restart'));
  });

  it('save() sends displayName alone, so the backend reports a hot reload', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSsoSettingsPatch.and.returnValue(
        of({ note: 'takes effect immediately -- no restart required.' }),
      );
    });

    fixture.componentInstance.form.patchValue({ displayName: 'Sign in with Keycloak' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiSsoSettingsPatch).toHaveBeenCalledOnceWith({
      body: { displayName: 'Sign in with Keycloak' },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
  });

  it('treats scopes retyped with other separators but the same names as unchanged', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.patchValue({ scopesText: 'openid, profile,  email' });

    expect(fixture.componentInstance.hasChanges()).toBeFalse();
  });

  it('disables Save with a tooltip while nothing changed and does not send an empty patch', async () => {
    const fixture = await createComponent();
    const button = (fixture.nativeElement as HTMLElement).querySelector(
      'c-card-footer button',
    ) as HTMLButtonElement;

    expect(button.disabled).toBeTrue();
    expect(button.getAttribute('title')).toBe('No changes to save');

    await fixture.componentInstance.save();
    expect(apiSpy.apiSsoSettingsPatch).not.toHaveBeenCalled();

    // A typed client secret is a change on its own.
    fixture.componentInstance.form.patchValue({ clientSecret: 'a-new-secret' });
    fixture.detectChanges();
    expect(button.disabled).toBeFalse();
    expect(button.hasAttribute('title')).toBeFalse();
  });

  it('save() moves the baseline, so the next save sends only what changed since', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSsoSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ displayName: 'Sign in with Keycloak' });
    await fixture.componentInstance.save();
    expect(fixture.componentInstance.hasChanges()).toBeFalse();

    fixture.componentInstance.form.patchValue({ clientId: 'ninlil-2' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiSsoSettingsPatch.calls.mostRecent().args[0]).toEqual({
      body: { clientId: 'ninlil-2' },
    });
  });

  it('save() includes the clientSecret key when the operator actually typed one, then clears the field', async () => {
    const fixture = await createComponent(() => {
      apiSpy.apiSsoSettingsPatch.and.returnValue(of({ note: 'ok' }));
    });

    fixture.componentInstance.form.patchValue({ clientSecret: 'a-real-new-secret' });
    await fixture.componentInstance.save();

    expect(apiSpy.apiSsoSettingsPatch).toHaveBeenCalledOnceWith({
      body: { clientSecret: 'a-real-new-secret' },
    });
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

    fixture.componentInstance.form.patchValue({ displayName: 'Sign in with Keycloak' });
    await fixture.componentInstance.save();

    expect(fixture.componentInstance.errorMessage()).toBe('bad value');
    // A failed save keeps the edit pending.
    expect(fixture.componentInstance.hasChanges()).toBeTrue();
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
