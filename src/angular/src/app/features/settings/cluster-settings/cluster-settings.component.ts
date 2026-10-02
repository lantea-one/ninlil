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
import { ClusterSettingsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "Cluster" settings page (`/settings/cluster`). It edits this node's
 * `ClusterOptions` through `GET` and `PATCH /api/cluster-settings` (`clustersettings:read`,
 * `clustersettings:write`): whether clustering is on, the advertised address and heartbeat
 * tuning. `enable` and `advertisedHost` must be set before inviting or joining a peer works on the
 * Cluster page (`ClusterComponent`). `nodeId` is generated once and shown read-only.
 *
 * <p>The form also carries `addressReconcileInterval` from `CatalogSyncOptions`: how often
 * catalog-managed zones' NOTIFY and primary address lists are matched to the cluster membership.
 * It and `heartbeatInterval` need a restart; the other fields are hot-reloaded. The backend's note
 * says which applies.
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

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method PATCHes every editable field, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.clusterSettingsApi.apiClusterSettingsPatch({ body: value }),
      );
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
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load cluster settings.'));
    } finally {
      this.loading.set(false);
    }
  }
}
