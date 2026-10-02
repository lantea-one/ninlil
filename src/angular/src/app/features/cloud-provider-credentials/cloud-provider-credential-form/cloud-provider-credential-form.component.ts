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
import { CloudDnsProviderTypeEnum, CloudProviderCredentialsService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  RevealSecretDialogComponent,
  RevealSecretDialogData,
} from '../../../shared/reveal-secret-dialog/reveal-secret-dialog.component';
import {
  buildCredentialsPayload,
  credentialFieldsFor,
  credentialsPayloadIsComplete,
  toControlName,
} from '../cloud-provider-credential.model';

/** The provider types the select offers, in display order. */
const PROVIDER_TYPE_OPTIONS: readonly CloudDnsProviderTypeEnum[] = [
  CloudDnsProviderTypeEnum.Cloudflare,
  CloudDnsProviderTypeEnum.Route53,
  CloudDnsProviderTypeEnum.DigitalOcean,
];

/**
 * This component is the create and edit page for a cloud provider credential set
 * (`/cloud-provider-credentials/new` and `.../:id/edit`, `cloudprovidercredentials:write`); edit
 * mode is chosen by the `id` route parameter. Cloud-synced zones use these credentials to push
 * records to Cloudflare, Route 53 or DigitalOcean.
 *
 * <p>The stored secrets are never fetched or shown. Edit mode has two save paths:
 * - **Metadata only** (the default): the provider type and credential fields stay disabled, and
 *   save sends a `PATCH` with only name, disabled flag and comments, which leaves the stored
 *   secrets untouched.
 * - **Rotate credentials** ({@link startRotatingCredentials}): the provider type and empty
 *   credential fields become editable, and save sends a full `PUT` with the new credentials.
 *
 * <p>After a create, the backend echoes the secrets once and the page shows them in
 * {@link RevealSecretDialogComponent}, one row per field, returning to the list when the dialog
 * closes. A rotate shows no dialog, since the operator just typed the values.
 */
