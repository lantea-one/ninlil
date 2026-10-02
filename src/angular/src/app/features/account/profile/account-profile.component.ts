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

/** The size requested from Gravatar: twice the 96px display size, so the avatar stays sharp on high-density screens. */
const AVATAR_SIZE_PIXELS = 192;

/**
 * This validator checks that `newPassword` and `confirmNewPassword` in
 * {@link AccountProfileComponent.changePasswordForm} match. It reports the mismatch on the group,
 * since neither field is wrong on its own.
 */
function passwordsMatchValidator(control: AbstractControl): ValidationErrors | null {
  const newPassword = control.get('newPassword')?.value;
  const confirmNewPassword = control.get('confirmNewPassword')?.value;
  return newPassword === confirmNewPassword ? null : { passwordsMismatch: true };
}

/**
 * This component is the signed-in user's Profile page (`/account/profile`), opened from the
 * account menu. It needs only a session: `GET` and `PATCH /api/users/me` and
 * `POST /api/users/me/change-password` act on the caller's account and require no permission.
 *
 * <p>It edits the display name and phone number, shows the e-mail address read-only, and shows a
 * Gravatar avatar built in the browser from the e-mail address (`shared/gravatar/`). The browser
 * loads the avatar from gravatar.com; when that fails, because the address has no Gravatar or the
 * server has no internet access, the page shows initials instead.
 *
 * <p>The change-password section needs the current password. An administrator can reset the
 * password of an account that does not know it from the Users page (`ResetPasswordDialogComponent`).
 * Two-factor settings are on the Security page (`AccountSecurityComponent`).
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

  /** Set when the Gravatar image fails to load (a 404 or no internet access); the template then shows initials. */
  readonly avatarFailed = signal(false);

  readonly profileForm = this.formBuilder.group({
    displayName: ['', [Validators.maxLength(256)]],
    phoneNumber: ['', [Validators.maxLength(64)]],
  });

  readonly changingPassword = signal(false);
  readonly changePasswordErrorMessage = signal<string | null>(null);

  /**
   * The minimum new-password length checked in the browser. It is fixed, because the server's
   * password policy (Settings > Identity, `identitysettings:read`) is not readable by every user.
   * A password that passes here but fails the server policy comes back with the server's reason
   * in {@link changePasswordErrorMessage}.
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

  /** The Gravatar URL for the profile's e-mail address, or `null` before loading or without an address. */
  readonly avatarUrl = computed(() => {
    const current = this.profile();
    return current?.email ? buildGravatarUrl(current.email, AVATAR_SIZE_PIXELS) : null;
  });

  /** The initials for the fallback avatar, from the display name or else the user name. */
  readonly avatarInitials = computed(() =>
    buildGravatarInitials(this.profile()?.displayName, this.profile()?.userName),
  );

  constructor() {
    void this.loadProfile();
  }

  /**
   * This method runs when the Gravatar image fails to load and switches to the initials avatar.
   * The same fallback serves "no Gravatar for this address" and "gravatar.com unreachable".
   */
  onAvatarError(): void {
    this.avatarFailed.set(true);
  }

  /**
   * This method saves the display name and phone number through `PATCH /api/users/me`. A
   * cleared field is sent as JSON `null`, which clears it (RFC 7396 merge patch). The form is then
   * refilled from the response, not from what was typed.
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
   * This method changes the signed-in user's password through
   * `POST /api/users/me/change-password` and clears the form on success. Errors, such as a wrong
   * current password, show the backend's message.
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
   * This method stores a `UserProfileDetail` response, refills the form and clears the avatar
   * failure flag so the Gravatar image is tried again.
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
