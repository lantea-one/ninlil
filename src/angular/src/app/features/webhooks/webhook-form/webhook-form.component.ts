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
 * This component is the create and edit page for a webhook subscription (`/webhooks/new` and
 * `/webhooks/:id/edit`, `webhooks:write`); edit mode is chosen by the `id` route parameter. It
 * saves with `POST /api/webhooks` or `PATCH /api/webhooks/{id}`. A subscription has two kinds of
 * secret, handled differently:
 *
 * - **Auth credentials** (`authCredentials`) are always typed by the operator. Edit mode starts
 *   with the auth mode and credential fields disabled; {@link startRotatingCredentials} enables
 *   them so new values can be sent with the same `PATCH`.
 * - **HMAC signing secret** (`hmacSigningSecret`) can be typed or left blank to have the server
 *   generate one, at creation only. The `PATCH` has no field for it, so edit mode only shows
 *   whether one is configured.
 *
 * <p>After a create, the page shows the returned secrets (the credentials unless the auth mode is
 * None, and the signing secret if signing is on) once in {@link RevealSecretDialogComponent};
 * later reads never include them. The page returns to the list when the dialog is closed.
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
  /** Exposed so the template can find the form control for each credential field. */
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

  /** Whether the subscription has an HMAC signing secret (edit mode; shown read-only). */
  readonly hasHmacSigningSecret = signal(false);

  /**
   * Whether the auth mode and credential fields are editable: always in create mode, and in edit
   * mode only after {@link startRotatingCredentials}.
   */
  readonly credentialsEditable = signal(!this.isEditMode);

  /** Whether the reveal-once dialog is shown; set after a create that returned at least one secret. */
  readonly revealDialogVisible = signal(false);

  /** The new subscription's secrets, shown once in {@link RevealSecretDialogComponent}. */
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

  /** The selected auth mode as a signal (the form's `valueChanges` is not one). */
  readonly selectedAuthMode = signal<WebhookAuthModeEnum>(WebhookAuthModeEnum.None);

  /** The credential fields the selected auth mode needs. */
  readonly credentialFields = computed(() => credentialFieldsForAuthMode(this.selectedAuthMode()));

  constructor() {
    this.form.controls.authMode.valueChanges.subscribe((mode) => this.selectedAuthMode.set(mode));
  }

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingSubscription();
  }

  /** This method enables the auth mode and credential fields so the operator can replace the credentials. */
  startRotatingCredentials(): void {
    this.credentialsEditable.set(true);
    this.form.controls.authMode.enable();
    this.form.controls.bearerToken.enable();
    this.form.controls.username.enable();
    this.form.controls.password.enable();
  }

  /**
   * This method creates the subscription (`POST`) or, in edit mode, updates it (`PATCH`),
   * including the auth mode and credentials only when {@link credentialsEditable} is `true`.
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
          // Stay on the page until the dialog is closed (onSecretDialogClosed).
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

  /** This method hides the dialog and returns to the subscription list. */
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

      // Edit mode starts with the auth fields disabled, so a save cannot change the auth mode
      // without new credentials (the backend would refuse it).
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
