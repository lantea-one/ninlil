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

/**
 * This component reads and writes this host's own `DnsRateLimitOptions` (RFC-agnostic, per-client
 * token-bucket rate limiting) via `GET`/`PATCH /api/rate-limiting-settings`. No secret material is
 * involved here at all, so every field is a plain read/PATCH-with-current-value round trip --
 * unlike {@link EventBrokerSettingsComponent}'s own password field, nothing here needs a "leave
 * blank to mean unchanged" special case. Every successful `PATCH`'s own "restart required" note is
 * surfaced directly in the success notification, never suppressed.
 */
@Component({
  selector: 'app-rate-limiting-settings',
  imports: [
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

  /** When this settings section was last written and by which cluster node, per the cluster's own config-sync mechanism. */
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

  /** This method PATCHes every field this form holds, then surfaces the backend's own restart-required note. */
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
      // The cluster's own sync bookkeeping is written synchronously alongside the settings file
      // itself, so a fresh GET right after a successful PATCH always reflects it -- unlike the
      // settings values themselves, which this project's own IOptions-vs-IOptionsMonitor
      // distinction may still require a restart for.
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
