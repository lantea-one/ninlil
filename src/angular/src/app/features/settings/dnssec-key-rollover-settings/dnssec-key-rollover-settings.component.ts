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
import { DnssecKeyRolloverSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "DNSSEC Key Rollover" settings page (`/settings/dnssec-key-rollover`). It
 * edits `DnssecKeyRolloverOptions` through `GET` and `PATCH /api/dnssec-key-rollover-settings`
 * (`dnsseckeyrolloversettings:read`, `dnsseckeyrolloversettings:write`): the timing
 * `DnssecKeyRolloverBackgroundService` uses to walk locally signed zones' keys through RFC 6781
 * rollovers (Pre-Publish for a ZSK, §4.1.1.1; Double-Signature for a KSK, §4.1.2).
 * `externalParentDsTtl` stands in for the DS record TTL at an external registrar, which the server
 * cannot observe. Every field is hot-reloaded.
 */
@Component({
  selector: 'app-dnssec-key-rollover-settings',
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
  templateUrl: './dnssec-key-rollover-settings.component.html',
  styleUrl: './dnssec-key-rollover-settings.component.scss',
})
export class DnssecKeyRolloverSettingsComponent implements OnInit {
  private readonly rolloverApi = inject(DnssecKeyRolloverSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    propagationDelay: ['01:00:00', [Validators.required]],
    externalParentDsTtl: ['1.00:00:00', [Validators.required]],
    pollInterval: ['00:15:00', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.rolloverApi.apiDnssecKeyRolloverSettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'DNSSEC key rollover settings saved.');
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not save DNSSEC key rollover settings.'),
      );
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.rolloverApi.apiDnssecKeyRolloverSettingsGet());
      this.form.patchValue({
        propagationDelay: settings.propagationDelay ?? '01:00:00',
        externalParentDsTtl: settings.externalParentDsTtl ?? '1.00:00:00',
        pollInterval: settings.pollInterval ?? '00:15:00',
      });
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load DNSSEC key rollover settings.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
