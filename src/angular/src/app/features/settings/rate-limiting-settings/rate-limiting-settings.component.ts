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
import { RateLimitingSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { ClusterSyncInfoComponent } from '../../../shared/cluster-sync-info/cluster-sync-info.component';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Rate limiting" settings page (`/settings/rate-limiting`). It edits
 * `DnsRateLimitOptions`, the per-client token bucket and per-subnet limits for DNS queries,
 * through `GET` and `PATCH /api/rate-limiting-settings` (`ratelimiting:read`,
 * `ratelimiting:write`). Bypass CIDRs are one per line. Changes need a restart; the backend's note
 * in the success notification says so.
 */
@Component({
  selector: 'app-rate-limiting-settings',
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
    ClusterSyncInfoComponent,
  ],
  templateUrl: './rate-limiting-settings.component.html',
  styleUrl: './rate-limiting-settings.component.scss',
})
export class RateLimitingSettingsComponent implements OnInit {
  private readonly rateLimitingApi = inject(RateLimitingSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** When this section was last written, from the cluster config sync; {@link originatingNodeId} names the node. */
  readonly lastModifiedUtc = signal<string | null | undefined>(null);
  readonly originatingNodeId = signal<string | null | undefined>(null);

  readonly form = this.formBuilder.group({
    enable: [false],
    queriesPerSecond: [20, [Validators.required, Validators.min(0)]],
    burstSize: [40, [Validators.required, Validators.min(1)]],
    trackedClientExpiry: ['00:05:00', [Validators.required]],
    cleanupInterval: ['00:01:00', [Validators.required]],
    maxTrackedClients: [100000, [Validators.required, Validators.min(1)]],
    subnetPrefixLengthIPv4: [24, [Validators.required, Validators.min(0), Validators.max(32)]],
    subnetPrefixLengthIPv6: [56, [Validators.required, Validators.min(0), Validators.max(128)]],
    subnetSampleWindow: ['00:00:10', [Validators.required]],
    subnetUdpQueryLimit: [500, [Validators.required, Validators.min(1)]],
    subnetTcpQueryLimit: [100, [Validators.required, Validators.min(1)]],
    subnetTruncatePercentage: [50, [Validators.required, Validators.min(0), Validators.max(100)]],
    subnetBypassCidrsText: [''],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field, shows the backend's restart note and reloads. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { subnetBypassCidrsText, ...value } = this.form.getRawValue();
    const body = {
      ...value,
      subnetBypassCidrs: subnetBypassCidrsText
        .split('\n')
        .map((line) => line.trim())
        .filter((line) => line.length > 0),
    };

    try {
      const result = await firstValueFrom(
        this.rateLimitingApi.apiRateLimitingSettingsPatch({ body }),
      );
      this.notification.success(result.note ?? 'Rate limiting settings saved.');
      // The cluster sync record is written with the settings file, so a GET right after the
      // PATCH shows the new last-modified time even though the values wait for a restart.
      await this.loadSettings();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save rate limiting settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.rateLimitingApi.apiRateLimitingSettingsGet());
      this.form.patchValue({
        enable: settings.enable ?? false,
        queriesPerSecond: settings.queriesPerSecond ?? 20,
        burstSize: settings.burstSize ?? 40,
        trackedClientExpiry: settings.trackedClientExpiry ?? '00:05:00',
        cleanupInterval: settings.cleanupInterval ?? '00:01:00',
        maxTrackedClients: settings.maxTrackedClients ?? 100000,
        subnetPrefixLengthIPv4: settings.subnetPrefixLengthIPv4 ?? 24,
        subnetPrefixLengthIPv6: settings.subnetPrefixLengthIPv6 ?? 56,
        subnetSampleWindow: settings.subnetSampleWindow ?? '00:00:10',
        subnetUdpQueryLimit: settings.subnetUdpQueryLimit ?? 500,
        subnetTcpQueryLimit: settings.subnetTcpQueryLimit ?? 100,
        subnetTruncatePercentage: settings.subnetTruncatePercentage ?? 50,
        subnetBypassCidrsText: (settings.subnetBypassCidrs ?? []).join('\n'),
      });
      this.lastModifiedUtc.set(settings.lastModifiedUtc);
      this.originatingNodeId.set(settings.originatingNodeId);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load rate limiting settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
