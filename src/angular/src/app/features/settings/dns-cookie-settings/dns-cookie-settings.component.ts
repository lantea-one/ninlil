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
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { DnsCookieSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClusterSyncInfoComponent } from '../../../shared/cluster-sync-info/cluster-sync-info.component';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component reads and writes this host's own `DnsCookieOptions` (RFC 7873 DNS Cookies) via
 * `GET`/`PATCH /api/dns-cookie-settings`. No secret material is involved, mirroring
 * {@link RateLimitingSettingsComponent}'s own exact shape. Every successful `PATCH`'s own
 * "restart required" note is surfaced directly in the success notification, never suppressed.
 */
@Component({
  selector: 'app-dns-cookie-settings',
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
    SpinnerComponent,
    ClusterSyncInfoComponent,
  ],
  templateUrl: './dns-cookie-settings.component.html',
  styleUrl: './dns-cookie-settings.component.scss',
})
export class DnsCookieSettingsComponent implements OnInit {
  private readonly cookieSettingsApi = inject(DnsCookieSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** When this settings section was last written and by which cluster node, per the cluster's own config-sync mechanism. */
  readonly lastModifiedUtc = signal<string | null | undefined>(null);
  readonly originatingNodeId = signal<string | null | undefined>(null);

  readonly form = this.formBuilder.group({
    enable: [false],
    require: [false],
    secretRotationInterval: ['1.00:00:00', [Validators.required]],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field this form holds, then surfaces the backend's own restart-required note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.cookieSettingsApi.apiDnsCookieSettingsPatch({ body: value }),
      );
      this.notification.success(result.note ?? 'DNS Cookie settings saved.');
      await this.loadSettings();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save DNS Cookie settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.cookieSettingsApi.apiDnsCookieSettingsGet());
      this.form.patchValue({
        enable: settings.enable ?? false,
        require: settings.require ?? false,
        secretRotationInterval: settings.secretRotationInterval ?? '1.00:00:00',
      });
      this.lastModifiedUtc.set(settings.lastModifiedUtc);
      this.originatingNodeId.set(settings.originatingNodeId);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load DNS Cookie settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
