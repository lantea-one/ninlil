import { Component, computed, inject, signal } from '@angular/core';
import {
  AbstractControl,
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidationErrors,
  Validators,
} from '@angular/forms';
import { firstValueFrom } from 'rxjs';
import {
  AlertComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardHeaderComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { UserProfileDetail, UsersService } from '../../../api-client';
import { buildGravatarInitials, buildGravatarUrl } from '../../../shared/gravatar/gravatar.util';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';

/** The pixel size requested from Gravatar -- large enough to look sharp at this page's own 96px display size (see the stylesheet), matching a common `@2x`-friendly Gravatar convention. */
const AVATAR_SIZE_PIXELS = 192;

/**
 * This validator checks that {@link AccountProfileComponent.changePasswordForm}'s own
 * `newPassword`/`confirmNewPassword` fields hold the same value, reporting the mismatch on the
 * group itself (`confirmNewPassword` alone has nothing wrong with it in isolation) so the
 * template can show one clear message rather than a per-field error that would not make sense on
 * either field individually.
 */
function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const newPassword = control.get('newPassword')?.value;
  const confirmNewPassword = control.get('confirmNewPassword')?.value;
  return newPassword === confirmNewPassword ? null : { passwordsMismatch: true };
}

/**
 * This component is the current caller's own self-service Profile page -- reachable from the app
 * shell's own user menu, at `/account/profile`, gated only by `authGuard` (any signed-in
 * session), mirroring `AccountSecurityComponent`'s own self-service bar: `UsersController`'s own
 * `GetMine`/`PatchMine` actions carry no `[RequirePermission(...)]` either, since every action
 * here acts on the caller's own account, never anyone else's.
 *
 * Distinct from `AccountSecurityComponent` (authenticator/2FA/recovery codes): this page covers
 * the account's own profile identity -- display name, phone number, and a Gravatar-backed avatar
 * built client-side from the account's e-mail address. `UserEntity.DisplayName`/`PhoneNumber`
 * already existed as unused database columns before this page; this is their first real
 * consumer. The account's e-mail address itself is shown read-only -- changing it needs its own
 * re-verification flow this project does not yet build (see `UserProfileDetail`'s own doc
 * comment on the backend).
 *
 * The avatar itself is never fetched or computed server-side: the `<img>` element's own `src` is
 * a plain `gravatar.com` URL built from an MD5 hash of the account's e-mail address (see
 * `shared/gravatar/gravatar.util.ts`), fetched by the *viewer's own browser* the same way any
 * ordinary external image is. Gravatar's own `d=404` query parameter makes a lookup with no
 * registered avatar fail with a real HTTP 404 rather than silently serving a generic silhouette,
 * so this page's own `onAvatarError()` handler can reliably tell "no Gravatar for this address"
 * and "gravatar.com is entirely unreachable" (a fully offline homelab install) apart from an
 * actual photo -- both cases fall back identically to a local, CSS-only initials avatar, so the
 * offline case degrades to cosmetic-only (a plain-letter avatar instead of a photo), never a
 * functional break. Confirmed via `AskUserQuestion`, 2026-09-02 -- an opt-in toggle and a
 * local-only avatar were both considered and rejected as unnecessary given this fallback already
 * covers the offline case.
 *
 * This page also carries a self-service change-password section, wired to
 * `POST /api/users/me/change-password` (`UsersController.ChangeMyPassword`) -- a wrong current
 * password surfaces the real `IdentityResult` failure from the backend (via
 * {@link extractErrorMessage}'s own field-keyed validation handling), never a generic message.
 * See `ResetPasswordDialogComponent` (on `UserListComponent`) for the administrator-driven
 * counterpart, for an account that cannot supply its own current password at all.
 */
