import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
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
  FormSelectDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { DnssecAlgorithmEnum, DnssecKeyRoleEnum, DnssecKeysService } from '../../../../api-client';
import { extractErrorMessage } from '../../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../../shared/notification/notification.service';
import { DEFAULT_RSA_KEY_SIZE_BITS, isRsaAlgorithm } from '../dnssec-key.model';

/**
 * This component is both the "generate a DNSSEC key" and "edit a key's own retirement status"
 * page -- one component for both, distinguished by whether the route supplies an `id` (see
 * `app.routes.ts`), mirroring `ZoneFormComponent`'s/`TsigKeyFormComponent`'s own precedent, but
 * with each mode showing a genuinely disjoint field set rather than the same fields in a
 * different state: generate mode shows role/algorithm/key size/comments
 * (`DnssecKeyCreateRequest`'s own full shape); edit mode shows only retirement status/comments
 * (`DnssecKeyWriteRequest`'s own full shape) -- a key's role, algorithm, and key material are
 * permanent once generated, per `DnssecKeysController.Replace`'s own doc comment, so there is
 * nothing else this page could ever let an operator change on an existing key.
 *
 * **This page never renders, requests, or receives a key's own private key material at any
 * point** -- `DnssecKeyDetail` (what both the generate response and a later `GET` return) simply
 * never carries it, matching `DnssecKeysController`'s own deliberate exposure policy exactly; the
 * reveal-once secret dialog `TsigKeyFormComponent`/`CloudProviderCredentialFormComponent`
 * establish is never used here, since there is no secret this page could ever reveal.
 */
@Component({
  selector: 'app-dnssec-key-form',
  imports: [
    ReactiveFormsModule,
    RouterLink,
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
    FormSelectDirective,
    SpinnerComponent,
  ],
  templateUrl: './dnssec-key-form.component.html',
  styleUrl: './dnssec-key-form.component.scss',
})
export class DnssecKeyFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly dnssecKeysApi = inject(DnssecKeysService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly zoneId = this.route.snapshot.paramMap.get('zoneId') ?? '';
  private readonly keyId = this.route.snapshot.paramMap.get('keyId');

  /** Whether this instance is editing an existing key's own retirement status rather than generating a new one. */
  readonly isEditMode = this.keyId !== null;

  readonly roleOptions = Object.values(DnssecKeyRoleEnum);
  readonly algorithmOptions = Object.values(DnssecAlgorithmEnum);

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Read-only context shown alongside the retirement-status form in edit mode -- these fields are never editable. */
  readonly existingRole = signal<DnssecKeyRoleEnum | null>(null);
  readonly existingAlgorithm = signal<DnssecAlgorithmEnum | null>(null);
  readonly existingKeyTag = signal<number | null>(null);

  readonly form = this.formBuilder.group({
    role: [DnssecKeyRoleEnum.Zsk as DnssecKeyRoleEnum, [Validators.required]],
    algorithm: [DnssecAlgorithmEnum.Ecdsap256Sha256 as DnssecAlgorithmEnum, [Validators.required]],
    keySizeBits: [DEFAULT_RSA_KEY_SIZE_BITS, [Validators.min(0)]],
    isRetired: [false],
    comments: [''],
  });

  /** The algorithm currently selected, tracked as a plain signal since `valueChanges` isn't one. */
  readonly selectedAlgorithm = signal<DnssecAlgorithmEnum>(DnssecAlgorithmEnum.Ecdsap256Sha256);

  /** Whether the key-size field is meaningful for the currently selected algorithm -- see `isRsaAlgorithm`'s own doc comment. */
  readonly showKeySize = computed(() => isRsaAlgorithm(this.selectedAlgorithm()));

  constructor() {
    this.form.controls.algorithm.valueChanges.subscribe((algorithm) =>
      this.selectedAlgorithm.set(algorithm),
    );
  }

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingKey();
  }

  /** This method generates (`POST`) a new key, or fully replaces (`PUT`) an existing one's own retirement status/comments. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      if (this.isEditMode && this.keyId) {
        await firstValueFrom(
          this.dnssecKeysApi.apiZonesZoneIdDnssecKeysIdPut({
            zoneId: this.zoneId,
            id: this.keyId,
            dnssecKeyWriteRequest: {
              isRetired: value.isRetired,
              comments: value.comments.trim().length > 0 ? value.comments : null,
            },
          }),
        );
        this.notification.success('Key saved.');
      } else {
        await firstValueFrom(
          this.dnssecKeysApi.apiZonesZoneIdDnssecKeysPost({
            zoneId: this.zoneId,
            dnssecKeyCreateRequest: {
              role: value.role,
              algorithm: value.algorithm,
              keySizeBits: this.showKeySize() ? value.keySizeBits : 0,
              comments: value.comments.trim().length > 0 ? value.comments : null,
            },
          }),
        );
        this.notification.success('Key generated.');
      }

      await this.router.navigate(['/zones', this.zoneId, 'dnssec-keys']);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this key.'));
    } finally {
      this.submitting.set(false);
    }
  }

  private async loadExistingKey(): Promise<void> {
    if (!this.keyId) return;

    try {
      const key = await firstValueFrom(
        this.dnssecKeysApi.apiZonesZoneIdDnssecKeysIdGet({ zoneId: this.zoneId, id: this.keyId }),
      );

      this.existingRole.set(key.role ?? null);
      this.existingAlgorithm.set(key.algorithm ?? null);
      this.existingKeyTag.set(key.keyTag ?? null);
      this.form.patchValue({
        isRetired: key.isRetired ?? false,
        comments: key.comments ?? '',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this key.'));
    } finally {
      this.loading.set(false);
    }
  }
}
