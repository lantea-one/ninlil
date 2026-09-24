import { Component, computed, effect, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
import { DEFAULT_RSA_KEY_SIZE_BITS, isRsaAlgorithm } from '../dnssec-key.model';

/**
 * This component starts an RFC 6781 rollover for one active key, from `DnssecKeyListComponent`.
 * It replaces the plain yes/no confirmation the list used to show, because the roll endpoint can
 * also change the key's algorithm (an algorithm rollover) and, for RSA, its key size. Leaving the
 * algorithm on "same as now" sends neither, so the backend reuses the outgoing key's algorithm.
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

  /** Whether this dialog is currently shown -- a caller drives this with its own signal. */
  readonly visible = input<boolean>(false);

  /** The zone the key belongs to. */
  readonly zoneId = input.required<string>();

  /** The active key this dialog rolls over. */
  readonly key = input<DnssecKeyDetail | null>(null);

  /** Emits this dialog's own new visibility -- `false` on every close. */
  readonly visibleChange = output<boolean>();

  /** Emits the incoming replacement key once the rollover has started. */
  readonly rolled = output<DnssecKeyDetail>();

  readonly algorithmOptions = Object.values(DnssecAlgorithmEnum);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  readonly form = this.formBuilder.group({
    // '' keeps the outgoing key's algorithm.
    algorithm: ['' as '' | DnssecAlgorithmEnum],
    keySizeBits: [DEFAULT_RSA_KEY_SIZE_BITS, [Validators.required, Validators.min(1024)]],
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

  /** RFC 6781 scheme the backend picks from the role. */
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

  /** This method runs when the modal reports a new visibility on its own (a backdrop click or Escape). */
  onModalVisibleChange(visible: boolean): void {
    if (!visible) this.visibleChange.emit(false);
  }
}
