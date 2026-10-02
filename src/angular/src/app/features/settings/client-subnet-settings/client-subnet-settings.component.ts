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
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { ClientSubnetSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Client Subnet (ECS)" settings page (`/settings/client-subnet`). It edits
 * `EdnsClientSubnetOptions` (RFC 7871 EDNS Client Subnet) through `GET` and
 * `PATCH /api/client-subnet-settings` (`clientsubnetsettings:read`, `clientsubnetsettings:write`).
 * Every field is hot-reloaded. ECS is off by default, because it tells upstream servers part of
 * the client's network location, so the opt-in switch comes first on the form.
 */
@Component({
  selector: 'app-client-subnet-settings',
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
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './client-subnet-settings.component.html',
  styleUrl: './client-subnet-settings.component.scss',
})
export class ClientSubnetSettingsComponent implements OnInit {
  private readonly clientSubnetApi = inject(ClientSubnetSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enable: [false],
    sourcePrefixLengthIPv4: [24, [Validators.required, Validators.min(0), Validators.max(32)]],
    sourcePrefixLengthIPv6: [56, [Validators.required, Validators.min(0), Validators.max(128)]],
    overrideAddress: [''],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field (a blank override address as null), then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const body = {
      ...value,
      overrideAddress: value.overrideAddress.trim() === '' ? null : value.overrideAddress.trim(),
    };

    try {
      const result = await firstValueFrom(
        this.clientSubnetApi.apiClientSubnetSettingsPatch({ body }),
      );
      this.notification.success(result.note ?? 'Client Subnet settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save Client Subnet settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.clientSubnetApi.apiClientSubnetSettingsGet());
      this.form.patchValue({
        enable: settings.enable ?? false,
        sourcePrefixLengthIPv4: settings.sourcePrefixLengthIPv4 ?? 24,
        sourcePrefixLengthIPv6: settings.sourcePrefixLengthIPv6 ?? 56,
        overrideAddress: settings.overrideAddress ?? '',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load Client Subnet settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
