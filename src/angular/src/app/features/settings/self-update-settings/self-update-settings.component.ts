import { Component, OnInit, inject, signal } from '@angular/core';
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
import { SelfUpdateService, SelfUpdateSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component reads and writes this host's own `SelfUpdateOptions` (whether, how often, and
 * against what feed this server checks for a newer release of itself) via `GET`/`PATCH
 * /api/self-update-settings`, and separately surfaces the checker's own currently-known status
 * (`GET /api/self-update`) plus a real, on-demand "Check now" action (`POST
 * /api/self-update/check`) -- deliberately **not** an "update now" button, since this server has
 * no safe, in-process way to replace its own running binary yet; see the backend's own
 * `SelfUpdateController.Get` doc comment for the full reasoning this page's own status card
 * mirrors in `updateInstructions`.
 * <p>
 * Checking is on by default (a pure availability/correctness notice, no new attack surface) but
 * has nothing to check against until an operator sets a release feed URL here -- this project has
 * no fixed release-hosting location of its own yet.
 */
@Component({
  selector: 'app-self-update-settings',
  imports: [
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

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly checking = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** This host's own currently-known update status, or `null` until the first read completes. */
  readonly currentVersion = signal<string | null>(null);
  readonly latestKnownVersion = signal<string | null>(null);
  readonly updateAvailable = signal(false);
  readonly updateInstructions = signal<string | null>(null);
  readonly lastCheckedAtUtc = signal<string | null>(null);
  readonly lastCheckError = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enableAutomaticCheck: [true],
    releaseFeedUrl: [''],
    checkInterval: ['1.00:00:00', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadSettings();
    void this.loadStatus();
  }

  /** This method PATCHes every field this form holds, then surfaces the backend's own hot-reload note. */
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

  /** This method triggers a real, immediate check against the currently-configured feed and reloads this page's own status display with the result. */
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
    try {
      const status = await firstValueFrom(this.selfUpdateApi.apiSelfUpdateGet());
      this.applyStatus(status);
    } catch {
      // A status read failure is surfaced only through the settings form's own error banner --
      // the status card simply stays at its own initial, empty state.
    }
  }

  private applyStatus(status: {
    currentVersion?: string | null;
    latestKnownVersion?: string | null;
    updateAvailable?: boolean;
    updateInstructions?: string | null;
    lastCheckedAtUtc?: string | null;
    lastCheckError?: string | null;
  }): void {
    this.currentVersion.set(status.currentVersion ?? null);
    this.latestKnownVersion.set(status.latestKnownVersion ?? null);
    this.updateAvailable.set(status.updateAvailable ?? false);
    this.updateInstructions.set(status.updateInstructions ?? null);
    this.lastCheckedAtUtc.set(status.lastCheckedAtUtc ?? null);
    this.lastCheckError.set(status.lastCheckError ?? null);
  }
}
