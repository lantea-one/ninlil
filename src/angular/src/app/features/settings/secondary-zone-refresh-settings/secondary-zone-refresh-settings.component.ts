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
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Secondary Zone Refresh" settings page (`/settings/secondary-zone-refresh`).
 * It edits `SecondaryZoneRefreshOptions` through `GET` and
 * `PATCH /api/secondary-zone-refresh-settings` (`secondaryzonerefreshsettings:read`,
 * `secondaryzonerefreshsettings:write`): how often the loop that checks whether a secondary zone
 * is due for a refresh wakes up, and the retry interval for a zone with no SOA yet or a stub
 * zone (other zones use their own SOA Retry value). Changes are hot-reloaded.
 */
@Component({
  selector: 'app-secondary-zone-refresh-settings',
  imports: [
    IfPermissionDirective,
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

  /** This method PATCHes both fields, then shows the backend's note. */
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
