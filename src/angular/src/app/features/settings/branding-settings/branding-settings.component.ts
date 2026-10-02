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
import { BrandingSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  MAX_LOGO_SIZE_BYTES,
  readFileAsDataUri,
  validateLogoFile,
} from './branding-settings.model';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Branding" settings page (`/settings/branding`). Through `GET` and
 * `PATCH /api/branding-settings` (`branding:read`, `branding:write`) it edits the site title and
 * logo (`BrandingOptions`) and the `AuthoritativeServerOptions` fields behind CHAOS-class
 * `version.bind` and `hostname.bind` answers; the backend saves both in one `PATCH`.
 *
 * <p>A file input cannot be bound to a form control, so {@link onLogoSelected} checks the chosen
 * file with {@link validateLogoFile}, reads it into a data URI with {@link readFileAsDataUri} and
 * puts that string on the `logoDataUri` control. From there it is an ordinary string field sent on
 * save.
 *
 * <p>Every field is hot-reloaded; the backend's note in the success notification says so.
 */
@Component({
  selector: 'app-branding-settings',
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
  templateUrl: './branding-settings.component.html',
  styleUrl: './branding-settings.component.scss',
})
export class BrandingSettingsComponent implements OnInit {
  private readonly brandingApi = inject(BrandingSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** A rejection reason for the most recently selected logo file, or `null` when none was rejected. */
  readonly logoFileError = signal<string | null>(null);

  /** The logo size limit in whole KB, for the hint text. */
  readonly maxLogoSizeKb = Math.floor(MAX_LOGO_SIZE_BYTES / 1024);

  readonly form = this.formBuilder.group({
    siteTitle: [''],
    logoDataUri: [null as string | null],
    hostnameString: ['', [Validators.required]],
    versionString: ['', [Validators.required]],
    enableChaosVersionQueries: [false],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /**
   * This method handles a logo file selection: it validates the file, reads it into a data URI and
   * sets the `logoDataUri` control. Nothing is sent until {@link save}.
   */
  async onLogoSelected(event: Event): Promise<void> {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    input.value = '';

    if (!file) return;

    const rejection = validateLogoFile(file);
    if (rejection) {
      this.logoFileError.set(rejection);
      return;
    }

    this.logoFileError.set(null);

    try {
      const dataUri = await readFileAsDataUri(file);
      this.form.controls.logoDataUri.setValue(dataUri);
      this.form.controls.logoDataUri.markAsDirty();
    } catch {
      this.logoFileError.set('Could not read the selected file.');
    }
  }

  /** This method clears the currently configured (or newly selected, unsaved) logo. */
  removeLogo(): void {
    this.logoFileError.set(null);
    this.form.controls.logoDataUri.setValue(null);
    this.form.controls.logoDataUri.markAsDirty();
  }

  /** This method PATCHes every field, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.brandingApi.apiBrandingSettingsPatch({
          body: {
            // A blank title means "use the built-in default": null clears it in a merge patch,
            // where an empty string would be stored as is.
            siteTitle: value.siteTitle.trim() === '' ? null : value.siteTitle,
            logoDataUri: value.logoDataUri,
            hostnameString: value.hostnameString,
            versionString: value.versionString,
            enableChaosVersionQueries: value.enableChaosVersionQueries,
          },
        }),
      );
      this.notification.success(result.note ?? 'Branding settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save branding settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.brandingApi.apiBrandingSettingsGet());
      this.form.patchValue({
        siteTitle: settings.siteTitle ?? '',
        logoDataUri: settings.logoDataUri ?? null,
        hostnameString: settings.hostnameString ?? '',
        versionString: settings.versionString ?? '',
        enableChaosVersionQueries: settings.enableChaosVersionQueries ?? false,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load branding settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
