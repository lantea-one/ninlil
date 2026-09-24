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
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component reads and writes this host's own `PtrSyncOptions` global defaults (`GET`/`PATCH
 * /api/ptr-sync-settings`): whether a `Primary`/`CloudPrimary` zone's own A/AAAA record set is
 * automatically mirrored into its enclosing reverse zone, and the IPv4/IPv6 reverse-zone grouping
 * granularity used whenever a zone's own per-zone overrides (`ZoneEntity.AutoManagePtrRecords`/
 * `PtrIpv4PrefixLength`/`PtrIpv6PrefixLength`, all already editable through the zone form itself)
 * are left unset. The enable toggle mirrors {@link FilteringSettingsComponent}'s own single-
 * `Enable`-toggle reference pattern; the two prefix-length fields are new, validated numeric
 * inputs sharing the exact same 24-30 (IPv4) / 16-124-multiple-of-4 (IPv6) bounds the zone form's
 * own per-zone override fields already enforce.
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
    defaultIpv6ReverseZonePrefixBits: [64, [Validators.min(16), Validators.max(124)]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes the global default, then surfaces the backend's own hot-reloaded note. */
  async save(): Promise<void> {
    if (this.submitting()) return;

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
