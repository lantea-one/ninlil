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
import { DnsPipelineSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component reads and writes this host's own `DnsQueryProcessingPipelineOptions`
 * (`GET`/`PATCH /api/dns-pipeline-settings`) -- worker-pool/queue sizing plus UDP
 * amplification-guard tuning. No secret material is involved, so this is a plain
 * read/PATCH-with-current-value round trip -- built from {@link ZoneSigningSettingsComponent}'s
 * own reference pattern.
 * <p>
 * <b>Only half this options class's own fields genuinely hot-reload.</b> `workerCount`/
 * `channelCapacity` size a fixed worker pool and queue built once, when this pipeline's own
 * service starts -- a change to either needs that one service specifically restarted (from the
 * Services page), not a full process restart. Every other field takes effect immediately. The
 * backend's own returned note names exactly which case a given save falls into, so this page
 * surfaces it unconditionally rather than guessing, alongside a static hint next to the two
 * restart-needing fields themselves.
 */
@Component({
  selector: 'app-dns-pipeline-settings',
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
  templateUrl: './dns-pipeline-settings.component.html',
  styleUrl: './dns-pipeline-settings.component.scss',
})
export class DnsPipelineSettingsComponent implements OnInit {
  private readonly pipelineApi = inject(DnsPipelineSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    workerCount: [4, [Validators.required, Validators.min(1)]],
    channelCapacity: [4096, [Validators.required, Validators.min(1)]],
    maxUdpPayloadSizeOctets: [
      1232,
      [Validators.required, Validators.min(1), Validators.max(65535)],
    ],
    maxUdpAmplificationFactor: [20, [Validators.required, Validators.min(0.01)]],
    minUdpAmplificationFloorSizeOctets: [
      512,
      [Validators.required, Validators.min(1), Validators.max(65535)],
    ],
  });

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every field this form holds, then surfaces the backend's own note (which one of its three cases applies depends on which fields changed). */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.pipelineApi.apiDnsPipelineSettingsPatch({ body: this.form.getRawValue() }),
      );
      this.notification.success(result.note ?? 'Pipeline settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save pipeline settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.pipelineApi.apiDnsPipelineSettingsGet());
      this.form.patchValue({
        workerCount: settings.workerCount ?? 4,
        channelCapacity: settings.channelCapacity ?? 4096,
        maxUdpPayloadSizeOctets: settings.maxUdpPayloadSizeOctets ?? 1232,
        maxUdpAmplificationFactor: settings.maxUdpAmplificationFactor ?? 20,
        minUdpAmplificationFloorSizeOctets: settings.minUdpAmplificationFloorSizeOctets ?? 512,
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load pipeline settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
