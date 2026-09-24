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
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/**
 * This component reads and writes this host's own `DnssecAlgorithmPolicyOptions` (`GET`/`PATCH
 * /api/dnssec-policy-settings`) -- which algorithms `DnssecKeyManagementService.GenerateKeyAsync`
 * will generate a brand-new signing key for. A checkbox group over every algorithm this project's
 * own `DnssecAlgorithmEnum` supports -- the right control type for "which of a small, fixed set
 * may be used," per this project's own UI/UX interaction-pattern conventions (a dropdown/select
 * only lets one value be chosen at a time, wrong for an allowlist). No other field exists on this
 * options class, so this component drives its own selection directly off a plain array signal
 * rather than a `FormGroup` a single checkbox group gains nothing from.
 */
@Component({
  selector: 'app-dnssec-policy-settings',
  imports: [
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

  /** Every algorithm this project's own `DnssecAlgorithmEnum` supports, rendered as one checkbox each. */
  readonly algorithmOptions = Object.values(DnssecAlgorithmEnum);

  readonly loading = signal(true);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** The algorithms currently checked -- an empty set means "no restriction at all," this options class' own compiled-in default. */
  readonly selectedAlgorithms = signal<ReadonlySet<DnssecAlgorithmEnum>>(new Set());

  ngOnInit(): void {
    void this.loadSettings();
  }

  /** This method reports whether a given algorithm is currently checked. */
  isSelected(algorithm: DnssecAlgorithmEnum): boolean {
    return this.selectedAlgorithms().has(algorithm);
  }

  /** This method flips a single algorithm's own checked state. */
  toggle(algorithm: DnssecAlgorithmEnum): void {
    const next = new Set(this.selectedAlgorithms());
    if (next.has(algorithm)) next.delete(algorithm);
    else next.add(algorithm);
    this.selectedAlgorithms.set(next);
  }

  /** This method PATCHes the full allowlist, then surfaces the backend's own hot-reloaded note. */
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
      this.selectedAlgorithms.set(new Set(settings.allowedAlgorithms ?? []));
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load the DNSSEC algorithm policy.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
