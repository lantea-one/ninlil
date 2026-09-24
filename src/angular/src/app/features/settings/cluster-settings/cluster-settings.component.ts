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
 * This component reads and writes this node's own `ClusterOptions` via
 * `GET`/`PATCH /api/cluster-settings` -- whether clustering is on at all, this node's own
 * advertised address, and the heartbeat engine's own tuning. An operator must set `enable` and
 * `advertisedHost` here before either inviting or joining a peer can succeed (see the "Cluster"
 * page, `ClusterComponent`); `nodeId` is shown for diagnostic reference only, generated once
 * automatically and never editable. Most fields take effect immediately, with no restart --
 * `heartbeatInterval` is the one exception, read once at startup by the background heartbeat
 * engine. `addressReconcileInterval` is a genuinely distinct backend options class
 * (`CatalogSyncOptions`, not `ClusterOptions` itself) folded into this same form rather than
 * given a settings page of its own -- how often a catalog-managed zone's own NOTIFY/primary
 * address lists are reconciled against this node's own live cluster membership. Like
 * `heartbeatInterval`, it also takes effect only after a restart.
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

  /** This node's own stable cluster identity -- shown read-only, never part of the editable form. */
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

  /** This method PATCHes every editable field this form holds, then surfaces the backend's own reload note. */
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
