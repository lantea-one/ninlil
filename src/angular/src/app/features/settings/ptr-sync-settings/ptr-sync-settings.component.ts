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
  FormLabelDirective,
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { PtrSyncSettingsService } from '../../../api-client';
import { ptrIpv6PrefixLengthValidator } from '../../zones/zone.model';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "PTR Sync" settings page (`/settings/ptr-sync`). It edits the
 * `PtrSyncOptions` defaults through `GET` and `PATCH /api/ptr-sync-settings`
 * (`ptrsyncsettings:read`, `ptrsyncsettings:write`): whether a Primary or CloudPrimary zone's A and
 * AAAA records are mirrored as PTR records into the reverse zone, and the IPv4 and IPv6
 * reverse-zone prefix lengths. A zone's own overrides on the zone form take precedence. The
 * prefix fields use the zone form's ranges (IPv4 24 to 30, IPv6 16 to 124, a multiple of 4, the same
 * `ptrIpv6PrefixLengthValidator`); Save stays disabled and `save()` sends nothing while the form is
 * invalid. Changes are hot-reloaded.
 */
@Component({
  selector: 'app-ptr-sync-settings',
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
    FormLabelDirective,
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './ptr-sync-settings.component.html',
  styleUrl: './ptr-sync-settings.component.scss',
})
export class PtrSyncSettingsComponent implements OnInit {
  private readonly ptrSyncApi = inject(PtrSyncSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    defaultEnabled: [false],
    defaultIpv4ReverseZonePrefixBits: [24, [Validators.min(24), Validators.max(30)]],
    defaultIpv6ReverseZonePrefixBits: [64, [ptrIpv6PrefixLengthValidator]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes the defaults, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.submitting() || this.form.invalid) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.ptrSyncApi.apiPtrSyncSettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'PTR sync settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save PTR sync settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.ptrSyncApi.apiPtrSyncSettingsGet());
      this.form.patchValue({
        defaultEnabled: settings.defaultEnabled ?? false,
        defaultIpv4ReverseZonePrefixBits: settings.defaultIpv4ReverseZonePrefixBits ?? 24,
        defaultIpv6ReverseZonePrefixBits: settings.defaultIpv6ReverseZonePrefixBits ?? 64,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load PTR sync settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
