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
  FormCheckComponent,
  FormCheckInputDirective,
  FormCheckLabelDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom, map } from 'rxjs';
import { ClusterSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';
import {
  changedFields,
  hasChangedFields,
} from '../../../shared/settings-patch/changed-fields.util';

/**
 * This component is the "Cluster" settings page (`/settings/cluster`). It edits this node's
 * `ClusterOptions` through `GET` and `PATCH /api/cluster-settings` (`clustersettings:read`,
 * `clustersettings:write`): whether clustering is on, the advertised address and heartbeat
 * tuning. `enable` and `advertisedHost` must be set before inviting or joining a peer works on the
 * Cluster page (`ClusterComponent`). `nodeId` is generated once and shown read-only.
 *
 * <p>The form also carries `addressReconcileInterval` from `CatalogSyncOptions`: how often
 * catalog-managed zones' NOTIFY and primary address lists are matched to the cluster membership.
 * It and `heartbeatInterval` need a restart; the other fields are hot-reloaded. The backend picks
 * its note from the fields present in the request, so the page sends only the fields that changed
 * since the last load or save (`changedFields`): saving a hot-reloaded field alone does not
 * report that a restart is needed. Save stays disabled while nothing changed.
 */
@Component({
  selector: 'app-cluster-settings',
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
  templateUrl: './cluster-settings.component.html',
  styleUrl: './cluster-settings.component.scss',
})
export class ClusterSettingsComponent implements OnInit {
  private readonly clusterSettingsApi = inject(ClusterSettingsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** This node's cluster id, shown read-only and not part of the form. */
  readonly nodeId = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    enable: [false],
    advertisedHost: [''],
    advertisedPort: [0, [Validators.min(0), Validators.max(65535)]],
    heartbeatInterval: ['00:00:30', [Validators.required]],
    heartbeatTimeout: ['00:00:10', [Validators.required]],
    missedHeartbeatThreshold: [3, [Validators.required, Validators.min(1)]],
    addressReconcileInterval: ['00:01:00', [Validators.required]],
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
        this.clusterSettingsApi.apiClusterSettingsPatch({ body: this.changes() }),
      );
      this.baseline.set(saved);
      this.notification.success(result.note ?? 'Cluster settings saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save cluster settings.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.clusterSettingsApi.apiClusterSettingsGet());
      this.nodeId.set(settings.nodeId ?? null);
      this.form.patchValue({
        enable: settings.enable ?? false,
        advertisedHost: settings.advertisedHost ?? '',
        advertisedPort: settings.advertisedPort ?? 0,
        heartbeatInterval: settings.heartbeatInterval ?? '00:00:30',
        heartbeatTimeout: settings.heartbeatTimeout ?? '00:00:10',
        missedHeartbeatThreshold: settings.missedHeartbeatThreshold ?? 3,
        addressReconcileInterval: settings.addressReconcileInterval ?? '00:01:00',
      });
      this.baseline.set(this.form.getRawValue());
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load cluster settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
