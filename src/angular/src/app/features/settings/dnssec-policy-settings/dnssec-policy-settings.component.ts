import { Component, OnInit, inject, signal } from '@angular/core';
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
  FormTextDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import { DnssecAlgorithmEnum, DnssecPolicySettingsService } from '../../../api-client';
import {
  isSigningProhibited,
  SIGNING_DNSSEC_ALGORITHMS,
} from '../../zones/dnssec-keys/dnssec-key.model';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import { IfPermissionDirective } from '../../../shared/if-permission/if-permission.directive';

/**
 * This component is the "DNSSEC Policy" settings page (`/settings/dnssec-policy`). It edits
 * `DnssecAlgorithmPolicyOptions` through `GET` and `PATCH /api/dnssec-policy-settings`
 * (`dnssecpolicysettings:read`, `dnssecpolicysettings:write`): the algorithms
 * `DnssecKeyManagementService.GenerateKeyAsync` may generate new keys with, which also limits
 * rollovers. It is a checkbox per `DnssecAlgorithmEnum` value, since several may be allowed. The
 * allowlist is the only field, so the selection is a signal rather than a `FormGroup`. Changes are
 * hot-reloaded.
 */
@Component({
  selector: 'app-dnssec-policy-settings',
  imports: [
    IfPermissionDirective,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    FormCheckComponent,
    FormCheckInputDirective,
    FormCheckLabelDirective,
    FormTextDirective,
    SpinnerComponent,
  ],
  templateUrl: './dnssec-policy-settings.component.html',
  styleUrl: './dnssec-policy-settings.component.scss',
})
export class DnssecPolicySettingsComponent implements OnInit {
  private readonly dnssecPolicyApi = inject(DnssecPolicySettingsService);
  private readonly notification = inject(NotificationService);

  /** Every algorithm the server may sign with, one checkbox each. */
  readonly algorithmOptions = SIGNING_DNSSEC_ALGORITHMS;

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** The checked algorithms. An empty set means no restriction, the default. */
  readonly selectedAlgorithms = signal<ReadonlySet<DnssecAlgorithmEnum>>(new Set());

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method reports whether a given algorithm is currently checked. */
  isSelected(algorithm: DnssecAlgorithmEnum): boolean {
    return this.selectedAlgorithms().has(algorithm);
  }

  /** This method checks or unchecks one algorithm. */
  toggle(algorithm: DnssecAlgorithmEnum): void {
    const next = new Set(this.selectedAlgorithms());
    if (next.has(algorithm)) next.delete(algorithm);
    else next.add(algorithm);
    this.selectedAlgorithms.set(next);
  }

  /** This method PATCHes the full allowlist, then shows the backend's note. */
  async save(): Promise<void> {
    if (this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    try {
      const result = await firstValueFrom(
        this.dnssecPolicyApi.apiDnssecPolicySettingsPatch({
          body: { allowedAlgorithms: [...this.selectedAlgorithms()] },
        }),
      );
      this.notification.success(result.note ?? 'DNSSEC algorithm policy saved.');
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not save the DNSSEC algorithm policy.'),
      );
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadSettings(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const settings = await firstValueFrom(this.dnssecPolicyApi.apiDnssecPolicySettingsGet());
      this.selectedAlgorithms.set(
        new Set((settings.allowedAlgorithms ?? []).filter((a) => !isSigningProhibited(a))),
      );
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load the DNSSEC algorithm policy.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
