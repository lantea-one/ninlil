import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule } from '@angular/forms';
import {
  AlertComponent,
  ButtonDirective,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  FormTextDirective,
  ModalBodyComponent,
  ModalComponent,
  ModalFooterComponent,
  ModalHeaderComponent,
  ModalTitleDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import {
  DnssecAlgorithmEnum,
  DnssecKeyDetail,
  DnssecKeyRoleEnum,
  DnssecKeysService,
} from '../../../../api-client';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import {
  DEFAULT_RSA_KEY_SIZE_BITS,
  isRsaAlgorithm,
  RSA_KEY_SIZE_HINT,
  rsaKeySizeValidator,
  SIGNING_DNSSEC_ALGORITHMS,
} from '../dnssec-key.model';

/**
 * This component is the rollover dialog of `DnssecKeyListComponent`. It starts an RFC 6781
 * rollover for one active key through `POST /api/zones/{zoneId}/dnssec-keys/{id}/roll`
 * (`zones:write`), and can also change the algorithm (an algorithm rollover) and, for RSA, the key
 * size. Leaving the algorithm on "same as now" omits it, so the backend keeps the outgoing key's
 * algorithm; a key size is sent whenever the new key will be RSA.
 */
@Component({
  selector: 'app-dnssec-key-rollover-dialog',
  imports: [
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    FormTextDirective,
    ModalBodyComponent,
    ModalComponent,
    ModalFooterComponent,
    ModalHeaderComponent,
    ModalTitleDirective,
    SpinnerComponent,
  ],
  templateUrl: './dnssec-key-rollover-dialog.component.html',
})
export class DnssecKeyRolloverDialogComponent {
  private readonly dnssecKeysApi = inject(DnssecKeysService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  /** Whether the dialog is shown; the parent drives it. */
  readonly visible = input<boolean>(false);

  /** The zone the key belongs to. */
  readonly zoneId = input.required<string>();

  /** The active key this dialog rolls over. */
  readonly key = input<DnssecKeyDetail | null>(null);

  /** Emits the new visibility, `false` on every close. */
  readonly visibleChange = output<boolean>();

  /** Emits the incoming replacement key once the rollover has started. */
  readonly rolled = output<DnssecKeyDetail>();

  readonly algorithmOptions = SIGNING_DNSSEC_ALGORITHMS;

  /** The hint under the key size field, shared with the key form. */
  readonly keySizeHint = RSA_KEY_SIZE_HINT;

  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    // '' keeps the outgoing key's algorithm.
    algorithm: ['' as '' | DnssecAlgorithmEnum],
    keySizeBits: [DEFAULT_RSA_KEY_SIZE_BITS, [rsaKeySizeValidator]],
  });

  private readonly chosenAlgorithm = toSignal(this.form.controls.algorithm.valueChanges, {
    initialValue: this.form.controls.algorithm.value,
  });

  /** The algorithm the new key will use: the chosen one, or the outgoing key's. */
  readonly effectiveAlgorithm = computed(
    () => this.chosenAlgorithm() || this.key()?.algorithm || null,
  );

  /** Key size only means something for RSA; every other algorithm fixes it. */
  readonly showKeySize = computed(() => {
    const algorithm = this.effectiveAlgorithm();
    return algorithm !== null && isRsaAlgorithm(algorithm);
  });

  /** The RFC 6781 scheme the backend uses for the key's role, shown in the dialog. */
  readonly scheme = computed(() =>
    this.key()?.role === DnssecKeyRoleEnum.Ksk ? 'Double-Signature' : 'Pre-Publish',
  );

  readonly isKsk = computed(() => this.key()?.role === DnssecKeyRoleEnum.Ksk);

  constructor() {
    // Start from "same as now" each time the dialog opens for a key.
    effect(() => {
      this.key();
      this.form.reset({ algorithm: '', keySizeBits: DEFAULT_RSA_KEY_SIZE_BITS });
      this.errorMessage.set(null);
    });
  }

  /** This method starts the rollover against `POST .../dnssec-keys/{id}/roll`. */
  async submit(): Promise<void> {
    const target = this.key();
    if (!target?.id || this.submitting()) return;
    if (this.showKeySize() && this.form.controls.keySizeBits.invalid) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const { algorithm, keySizeBits } = this.form.getRawValue();

    try {
      const replacement = await firstValueFrom(
        this.dnssecKeysApi.apiZonesZoneIdDnssecKeysIdRollPost({
          zoneId: this.zoneId(),
          id: target.id,
          dnssecKeyRolloverRequest: {
            ...(algorithm ? { algorithm } : {}),
            keySizeBits: this.showKeySize() ? keySizeBits : 0,
          },
        }),
      );
      this.visibleChange.emit(false);
      this.rolled.emit(replacement);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not start a rollover for this key.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method handles the modal closing itself (a backdrop click or Escape). */
  onModalVisibleChange(visible: boolean): void {
    if (!visible) this.visibleChange.emit(false);
  }
}