@Component({
  selector: 'app-account-profile',
  imports: [
    ReactiveFormsModule,
    AlertComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardHeaderComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    SpinnerComponent,
  ],
  templateUrl: './account-profile.component.html',
  styleUrl: './account-profile.component.scss',
})
export class AccountProfileComponent {
  private readonly usersApi = inject(UsersService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly saving = signal(false);
  readonly profile = signal<UserProfileDetail | null>(null);
  readonly errorMessage = signal<string | null>(null);

  /** Set once the Gravatar `<img>` element's own load fails (a real 404 or a fully offline install) -- switches the template over to the local initials fallback. */
  readonly avatarFailed = signal(false);

  readonly profileForm = this.formBuilder.group({
    displayName: ['', [Validators.maxLength(256)]],
    phoneNumber: ['', [Validators.maxLength(64)]],
  });

  readonly changingPassword = signal(false);
  readonly changePasswordErrorMessage = signal<string | null>(null);

  /**
   * This constant is the client-side floor {@link changePasswordForm}'s own `newPassword` field
   * enforces before ever submitting -- a fixed baseline independent of this server's own
   * admin-configurable password-complexity policy (`IdentitySettingsController`'s
   * `Password.RequiredLength`, which an ordinary self-service caller has no permission to read).
   * A submission that clears this floor but still fails that server-side policy is rejected with
   * the real reason, surfaced via {@link changePasswordErrorMessage}.
   */
  readonly passwordMinLength = 12;

  readonly changePasswordForm = this.formBuilder.group(
    {
      currentPassword: ['', [Validators.required]],
      newPassword: ['', [Validators.required, Validators.minLength(this.passwordMinLength)]],
      confirmNewPassword: ['', [Validators.required]],
    },
    { validators: [passwordsMatchValidator] },
  );

  /** The Gravatar image URL for the current profile's own e-mail address, or `null` before the profile has loaded (or if the account somehow has no e-mail address at all). */
  readonly avatarUrl = computed(() => {
    const current = this.profile();
    return current?.email ? buildGravatarUrl(current.email, AVATAR_SIZE_PIXELS) : null;
  });

  /** The initials shown by the local fallback avatar, derived from the current profile's own display name (or, absent that, its sign-in user name). */
  readonly avatarInitials = computed(() =>
    buildGravatarInitials(this.profile()?.displayName, this.profile()?.userName),
  );

  constructor() {
    void this.loadProfile();
  }

  /**
   * This method runs once the Gravatar `<img>` element's own load fails, switching the template
   * over to the local initials fallback -- see this class's own doc comment for why this single
   * handler correctly covers both "no Gravatar registered for this address" and "this install has
   * no path to gravatar.com at all" identically.
   */
  onAvatarError(): void {
    this.avatarFailed.set(true);
  }

  /**
   * This method saves the form's own current display name/phone number via `PATCH
   * /api/users/me`, sending an explicit JSON `null` (via RFC 7396 merge-patch semantics) for a
   * field the operator has cleared entirely, and applies the response -- rather than trusting the
   * submitted form values verbatim -- back onto both this page's own state and the form.
   */
  async save(): Promise<void> {
    if (this.profileForm.invalid || this.saving()) return;

    this.saving.set(true);
    this.errorMessage.set(null);

    const { displayName, phoneNumber } = this.profileForm.getRawValue();
    const trimmedDisplayName = displayName.trim();
    const trimmedPhoneNumber = phoneNumber.trim();

    try {
      const updated = await firstValueFrom(
        this.usersApi.apiUsersMePatch({
          body: {
            displayName: trimmedDisplayName.length > 0 ? trimmedDisplayName : null,
            phoneNumber: trimmedPhoneNumber.length > 0 ? trimmedPhoneNumber : null,
          },
        }),
      );
      this.applyProfile(updated);
      this.notification.success('Profile saved.');
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not save your profile.'));
    } finally {
      this.saving.set(false);
    }
  }

  /**
   * This method changes the current caller's own password via
   * `POST /api/users/me/change-password`. A wrong current password reports the backend's own
   * real error message (via {@link extractErrorMessage}), never a generic one -- see this class's
   * own doc comment.
   */
  async changePassword(): Promise<void> {
    if (this.changePasswordForm.invalid || this.changingPassword()) return;

    this.changingPassword.set(true);
    this.changePasswordErrorMessage.set(null);

    const { currentPassword, newPassword } = this.changePasswordForm.getRawValue();

    try {
      await firstValueFrom(
        this.usersApi.apiUsersMeChangePasswordPost({
          changePasswordRequest: { currentPassword, newPassword },
        }),
      );
      this.changePasswordForm.reset({
        currentPassword: '',
        newPassword: '',
        confirmNewPassword: '',
      });
      this.notification.success('Password changed.');
    } catch (error) {
      this.changePasswordErrorMessage.set(
        extractErrorMessage(error, 'Could not change your password.'),
      );
    } finally {
      this.changingPassword.set(false);
    }
  }

  private async loadProfile(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const profile = await firstValueFrom(this.usersApi.apiUsersMeGet());
      this.applyProfile(profile);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load your profile.'));
    } finally {
      this.loading.set(false);
    }
  }

  /**
   * This method applies a `UserProfileDetail` response to both this page's own state and the
   * editable form, and resets the avatar-fallback flag -- a fresh `GET`/`PATCH` result always
   * deserves a fresh attempt at loading the Gravatar image (its own URL is unchanged, since
   * e-mail is read-only here, but there is no reason to stay stuck on a stale failure flag from
   * before this particular load either).
   */
  private applyProfile(profile: UserProfileDetail): void {
    this.profile.set(profile);
    this.avatarFailed.set(false);
    this.profileForm.setValue({
      displayName: profile.displayName ?? '',
      phoneNumber: profile.phoneNumber ?? '',
    });
  }
}
