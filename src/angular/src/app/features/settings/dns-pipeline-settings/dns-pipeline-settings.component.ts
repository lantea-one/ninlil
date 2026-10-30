import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
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
import { firstValueFrom, map } from 'rxjs';
import { DnsPipelineSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import {
  changedFields,
  hasChangedFields,
} from '../../../shared/settings-patch/changed-fields.util';

/**
 * This component is the "Pipeline" settings page (`/settings/pipeline`). It edits
 * `DnsQueryProcessingPipelineOptions` through `GET` and `PATCH /api/dns-pipeline-settings`
 * (`dnspipelinesettings:read`, `dnspipelinesettings:write`): queue readers, the number of queries
 * answered at once, queue size, and the UDP amplification guard.
 * <p>
 * `workerCount`, `maxConcurrentQueries` and `channelCapacity` size a pool, a set of slots and a
 * queue built when the pipeline service starts, so they apply after that service is restarted
 * (from the Services page) or the server restarts. The other fields are hot-reloaded. The page
 * shows the backend's note, which says which case applies, and a hint beside the restart fields.
 * The backend picks the note from the fields present in the request, so the page sends only the
 * fields that changed since the last load or save (`changedFields`). Save stays disabled while
 * nothing changed.
 */
@Component({
  selector: 'app-dns-pipeline-settings',
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
    maxConcurrentQueries: [1000, [Validators.required, Validators.min(1)]],
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

  /** The form's value after every edit, so the difference below follows the inputs. */
  private readonly formValue = toSignal(
    this.form.valueChanges.pipe(map(() => this.form.getRawValue())),
    { initialValue: this.form.getRawValue() },
  );

  /** The form as last loaded or saved; the fields that differ from it are the ones to send. */
  private readonly baseline = signal<ReturnType<typeof this.form.getRawValue> | null>(null);

  /** The fields edited since the last load or save, the body of the next `PATCH`. */
  readonly changes = computed(() => {
    const baseline = this.baseline();
    return baseline === null ? {} : changedFields(baseline, this.formValue());
  });

  /** Whether any field was edited since the last load or save. */
  readonly hasChanges = computed(() => hasChangedFields(this.changes()));

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes the changed fields only, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting() || !this.hasChanges()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const saved = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.pipelineApi.apiDnsPipelineSettingsPatch({ body: this.changes() }),
      );
      this.baseline.set(saved);
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
        maxConcurrentQueries: settings.maxConcurrentQueries ?? 1000,
        channelCapacity: settings.channelCapacity ?? 4096,
        maxUdpPayloadSizeOctets: settings.maxUdpPayloadSizeOctets ?? 1232,
        maxUdpAmplificationFactor: settings.maxUdpAmplificationFactor ?? 20,
        minUdpAmplificationFloorSizeOctets: settings.minUdpAmplificationFloorSizeOctets ?? 512,
      });
      this.baseline.set(this.form.getRawValue());
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load pipeline settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
