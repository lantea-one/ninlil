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
import { TsigSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "TSIG" settings page (`/settings/tsig`). It edits `TsigServiceOptions`
 * through `GET` and `PATCH /api/tsig-settings` (`tsigsettings:read`, `tsigsettings:write`): the
 * clock-skew tolerance (the RFC 8945 "fudge" field) written into every message the server signs.
 * The keys themselves are on the TSIG Keys page, which this page links to. Changes are
 * hot-reloaded.
 */
@Component({
  selector: 'app-tsig-settings',
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
  templateUrl: './tsig-settings.component.html',
  styleUrl: './tsig-settings.component.scss',
})
export class TsigSettingsComponent implements OnInit {
  private readonly tsigApi = inject(TsigSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    defaultFudgeSeconds: [300, [Validators.required, Validators.min(0), Validators.max(65535)]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes the fudge value, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.tsigApi.apiTsigSettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'TSIG settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save TSIG settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.tsigApi.apiTsigSettingsGet());
      this.form.patchValue({ defaultFudgeSeconds: settings.defaultFudgeSeconds ?? 300 });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load TSIG settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
