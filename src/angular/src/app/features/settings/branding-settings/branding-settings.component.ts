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
 * This component reads and writes this host's own global branding (`GET`/`PATCH
 * /api/branding-settings`, `branding:read`/`branding:write`): a custom site title/logo
 * (`BrandingOptions`), plus the already-existing `AuthoritativeServerOptions` fields that drive
 * real CHAOS-class `version.bind`/`hostname.bind` TXT query answers -- exposed together here
 * rather than as a second, unrelated page, since the backend itself merges both through the same
 * `PATCH` action. Built from {@link FilteringSettingsComponent}'s own reference pattern (a plain
 * checkbox alongside ordinary text fields).
 *
 * <p>The logo is never a dedicated file-upload endpoint -- there is no `<input type="file">`-bound
 * form control at all, since a native file input's value cannot be data-bound. Instead,
 * {@link onLogoSelected} reads a browser-selected file into a complete data URI, client-side, via
 * {@link readFileAsDataUri}, and writes that string directly onto the `logoDataUri` form control --
 * from that point on it is just an ordinary string field, sent in the `PATCH` body exactly like
 * `siteTitle`. {@link validateLogoFile} rejects an oversized or non-image file before it is ever
 * read, mirroring `BrandingSettingsController.ValidateLogoDataUri`'s own two server-side checks so
 * a doomed upload never reaches the network at all.
 *
 * <p>Every field on this page takes effect immediately, with no restart -- `BrandingOptions` is
 * bound via `IOptionsMonitor` from the start, and `AuthoritativeServerOptions`'s own real consumer
 * (`AuthoritativeQueryResolver`) was converted to the same live-reload shape alongside this page's
 * own build. The backend's own `PATCH` response note (surfaced directly in the success
 * notification, never suppressed) confirms this explicitly.
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

  /** The real accepted logo size ceiling, in whole KB, surfaced in this page's own hint text. */
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
   * This method handles a real `<input type="file">` selection: validates the file client-side
   * (real image type, within the accepted size), then reads it into a data URI and writes that
   * string directly onto the `logoDataUri` control. Never touches the network on its own -- the
   * new logo is only actually saved once {@link save} is called, exactly like every other field on
   * this form.
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

  /** This method PATCHes every field this form holds, then surfaces the backend's own hot-reload note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.brandingApi.apiBrandingSettingsPatch({
          body: {
            // A blank site title means "use the SPA's own compiled-in default" -- sent as an
            // explicit null, this project's own established merge-patch "clear this field"
            // convention, rather than an empty string the backend would otherwise store literally.
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
