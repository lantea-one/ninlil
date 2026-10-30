import { Component, OnInit, inject, signal } from '@angular/core';
import { DatePipe } from '@angular/common';
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
  SelfUpdateService,
  SelfUpdateSettingsService,
  SelfUpdateStatusDetail,
} from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Self-Update" settings page (`/settings/self-update`). It edits
 * `SelfUpdateOptions` (whether, how often and against which feed the server checks for a newer
 * release) through `GET` and `PATCH /api/self-update-settings` (`selfupdatesettings:read`,
 * `selfupdatesettings:write`; hot-reloaded). It also shows the checker's status from
 * `GET /api/self-update` (`selfupdate:read`) and offers "Check now"
 * (`POST /api/self-update/check`, `selfupdate:manage`). There is no "update now": the server cannot
 * replace its own running binary, so the status card shows the backend's `updateInstructions`
 * instead (see `SelfUpdateController.Get`).
 * <p>
 * The status card needs `selfupdate:read`, which the page's own permission
 * (`selfupdatesettings:read`) does not imply. Without it the page makes no status request and the
 * card says which permission is missing; any other status failure shows its message in the card.
 * <p>
 * Checking is on by default but does nothing until a release feed URL is set here, since there is
 * no built-in release location.
 */
@Component({
  selector: 'app-self-update-settings',
  imports: [
    DatePipe,
    IfPermissionDirective,
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
  templateUrl: './self-update-settings.component.html',
  styleUrl: './self-update-settings.component.scss',
})
export class SelfUpdateSettingsComponent implements OnInit {
  private readonly selfUpdateSettingsApi = inject(SelfUpdateSettingsService);
  private readonly selfUpdateApi = inject(SelfUpdateService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly auth = inject(AuthService);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly checking = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether the user holds `selfupdate:read`, which `GET /api/self-update` requires. */
  readonly canReadStatus = this.auth.hasPermission('selfupdate:read');

  /** The message of a failed status read, shown in the status card; `null` when none failed. */
  readonly statusError = signal<string | null>(null);

  /** The running version, from the update status; `null` until the first read completes. */
  readonly currentVersion = signal<string | null>(null);
  readonly latestKnownVersion = signal<string | null>(null);
  readonly updateAvailable = signal(false);
  readonly updateInstructions = signal<string | null>(null);
  readonly lastCheckedAtUtc = signal<string | null>(null);
  readonly lastCheckError = signal<string | null>(null);
  /** The latest known release's notes, as the release feed wrote them (plain text). */
  readonly releaseNotes = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enableAutomaticCheck: [true],
    releaseFeedUrl: [''],
    checkInterval: ['1.00:00:00', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadSettings();
    void this.loadStatus();
  }

  /** This method PATCHes every field, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.selfUpdateSettingsApi.apiSelfUpdateSettingsPatch({ body: value }),
      );
      this.notification.success(result.note ?? 'Self-update settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save self-update settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method checks the configured feed now and shows the resulting status. */
  async checkNow(): Promise<void> {
    if (this.checking()) return;

    this.checking.set(true);
    this.errorMessage.set(null);

    try {
      const status = await firstValueFrom(this.selfUpdateApi.apiSelfUpdateCheckPost());
      this.applyStatus(status);
      this.notification.success(
        status.updateAvailable
          ? `A newer version (${status.latestKnownVersion}) is available.`
          : 'Checked -- no newer version is available.',
      );
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not check for an update.'));
    } finally {
      this.checking.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.selfUpdateSettingsApi.apiSelfUpdateSettingsGet());
      this.form.patchValue({
        enableAutomaticCheck: settings.enableAutomaticCheck ?? true,
        releaseFeedUrl: settings.releaseFeedUrl ?? '',
        checkInterval: settings.checkInterval ?? '1.00:00:00',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load self-update settings.'));
    } finally {
      this.loading.set(false);
    }
  }

  private async loadStatus(): Promise<void> {
    // The request would answer 403; the card names the missing permission instead.
    if (!this.canReadStatus) return;

    try {
      const status = await firstValueFrom(this.selfUpdateApi.apiSelfUpdateGet());
      this.applyStatus(status);
    } catch (error) {
      this.statusError.set(extractErrorMessage(error, 'Could not load the update status.'));
    }
  }

  private applyStatus(status: SelfUpdateStatusDetail): void {
    this.currentVersion.set(status.currentVersion ?? null);
    this.latestKnownVersion.set(status.latestKnownVersion ?? null);
    this.updateAvailable.set(status.updateAvailable ?? false);
    this.updateInstructions.set(status.updateInstructions ?? null);
    this.lastCheckedAtUtc.set(status.lastCheckedAtUtc ?? null);
    this.lastCheckError.set(status.lastCheckError ?? null);
    this.releaseNotes.set(status.releaseNotes?.trim() || null);
  }
}
