import { Component, OnInit, inject, signal } from '@angular/core';
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
import { TsigKeysService } from '../../../api-client';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  RevealSecretDialogComponent,
  RevealSecretDialogData,
} from '../../../shared/reveal-secret-dialog/reveal-secret-dialog.component';
import { TSIG_ALGORITHM_OPTIONS } from '../tsig-key.model';

/**
 * This component is both the "create a TSIG key" and "edit a TSIG key" page -- one component for
 * both, distinguished by whether the route supplies an `id` (see `app.routes.ts`), mirroring
 * `ZoneFormComponent`'s own precedent. Only create mode exposes a `secret` field at all --
 * `TsigKeysController.Replace`'s own `TsigKeyWriteRequest` has no such field, since rotating a
 * key's own shared secret has no later point at which the new value could be shown again through
 * a `PUT`'s own no-body `204` response; rotating one means deleting and recreating the key. Edit
 * mode here therefore only ever touches name/algorithm/disabled/comments -- never the secret --
 * so there is no "edit the secret" affordance anywhere in this page, in either mode.
 *
 * This is where this project's own reveal-once secret pattern is established: on a successful
 * create, this component shows {@link RevealSecretDialogComponent} with the response's own
 * `secret` field (server-generated when the caller left it blank, or the caller's own supplied
 * value echoed back either way) before navigating away -- the one and only place in this whole
 * feature a TSIG key's shared secret is ever shown, matching `TsigKeyDetail`'s own deliberate
 * omission of it from every later `GET`/list response. The dialog is embedded declaratively
 * (`[visible]`/`[data]`/`(closed)`) rather than opened imperatively -- see
 * {@link RevealSecretDialogComponent}'s own doc comment for why -- so navigation away from this
 * page only happens once the operator has explicitly dismissed it via {@link onSecretDialogClosed}.
 */
@Component({
  selector: 'app-tsig-key-form',
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
  templateUrl: './tsig-key-form.component.html',
  styleUrl: './tsig-key-form.component.scss',
})
export class TsigKeyFormComponent implements OnInit {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly tsigKeysApi = inject(TsigKeysService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  private readonly keyId = this.route.snapshot.paramMap.get('id');

  /** Whether this instance is editing an existing key rather than creating a new one. */
  readonly isEditMode = this.keyId !== null;

  readonly algorithmOptions = TSIG_ALGORITHM_OPTIONS;

  readonly loading = signal(this.isEditMode);
  readonly submitting = signal(false);
  readonly errorMessage = signal<string | null>(null);

  /** Whether {@link RevealSecretDialogComponent} is currently shown -- set once, right after a successful create, and cleared only once the operator dismisses it. */
  readonly revealDialogVisible = signal(false);

  /** The just-created key's own shared secret, shown exactly once via {@link RevealSecretDialogComponent}. */
  readonly revealDialogData = signal<RevealSecretDialogData | null>(null);

  readonly form = this.formBuilder.group({
    name: ['', [Validators.required]],
    algorithmName: [TSIG_ALGORITHM_OPTIONS[0], [Validators.required]],
    secret: [''],
    isDisabled: [false],
    comments: [''],
  });

  ngOnInit(): void {
    if (this.isEditMode) void this.loadExistingKey();
  }

  /** This method creates (`POST`) or fully replaces (`PUT`) the key, per {@link isEditMode}. */
  async save(): Promise<void> {
    if (this.form.invalid || this.submitting()) return;

    this.submitting.set(true);
    this.errorMessage.set(null);

    const value = this.form.getRawValue();

    try {
      if (this.isEditMode && this.keyId) {
        await firstValueFrom(
          this.tsigKeysApi.apiTsigKeysIdPut({
            id: this.keyId,
            tsigKeyWriteRequest: {
              name: value.name,
              algorithmName: value.algorithmName,
              isDisabled: value.isDisabled,
              comments: value.comments.trim().length > 0 ? value.comments : null,
            },
          }),
        );
        this.notification.success('Key saved.');
        await this.router.navigate(['/tsig-keys']);
      } else {
        const created = await firstValueFrom(
          this.tsigKeysApi.apiTsigKeysPost({
            tsigKeyCreateRequest: {
              name: value.name,
              algorithmName: value.algorithmName,
              secret: value.secret.trim().length > 0 ? value.secret : null,
              isDisabled: value.isDisabled,
              comments: value.comments.trim().length > 0 ? value.comments : null,
            },
          }),
        );

        // Reveal the plaintext secret exactly once -- see this component's own class-level doc
        // comment for why this is the one place it is ever shown. Navigation away is deferred to
        // onSecretDialogClosed(), once the operator has actually dismissed the dialog.
        this.revealDialogData.set({
          title: 'TSIG key created',
          message:
            'Copy this shared secret now -- it will never be shown again. Configure it on the secondary server or dynamic-update client this key authenticates.',
          secrets: [{ label: 'Shared secret', value: created.secret ?? '' }],
        });
        this.revealDialogVisible.set(true);
      }
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save this key.'));
    } finally {
      this.submitting.set(false);
    }
  }

  /** This method runs once the operator dismisses the reveal-once secret dialog, navigating away only now that they have confirmed saving the value. */
  onSecretDialogClosed(): void {
    this.revealDialogVisible.set(false);
    void this.router.navigate(['/tsig-keys']);
  }

  private async loadExistingKey(): Promise<void> {
    if (!this.keyId) return;

    try {
      const key = await firstValueFrom(this.tsigKeysApi.apiTsigKeysIdGet({ id: this.keyId }));

      this.form.patchValue({
        name: key.name ?? '',
        algorithmName: key.algorithmName ?? TSIG_ALGORITHM_OPTIONS[0],
        isDisabled: key.isDisabled ?? false,
        comments: key.comments ?? '',
      });
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load this key.'));
    } finally {
      this.loading.set(false);
    }
  }
}
