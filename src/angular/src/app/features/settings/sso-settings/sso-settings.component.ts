import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { OidcSsoSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { buildClientSecretPatchField, joinScopes, splitScopes } from './sso-settings.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Single Sign-On" settings page (`/settings/sso`). It edits
 * `OidcSsoOptions`, OpenID Connect sign-in for the admin console, through `GET` and
 * `PATCH /api/sso-settings` (`ssosettings:read`, `ssosettings:write`). It asks only for the
 * standard OIDC client fields (issuer, client id and secret, scopes), so any issuer works
 * (Authelia, Authentik, Keycloak and others).
 *
 * <p>The OpenID Connect scheme is registered at startup, so changes to `enable`, `authority`,
 * `clientId`, the client secret and scopes need a restart; `displayName` is read live. The page
 * shows the backend's note as returned.
 *
 * <p>The client secret is never returned, so a blank secret field means "keep the stored one" (see
 * {@link buildClientSecretPatchField}).
 */
@Component({
  selector: 'app-sso-settings',
  imports: [
    IfPermissionDirective,
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    SpinnerComponent,
  ],
  templateUrl: './sso-settings.component.html',
  styleUrl: './sso-settings.component.scss',
})
export class SsoSettingsComponent implements OnInit {
  private readonly ssoApi = inject(OidcSsoSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether a client secret is stored, per the last `Get`; shown as a hint, never as a value. */
  readonly clientSecretConfigured = signal(false);

  readonly form = this.formBuilder.group({
    enable: [false],
    displayName: ['Single Sign-On', [Validators.required]],
    authority: ['', [Validators.required]],
    clientId: ['', [Validators.required]],
    clientSecret: [''],
    scopesText: ['openid profile email', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /**
   * This method PATCHes every field, leaving out a blank `clientSecret` (see
   * {@link buildClientSecretPatchField}) and splitting the scopes, then shows the backend's note.
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { clientSecret, scopesText, ...rest } = this.form.getRawValue();
    const body = {
      ...rest,
      ...buildClientSecretPatchField(clientSecret),
      scopes: splitScopes(scopesText),
    };

    try {
      const result = await firstValueFrom(this.ssoApi.apiSsoSettingsPatch({ body }));
      this.notification.success(result.note ?? 'SSO settings saved.');

      if (clientSecret.trim().length > 0) this.clientSecretConfigured.set(true);

      // The secret is never returned; clearing the field keeps "blank means unchanged" true.
      this.form.patchValue({ clientSecret: '' });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save SSO settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.ssoApi.apiSsoSettingsGet());
      this.clientSecretConfigured.set(settings.clientSecretConfigured ?? false);
      this.form.patchValue({
        enable: settings.enable ?? false,
        displayName: settings.displayName ?? 'Single Sign-On',
        authority: settings.authority ?? '',
        clientId: settings.clientId ?? '',
        scopesText: joinScopes(settings.scopes) || 'openid profile email',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load SSO settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
