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

/** Every cloud DNS provider type this form's own provider-type select offers, in the order shown. */
const PROVIDER_TYPE_OPTIONS: readonly CloudDnsProviderTypeEnum[] = [
  CloudDnsProviderTypeEnum.Cloudflare,
  CloudDnsProviderTypeEnum.Route53,
  CloudDnsProviderTypeEnum.DigitalOcean,
];

/**
 * This component is both the "create a cloud provider credential set" and "edit a credential set"
 * page -- one component for both, distinguished by whether the route supplies an `id` (see
 * `app.routes.ts`), mirroring `ZoneFormComponent`'s/`TsigKeyFormComponent`'s own precedent.
 *
 * Unlike `TsigKeyFormComponent`, this resource's own secret material genuinely *is* rotatable
 * (`CloudProviderCredentialsController.Replace`'s own doc comment explains why: a cloud
 * credential is always operator-supplied, so there is no "reveal it again" problem the way there
 * is for a server-generated TSIG secret). This shapes edit mode's own two distinct save paths,
 * both built around a single, real, checked requirement -- "let an operator submit a new
 * credential value without the UI ever having shown them the old one" -- which this component
 * satisfies by never fetching or displaying the stored secret at all, in either path:
 * - **Metadata-only edit** (the default): {@link credentialsEditable} stays `false`, the
 *   provider-type select and every credential field stay disabled, and `save()` sends a `PATCH`
 *   with only `name`/`isDisabled`/`comments` -- deliberately omitting `providerType` and
 *   `credentials` entirely, so `CloudProviderCredentialsController.Patch`'s own "omit credentials
 *   to leave the stored secret material completely untouched" contract applies.
 * - **Rotate credentials** (after clicking "Rotate credentials", {@link startRotatingCredentials}):
 *   the provider-type select and every credential field become editable (starting blank -- this
 *   component never has the old value to pre-fill even if it wanted to), and `save()` instead
 *   sends a full `PUT` replacing every field, including the newly typed `credentials`.
 *
 * On a successful **create**, this component shows {@link RevealSecretDialogComponent} with every
 * field `created.secrets` returns, labeled per {@link credentialFieldsFor}'s own per-provider
 * shape -- the same reveal-once pattern `TsigKeyFormComponent` established, extended here to
 * possibly more than one secret field at once (Route 53's own access key ID + secret access key
 * pair). Like `TsigKeyFormComponent`, the dialog is embedded declaratively, so navigation away is
 * deferred until the operator dismisses it (see {@link onSecretDialogClosed}). A **rotate** via
 * `PUT` deliberately does *not* show this dialog at all -- the operator just typed the new value
 * themselves in this very request, so there is nothing new to reveal, per
 * `CloudProviderCredentialWriteRequest`'s own documented reasoning; that path navigates away
 * immediately, exactly as a metadata-only `PATCH` does.
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
  /** Exposed so the template can pick the right fixed control per dynamic credential field, without a hand-written ternary chain. */
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
   * Whether the provider-type select and credential fields are currently editable -- always
   * `true` in create mode (a brand-new credential set has no stored secret to protect yet), and
   * `false` in edit mode until {@link startRotatingCredentials} is called.
   */
  readonly credentialsEditable = signal(!this.isEditMode);

  /** Whether {@link RevealSecretDialogComponent} is currently shown -- only ever set after a successful create, never after a rotate. */
  readonly revealDialogVisible = signal(false);

  /** The just-created credential set's own secret field(s), shown exactly once via {@link RevealSecretDialogComponent}. */
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

  /** The provider type currently selected, tracked as a plain signal since `valueChanges` isn't one. */
  readonly selectedProviderType = signal<CloudDnsProviderTypeEnum>(
    CloudDnsProviderTypeEnum.Cloudflare,
  );

  /** The credential field(s) the currently selected provider type's own shape requires. */
  readonly credentialFields = computed(() => credentialFieldsFor(this.selectedProviderType()));

  constructor() {
    this.form.controls.providerType.valueChanges.subscribe((type) =>
      this.selectedProviderType.set(type),
    );
  }

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingCredential();
  }

  /** This method reveals the provider-type select and credential fields so an operator can rotate them. */
  startRotatingCredentials(): void {
    this.credentialsEditable.set(true);
    this.form.controls.providerType.enable();
    this.form.controls.apiToken.enable();
    this.form.controls.accessKeyId.enable();
    this.form.controls.secretAccessKey.enable();
  }

  /**
   * This method creates (`POST`) a new credential set, or, in edit mode, either fully replaces
   * (`PUT`, when rotating) or partially updates (`PATCH`, metadata only) an existing one -- see
   * this component's own class-level doc comment for the full reasoning behind the two edit paths.
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const comments = value.comments.trim().length > 0 ? value.comments : null;
    // buildCredentialsPayload() only ever reads the 3 fixed credential controls -- narrowed to
    // just those here so its own `Record<string, string>` parameter type stays a real, useful
    // constraint rather than accepting this form's whole (non-string-valued) raw value object.
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

        // A rotate's own new value was just typed by the operator themselves, and a
        // metadata-only save never touched any secret at all -- neither path has anything new to
        // reveal, so both navigate away immediately, unlike the create path below.
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

        // Reveal every returned secret field exactly once -- navigation away is deferred to
        // onSecretDialogClosed(), once the operator has actually dismissed the dialog.
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

  /** This method runs once the operator dismisses the reveal-once secret dialog, navigating away only now that they have confirmed saving the values. */
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

      // Edit mode starts with the provider type and every credential field disabled -- see this
      // component's own class-level doc comment for why: a metadata-only save must never
      // accidentally submit a `providerType` change without matching new credentials.
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
