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
import { WebhookAuthModeEnum, WebhooksService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  RevealSecretDialogComponent,
  RevealSecretDialogData,
} from '../../../shared/reveal-secret-dialog/reveal-secret-dialog.component';
import {
  CURATED_WEBHOOK_EVENT_TYPE_NAMES,
  WEBHOOK_AUTH_MODE_OPTIONS,
  authCredentialsPayloadIsComplete,
  buildAuthCredentialsPayload,
  credentialFieldsForAuthMode,
  toControlName,
} from '../webhook.model';

/**
 * This component is both the "create a webhook subscription" and "edit a subscription" page --
 * one component for both, distinguished by whether the route supplies an `id` (see
 * `app.routes.ts`), mirroring `TsigKeyFormComponent`'s/`CloudProviderCredentialFormComponent`'s
 * own precedent. This entity carries *two* secrets with two different mutability postures, each
 * deliberately matching its own closest existing precedent (see `WebhookSubscriptionCreateRequest`'s
 * own doc comment for the backend's own reasoning):
 *
 * - **Auth credential(s)** (`authCredentials`) behave like a cloud provider credential's own
 *   secret -- always operator-supplied, so they follow {@link CloudProviderCredentialFormComponent}'s
 *   own "rotate" affordance: edit mode starts with the auth mode and every credential field
 *   disabled, and {@link startRotatingCredentials} reveals them so an operator can resupply a new
 *   value, sent as part of the very same `PATCH` `save()` always issues in edit mode.
 * - **HMAC signing secret** (`hmacSigningSecret`) behaves like a TSIG key's own server-generated
 *   secret instead -- optionally supplied (or, if left blank, server-generated) only at creation,
 *   and **immutable afterward**: `WebhooksController.Patch` has no field for it at all, so this
 *   component's own edit mode never renders an input for it, only a read-only "configured/not
 *   configured" indicator, matching this project's own confirmed "no edit affordance for it at
 *   all once set" requirement.
 *
 * On a successful **create**, this component shows {@link RevealSecretDialogComponent} with every
 * secret the response actually returned (the auth credential field(s), if `authMode` is not
 * `None`, and the HMAC signing secret, if signing was enabled) -- the one and only place either is
 * ever shown, matching `WebhookSubscriptionDetail`'s own deliberate omission of both from every
 * later `GET`/list response. The dialog is embedded declaratively (`[visible]`/`[data]`/`(closed)`)
 * rather than opened imperatively, so navigation away from this page is deferred until the
 * operator dismisses it -- see {@link RevealSecretDialogComponent}'s own doc comment for why.
 */
