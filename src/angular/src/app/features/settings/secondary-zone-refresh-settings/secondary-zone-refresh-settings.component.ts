import { Component, OnInit, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { SecondaryZoneRefreshSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component reads and writes this host's own `SecondaryZoneRefreshOptions` (`GET`/`PATCH
 * /api/secondary-zone-refresh-settings`) -- how often the background loop that checks whether a
 * secondary zone is due for a refresh wakes up. No secret material is involved, so this is a
 * plain read/PATCH-with-current-value round trip -- built from
 * {@link ZoneSigningSettingsComponent}'s own reference pattern.
 */
@Component({
  selector: 'app-secondary-zone-refresh-settings',
  imports: [
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './secondary-zone-refresh-settings.component.html',
  styleUrl: './secondary-zone-refresh-settings.component.scss',
})
export class SecondaryZoneRefreshSettingsComponent implements OnInit {
  private readonly secondaryZoneRefreshApi = inject(SecondaryZoneRefreshSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    pollInterval: ['00:00:15', [Validators.required]],
    defaultRetryInterval: ['00:00:30', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field this form holds, then surfaces the backend's own hot-reloaded note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.secondaryZoneRefreshApi.apiSecondaryZoneRefreshSettingsPatch({
          body: this.form.getRawValue(),
        }),
      );
      this.notification.success(result.note ?? 'Secondary zone refresh settings saved.');
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not save secondary zone refresh settings.'),
      );
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(
        this.secondaryZoneRefreshApi.apiSecondaryZoneRefreshSettingsGet(),
      );
      this.form.patchValue({
        pollInterval: settings.pollInterval ?? '00:00:15',
        defaultRetryInterval: settings.defaultRetryInterval ?? '00:00:30',
      });
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load secondary zone refresh settings.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
