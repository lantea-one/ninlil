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
 * This component reads and writes this host's own `OidcSsoOptions` -- the generic, standards-
 * compliant OpenID Connect admin-login federation surface, via `GET`/`PATCH /api/sso-settings`
 * (`ssosettings:read`/`ssosettings:write`). Works against any real OIDC issuer (Authelia,
 * Authentik, Keycloak, or any other) -- this form asks only for the standard fields every
 * OpenID Connect client needs (issuer, client id/secret, requested scopes), never anything
 * provider-specific.
 *
 * <p><strong>Every field written here still needs a process restart to actually change whether
 * federated sign-in is available</strong> -- unlike almost every other runtime settings page in
 * this app, `enable`/`authority`/`clientId` are read only once, while the OpenID Connect
 * authentication scheme itself is registered, before this host's own dependency injection
 * container even exists to serve a live settings read. `displayName`/`scopes` alone would take
 * effect immediately, but since a real change to this feature is rarely useful without also
 * touching `enable`/`authority`/`clientId` in the same edit, this page shows the backend's own
 * write-result note verbatim rather than trying to draw that fine distinction in its own copy.
 *
 * <p>Like {@link SmtpSettingsComponent}'s own `password` field, `clientSecret` here is a genuine
 * secret this page can never pre-fill (`OidcSsoSettingsController.Get` never returns it) --
 * leaving it blank on save means "leave the stored secret unchanged," never "clear it." See
 * {@link buildClientSecretPatchField}'s own doc comment for the full reasoning.
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

  /** Whether a client secret is currently on file, per the last `Get` -- shown as a hint, never as a value. */
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
   * This method PATCHes every field this form holds, omitting `clientSecret` entirely when left
   * blank (see {@link buildClientSecretPatchField}), then surfaces the backend's own write-result
   * note -- including its restart-required caveat, shown to the operator exactly as the backend
   * phrased it rather than paraphrased here.
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

      // Never re-populate the secret field from the response -- it is never returned, so
      // clearing it back to blank keeps this form's own "blank means unchanged" contract honest.
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
