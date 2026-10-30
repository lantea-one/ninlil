import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
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
import { firstValueFrom, map } from 'rxjs';
import { OidcSsoSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { buildClientSecretPatchField, joinScopes, splitScopes } from './sso-settings.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import {
  changedFields,
  hasChangedFields,
} from '../../../shared/settings-patch/changed-fields.util';

/** The `PATCH /api/sso-settings` fields the page loads and compares; the client secret is not one. */
interface SsoSettingsBody {
  enable: boolean;
  displayName: string;
  authority: string;
  clientId: string;
  scopes: string[];
}

/** This function reads the compared fields out of the form value, splitting the scopes text. */
function toSettingsBody(value: {
  enable: boolean;
  displayName: string;
  authority: string;
  clientId: string;
  scopesText: string;
}): SsoSettingsBody {
  return {
    enable: value.enable,
    displayName: value.displayName,
    authority: value.authority,
    clientId: value.clientId,
    scopes: splitScopes(value.scopesText),
  };
}

/**
 * This component is the "Single Sign-On" settings page (`/settings/sso`). It edits
 * `OidcSsoOptions`, OpenID Connect sign-in for the admin console, through `GET` and
 * `PATCH /api/sso-settings` (`ssosettings:read`, `ssosettings:write`). It asks only for the
 * standard OIDC client fields (issuer, client id and secret, scopes), so any issuer works
 * (Authelia, Authentik, Keycloak and others).
 *
 * <p>The OpenID Connect scheme is registered at startup, so changes to `enable`, `authority`,
 * `clientId`, the client secret and scopes need a restart; `displayName` is read live. The backend
 * answers "no restart needed" only for a request that holds `displayName` alone, so the page sends
 * only the fields that changed since the last load or save (`changedFields`) and shows the
 * backend's note as returned. Save stays disabled while nothing changed and no secret is typed.
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

  /**
   * Whether `NINLIL_IDENTITY_SSO_CLIENT_SECRET` supplies the secret, per the last `Get`. The
   * environment value overrides the one saved here, so the page says so.
   */
  readonly clientSecretFromEnvironment = signal(false);

  /**
   * Whether the saved secret cannot be decrypted with this server's key ring, per the last `Get`.
   * The operator must enter it again.
   */
  readonly clientSecretUnreadable = signal(false);

  readonly form = this.formBuilder.group({
    enable: [false],
    displayName: ['Single Sign-On', [Validators.required]],
    authority: ['', [Validators.required]],
    clientId: ['', [Validators.required]],
    clientSecret: [''],
    scopesText: ['openid profile email', [Validators.required]],
  });

  /** The form's value after every edit, so the difference below follows the inputs. */
  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  /**
   * The fields the page loads, as the request names them (the scopes as a list), as last loaded
   * or saved. The client secret is never loaded, so it is not part of the baseline.
   */
  private readonly baseline = signal<SsoSettingsBody | null>(null);

  /**
   * The fields edited since the last load or save, the body of the next `PATCH`. A typed client
   * secret is added whole (see {@link buildClientSecretPatchField}); a blank one is left out.
   */
  readonly changes = computed(() => {
    const baseline = this.baseline();
    if (baseline === null) {
      return {};
    }

    const current = this.formValue();
    return {
      ...changedFields(baseline, toSettingsBody(current)),
      ...buildClientSecretPatchField(current.clientSecret),
    };
  });

  /** Whether any field was edited, or a client secret typed, since the last load or save. */
  readonly hasChanges = computed(() => hasChangedFields(this.changes()));

  ngOnInit(): void {
    void this.loadSettings();
  }

  /**
   * This method PATCHes the changed fields only (a typed client secret included, a blank one
   * left out), then shows the backend's note.
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting() || !this.hasChanges()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { clientSecret } = this.form.getRawValue();
    const saved = toSettingsBody(this.form.getRawValue());

    try {
      const result = await firstValueFrom(
        this.ssoApi.apiSsoSettingsPatch({ body: this.changes() }),
      );
      this.baseline.set(saved);
      this.notification.success(result.note ?? 'SSO settings saved.');

      if (clientSecret.trim().length > 0) {
        this.clientSecretConfigured.set(true);
        // A freshly saved secret is readable again; an environment override still wins.
        this.clientSecretUnreadable.set(false);
      }

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
      this.clientSecretFromEnvironment.set(settings.clientSecretFromEnvironment ?? false);
      this.clientSecretUnreadable.set(settings.clientSecretUnreadable ?? false);
      this.form.patchValue({
        enable: settings.enable ?? false,
        displayName: settings.displayName ?? 'Single Sign-On',
        authority: settings.authority ?? '',
        clientId: settings.clientId ?? '',
        scopesText: joinScopes(settings.scopes) || 'openid profile email',
      });
      this.baseline.set(toSettingsBody(this.form.getRawValue()));
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load SSO settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
