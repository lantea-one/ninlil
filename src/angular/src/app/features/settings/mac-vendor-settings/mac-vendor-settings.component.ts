import { DatePipe, DecimalPipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  BadgeComponent,
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
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import {
  MacVendorDataStatusDetail,
  MacVendorRefreshResultEnum,
  MacVendorSettingsService,
} from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This constant matches a .NET `TimeSpan` in its invariant `[d.]hh:mm:ss[.fffffff]` form, as the
 * backend returns and accepts it. The backend enforces the one-day minimum refresh interval with a
 * 400, shown as the page's error banner.
 */
const TIME_SPAN_PATTERN = /^(\d+\.)?\d{1,2}:\d{2}:\d{2}(\.\d+)?$/;

/** This constant matches an absolute HTTP(S) URL by shape only; the backend checks it fully. */
const HTTP_URL_PATTERN = /^https?:\/\/\S+$/i;

/**
 * This component is the "MAC Vendors" settings page (`/settings/mac-vendors`). It edits
 * `MacVendorLookupOptions` through `GET` and `PATCH /api/mac-vendor-settings`
 * (`macvendorsettings:read`, `macvendorsettings:write`) and shows the vendor data loaded: the
 * built-in snapshot or a newer download, when it was generated, how many prefixes each IEEE
 * registry contributes and how the last refresh went.
 * <p>
 * Lookup works offline from the built-in snapshot, so the settings only control the automatic
 * refresh from the IEEE registry files. "Refresh now" (`POST /api/mac-vendor-settings/refresh`,
 * shown only with `macvendorsettings:manage`) runs one at once; a failed or implausible download
 * is reported and never replaces the loaded data. The data file path is shown read-only, since it
 * can only be set in the configuration file. Changes are hot-reloaded.
 */
@Component({
  selector: 'app-mac-vendor-settings',
  imports: [
    IfPermissionDirective,
    DatePipe,
    DecimalPipe,
    ReactiveFormsModule,
    AlertComponent,
    BadgeComponent,
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
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './mac-vendor-settings.component.html',
  styleUrl: './mac-vendor-settings.component.scss',
})
export class MacVendorSettingsComponent implements OnInit {
  private readonly settingsApi = inject(MacVendorSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly auth = inject(AuthService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly refreshing = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** The loaded vendor data and its refresh history, as last reported by the backend. */
  readonly status = signal<MacVendorDataStatusDetail | null>(null);

  /** Where refreshed data is saved; read-only, set only in the configuration file. */
  readonly dataFilePath = signal<string | null>(null);

  /** Whether this session may trigger an immediate refresh. */
  readonly canRefresh = this.auth.hasPermission('macvendorsettings:manage');

  /** The CoreUI badge color for the last refresh's outcome. */
  readonly resultColor = computed(() => {
    switch (this.status()?.lastRefreshResult) {
      case MacVendorRefreshResultEnum.Updated:
        return 'success';
      case MacVendorRefreshResultEnum.NotModified:
        return 'info';
      case MacVendorRefreshResultEnum.Failed:
        return 'danger';
      default:
        return 'secondary';
    }
  });

  readonly form = this.formBuilder.group({
    enableAutomaticRefresh: [true],
    refreshInterval: ['14.00:00:00', [Validators.required, Validators.pattern(TIME_SPAN_PATTERN)]],
    maLRegistryUrl: ['', [Validators.required, Validators.pattern(HTTP_URL_PATTERN)]],
    maMRegistryUrl: ['', [Validators.required, Validators.pattern(HTTP_URL_PATTERN)]],
    maSRegistryUrl: ['', [Validators.required, Validators.pattern(HTTP_URL_PATTERN)]],
  });

  ngOnInit(): void {
    void this.load();
  }

  /** This method PATCHes every field, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.settingsApi.apiMacVendorSettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'MAC vendor settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save MAC vendor settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method runs an immediate registry refresh and shows its outcome. */
  async refreshNow(): Promise<void> {
    if (this.refreshing()) return;

    this.refreshing.set(true);
    this.errorMessage.set(null);

    try {
      const status = await firstValueFrom(this.settingsApi.apiMacVendorSettingsRefreshPost());
      this.status.set(status);
      if (status.lastRefreshResult === MacVendorRefreshResultEnum.Failed) {
        this.notification.error(
          'The refresh failed; the vendor data already loaded is still in use.',
        );
      } else {
        this.notification.success(
          status.lastRefreshResult === MacVendorRefreshResultEnum.Updated
            ? 'Vendor data updated from the IEEE registries.'
            : 'Vendor data is already current.',
        );
      }
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not refresh vendor data.'));
    } finally {
      this.refreshing.set(false);
    }
  }

  private async load(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.settingsApi.apiMacVendorSettingsGet());
      this.form.patchValue({
        enableAutomaticRefresh: settings.enableAutomaticRefresh ?? true,
        refreshInterval: settings.refreshInterval ?? '14.00:00:00',
        maLRegistryUrl: settings.maLRegistryUrl ?? '',
        maMRegistryUrl: settings.maMRegistryUrl ?? '',
        maSRegistryUrl: settings.maSRegistryUrl ?? '',
      });
      this.status.set(settings.status ?? null);
      this.dataFilePath.set(settings.dataFilePath ?? null);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load MAC vendor settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
