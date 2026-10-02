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
 * This component is both the "Generate key" page (`/zones/:zoneId/dnssec-keys/new`) and the "Edit
 * key" page (`/zones/:zoneId/dnssec-keys/:keyId/edit`); a `keyId` route parameter selects edit
 * mode. Both routes need `zones:write`. The two modes show different fields. Generating
 * (`POST /api/zones/{zoneId}/dnssec-keys`, a `DnssecKeyCreateRequest`) asks for role, algorithm,
 * RSA key size and comments. Editing (`PUT .../{id}`, a `DnssecKeyWriteRequest`) changes only the
 * retirement flag and comments, since a key's role, algorithm and key material are fixed once it
 * is generated.
 *
 * No private key material is ever sent to this page: `DnssecKeyDetail` does not carry it, so
 * unlike the TSIG key and cloud credential forms there is no reveal-once secret dialog.
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

  /** Whether this instance edits an existing key rather than generating one. */
  readonly isEditMode = this.keyId !== null;

  readonly roleOptions = Object.values(DnssecKeyRoleEnum);
  readonly algorithmOptions = Object.values(DnssecAlgorithmEnum);

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** The existing key's role, shown read-only in edit mode (with the algorithm and key tag below). */
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

  /** The selected algorithm, mirrored into a signal since `valueChanges` is not one. */
  readonly selectedAlgorithm = signal<DnssecAlgorithmEnum>(DnssecAlgorithmEnum.Ecdsap256Sha256);

  /** Whether the key-size field shows: only RSA algorithms take a size (see `isRsaAlgorithm`). */
  readonly showKeySize = computed(() => isRsaAlgorithm(this.selectedAlgorithm()));

  constructor() {
    this.form.controls.algorithm.valueChanges.subscribe((algorithm) =>
      this.selectedAlgorithm.set(algorithm),
    );
  }

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingKey();
  }

  /** This method generates a key (`POST`), or replaces an existing key's retirement flag and comments (`PUT`). */
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