@Component({
  selector: 'app-cloud-provider-credential-form',
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
    RevealSecretDialogComponent,
  ],
  templateUrl: './cloud-provider-credential-form.component.html',
  styleUrl: './cloud-provider-credential-form.component.scss',
})
export class CloudProviderCredentialFormComponent implements OnInit {
  /** Exposed so the template can find the form control for each credential field. */
  readonly toControlName = toControlName;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly credentialsApi = inject(CloudProviderCredentialsService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly credentialId = this.route.snapshot.paramMap.get('id');

  /** Whether this instance is editing an existing credential set rather than creating a new one. */
  readonly isEditMode = this.credentialId !== null;

  readonly providerTypeOptions = PROVIDER_TYPE_OPTIONS;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /**
   * Whether the provider type and credential fields are editable: always in create mode, and in
   * edit mode only after {@link startRotatingCredentials}.
   */
  readonly credentialsEditable = signal(!this.isEditMode);

  /** Whether the reveal-once dialog is shown; set after a create, never after a rotate. */
  readonly revealDialogVisible = signal(false);

  /** The new credential set's secret fields, shown once in {@link RevealSecretDialogComponent}. */
  readonly revealDialogData = signal<RevealSecretDialogData | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    providerType: [
      CloudDnsProviderTypeEnum.Cloudflare as CloudDnsProviderTypeEnum,
      [Validators.required],
    ],
    isDisabled: [false],
    comments: [''],
    apiToken: [''],
    accessKeyId: [''],
    secretAccessKey: [''],
  });

  /** The selected provider type as a signal (the form's `valueChanges` is not one). */
  readonly selectedProviderType = signal<CloudDnsProviderTypeEnum>(
    CloudDnsProviderTypeEnum.Cloudflare,
  );

  /** The credential fields the selected provider type needs. */
  readonly credentialFields = computed(() => credentialFieldsFor(this.selectedProviderType()));

  constructor() {
    this.form.controls.providerType.valueChanges.subscribe((type) =>
      this.selectedProviderType.set(type),
    );
  }

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingCredential();
  }

  /** This method enables the provider type and credential fields (empty) so the operator can rotate them. */
  startRotatingCredentials(): void {
    this.credentialsEditable.set(true);
    this.form.controls.providerType.enable();
    this.form.controls.apiToken.enable();
    this.form.controls.accessKeyId.enable();
    this.form.controls.secretAccessKey.enable();
  }

  /**
   * This method creates a credential set (`POST`) or, in edit mode, replaces it (`PUT`, when
   * rotating) or updates its metadata (`PATCH`). See the class comment for the two edit paths.
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const comments = value.comments.trim().length > 0 ? value.comments : null;
    // Pass only the three credential controls, so buildCredentialsPayload() can keep its
    // Record<string, string> parameter type instead of taking the whole form value.
    const credentialFieldValues: Record<string, string> = {
      apiToken: value.apiToken,
      accessKeyId: value.accessKeyId,
      secretAccessKey: value.secretAccessKey,
    };

    try {
      if (this.isEditMode && this.credentialId) {
        if (this.credentialsEditable()) {
          const credentials = buildCredentialsPayload(value.providerType, credentialFieldValues);
          if (!credentialsPayloadIsComplete(credentials)) {
            this.errorMessage.set('Enter a value for every credential field before rotating.');
            return;
          }

          await firstValueFrom(
            this.credentialsApi.apiCloudProviderCredentialsIdPut({
              id: this.credentialId,
              cloudProviderCredentialWriteRequest: {
                name: value.name,
                providerType: value.providerType,
                credentials,
                isDisabled: value.isDisabled,
                comments,
              },
            }),
          );
          this.notification.success('Credential set saved and rotated.');
        } else {
          await firstValueFrom(
            this.credentialsApi.apiCloudProviderCredentialsIdPatch({
              id: this.credentialId,
              body: { name: value.name, isDisabled: value.isDisabled, comments },
            }),
          );
          this.notification.success('Credential set saved.');
        }

        // Neither edit path has anything to reveal (the operator typed the rotated values), so
        // both return to the list at once.
        await this.router.navigate(['/cloud-provider-credentials']);
      } else {
        const credentials = buildCredentialsPayload(value.providerType, credentialFieldValues);
        if (!credentialsPayloadIsComplete(credentials)) {
          this.errorMessage.set('Enter a value for every credential field.');
          return;
        }

        const created = await firstValueFrom(
          this.credentialsApi.apiCloudProviderCredentialsPost({
            cloudProviderCredentialCreateRequest: {
              name: value.name,
              providerType: value.providerType,
              credentials,
              isDisabled: value.isDisabled,
              comments,
            },
          }),
        );

        // Stay on the page until the dialog is closed (onSecretDialogClosed).
        this.revealDialogData.set({
          title: 'Credential set created',
          message:
            "Copy these values now -- they will never be shown again through this UI. They're " +
            'the exact API credentials the cloud sync connector authenticates with when syncing ' +
            'a zone against this provider, so store them with the same care you would give the ' +
            "provider's own console login. You can rotate them later from this credential set's " +
            'own edit page if they leak or expire.',
          secrets: credentialFieldsFor(value.providerType).map((field) => ({
            label: field.label,
            value: created.secrets?.[field.key] ?? '',
          })),
        });
        this.revealDialogVisible.set(true);
      }
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this credential set.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method hides the dialog and returns to the credential list. */
  onSecretDialogClosed(): void {
    this.revealDialogVisible.set(false);
    void this.router.navigate(['/cloud-provider-credentials']);
  }

  private async loadExistingCredential(): Promise<void> {
    if (!this.credentialId) return;

    try {
      const credential = await firstValueFrom(
        this.credentialsApi.apiCloudProviderCredentialsIdGet({ id: this.credentialId }),
      );

      this.form.patchValue({
        name: credential.name ?? '',
        providerType: credential.providerType ?? CloudDnsProviderTypeEnum.Cloudflare,
        isDisabled: credential.isDisabled ?? false,
        comments: credential.comments ?? '',
      });
      this.selectedProviderType.set(credential.providerType ?? CloudDnsProviderTypeEnum.Cloudflare);

      // Edit mode starts with the provider type and credential fields disabled, so a metadata
      // save can never change the provider type without new credentials.
      this.form.controls.providerType.disable();
      this.form.controls.apiToken.disable();
      this.form.controls.accessKeyId.disable();
      this.form.controls.secretAccessKey.disable();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this credential set.'));
    } finally {
      this.loading.set(false);
    }
  }
}