@Component({
  selector: 'app-webhook-form',
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
  templateUrl: './webhook-form.component.html',
  styleUrl: './webhook-form.component.scss',
})
export class WebhookFormComponent implements OnInit {
  /** Exposed so the template can pick the right fixed control per dynamic credential field, without a hand-written ternary chain. */
  readonly toControlName = toControlName;

  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly webhooksApi = inject(WebhooksService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly subscriptionId = this.route.snapshot.paramMap.get('id');

  /** Whether this instance is editing an existing subscription rather than creating a new one. */
  readonly isEditMode = this.subscriptionId !== null;

  readonly eventTypeOptions = CURATED_WEBHOOK_EVENT_TYPE_NAMES;
  readonly authModeOptions = WEBHOOK_AUTH_MODE_OPTIONS;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether this subscription (edit mode only) already has an HMAC signing secret configured -- read-only, never editable here. */
  readonly hasHmacSigningSecret = signal(false);

  /**
   * Whether the auth mode select and credential fields are currently editable -- always `true` in
   * create mode, and `false` in edit mode until {@link startRotatingCredentials} is called.
   */
  readonly credentialsEditable = signal(!this.isEditMode);

  /** Whether {@link RevealSecretDialogComponent} is currently shown -- only ever set after a successful create that returned at least one secret. */
  readonly revealDialogVisible = signal(false);

  /** The just-created subscription's own secret field(s), shown exactly once via {@link RevealSecretDialogComponent}. */
  readonly revealDialogData = signal<RevealSecretDialogData | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    targetUrl: ['', [Validators.required]],
    eventTypeNames: [[] as string[], [Validators.required]],
    authMode: [WebhookAuthModeEnum.None as WebhookAuthModeEnum],
    bearerToken: [''],
    username: [''],
    password: [''],
    enableHmacSigning: [false],
    hmacSigningSecret: [''],
    maxDeliveryAttempts: [5, [Validators.required, Validators.min(1)]],
    isDisabled: [false],
    comments: [''],
  });

  /** The auth mode currently selected, tracked as a plain signal since `valueChanges` isn't one. */
  readonly selectedAuthMode = signal<WebhookAuthModeEnum>(WebhookAuthModeEnum.None);

  /** The credential field(s) the currently selected auth mode's own shape requires. */
  readonly credentialFields = computed(() => credentialFieldsForAuthMode(this.selectedAuthMode()));

  constructor() {
    this.form.controls.authMode.valueChanges.subscribe((mode) => this.selectedAuthMode.set(mode));
  }

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingSubscription();
  }

  /** This method reveals the auth mode select and credential fields so an operator can rotate them. */
  startRotatingCredentials(): void {
    this.credentialsEditable.set(true);
    this.form.controls.authMode.enable();
    this.form.controls.bearerToken.enable();
    this.form.controls.username.enable();
    this.form.controls.password.enable();
  }

  /**
   * This method creates (`POST`) a new subscription, or, in edit mode, partially updates
   * (`PATCH`) an existing one -- including its auth mode/credentials only when
   * {@link credentialsEditable} is `true`, per this component's own class-level doc comment.
   */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();
    const comments = value.comments.trim().length > 0 ? value.comments : null;
    const credentialFieldValues: Record<string, string> = {
      bearerToken: value.bearerToken,
      username: value.username,
      password: value.password,
    };

    try {
      if (this.isEditMode && this.subscriptionId) {
        const body: Record<string, unknown> = {
          name: value.name,
          targetUrl: value.targetUrl,
          eventTypeNames: value.eventTypeNames,
          maxDeliveryAttempts: value.maxDeliveryAttempts,
          isDisabled: value.isDisabled,
          comments,
        };

        if (this.credentialsEditable()) {
          const authCredentials = buildAuthCredentialsPayload(
            value.authMode,
            credentialFieldValues,
          );
          if (
            value.authMode !== WebhookAuthModeEnum.None &&
            !authCredentialsPayloadIsComplete(authCredentials)
          ) {
            this.errorMessage.set('Enter a value for every credential field before rotating.');
            return;
          }

          body['authMode'] = value.authMode;
          body['authCredentials'] =
            value.authMode === WebhookAuthModeEnum.None ? null : authCredentials;
        }

        await firstValueFrom(
          this.webhooksApi.apiWebhooksIdPatch({ id: this.subscriptionId, body }),
        );
        this.notification.success('Webhook subscription saved.');
        await this.router.navigate(['/webhooks']);
      } else {
        const authCredentials = buildAuthCredentialsPayload(value.authMode, credentialFieldValues);
        if (
          value.authMode !== WebhookAuthModeEnum.None &&
          !authCredentialsPayloadIsComplete(authCredentials)
        ) {
          this.errorMessage.set('Enter a value for every credential field.');
          return;
        }

        const created = await firstValueFrom(
          this.webhooksApi.apiWebhooksPost({
            webhookSubscriptionCreateRequest: {
              name: value.name,
              targetUrl: value.targetUrl,
              eventTypeNames: value.eventTypeNames,
              authMode: value.authMode,
              authCredentials: value.authMode === WebhookAuthModeEnum.None ? null : authCredentials,
              enableHmacSigning: value.enableHmacSigning,
              hmacSigningSecret:
                value.hmacSigningSecret.trim().length > 0 ? value.hmacSigningSecret : null,
              maxDeliveryAttempts: value.maxDeliveryAttempts,
              isDisabled: value.isDisabled,
              comments,
            },
          }),
        );

        const secrets = [
          ...credentialFieldsForAuthMode(value.authMode).map((field) => ({
            label: field.label,
            value: created.authCredentials?.[field.key] ?? '',
          })),
          ...(created.hmacSigningSecret
            ? [{ label: 'HMAC signing secret', value: created.hmacSigningSecret }]
            : []),
        ];

        if (secrets.length > 0) {
          // Reveal every returned secret exactly once -- navigation away is deferred to
          // onSecretDialogClosed(), once the operator has actually dismissed the dialog.
          this.revealDialogData.set({
            title: 'Webhook subscription created',
            message:
              'Copy these values now -- they will never be shown again through this UI. Any ' +
              'auth credentials shown are what this server sends with every outbound delivery ' +
              "request to your target URL, and can be rotated later from this subscription's " +
              'edit page; the HMAC signing secret, if generated, is what your receiver uses to ' +
              'verify the X-Ninlil-Signature header on each delivered payload, and can never be ' +
              'changed after this -- only deleting and recreating the subscription gets a new ' +
              'one.',
            secrets,
          });
          this.revealDialogVisible.set(true);
        } else {
          await this.router.navigate(['/webhooks']);
        }
      }
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not save this webhook subscription.'),
      );
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method runs once the operator dismisses the reveal-once secret dialog, navigating away only now that they have confirmed saving the values. */
  onSecretDialogClosed(): void {
    this.revealDialogVisible.set(false);
    void this.router.navigate(['/webhooks']);
  }

  private async loadExistingSubscription(): Promise<void> {
    if (!this.subscriptionId) return;

    try {
      const subscription = await firstValueFrom(
        this.webhooksApi.apiWebhooksIdGet({ id: this.subscriptionId }),
      );

      this.form.patchValue({
        name: subscription.name ?? '',
        targetUrl: subscription.targetUrl ?? '',
        eventTypeNames: [...(subscription.eventTypeNames ?? [])],
        authMode: subscription.authMode ?? WebhookAuthModeEnum.None,
        maxDeliveryAttempts: subscription.maxDeliveryAttempts ?? 5,
        isDisabled: subscription.isDisabled ?? false,
        comments: subscription.comments ?? '',
      });
      this.selectedAuthMode.set(subscription.authMode ?? WebhookAuthModeEnum.None);
      this.hasHmacSigningSecret.set(subscription.hasHmacSigningSecret ?? false);

      // Edit mode starts with the auth mode and every credential field disabled -- see this
      // component's own class-level doc comment for why: a metadata-only save must never
      // accidentally submit an authMode change without matching new credentials.
      this.form.controls.authMode.disable();
      this.form.controls.bearerToken.disable();
      this.form.controls.username.disable();
      this.form.controls.password.disable();
    } catch (error) {
      this.errorMessage.set(
        extractErrorMessage(error, 'Could not load this webhook subscription.'),
      );
    } finally {
      this.loading.set(false);
    }
  }
}
