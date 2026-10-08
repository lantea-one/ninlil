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
  ListGroupDirective,
  ListGroupItemDirective,
  SpinnerComponent,
} from '@coreui/angular';
import { IconDirective } from '@coreui/icons-angular';
import { AuthService } from '../../core/auth/auth.service';
import { BackupRestoreResponse, BackupService as BackupApiService } from '../../api-client';
import {
  ConfirmDialogComponent,
  ConfirmDialogData,
} from '../../shared/confirm-dialog/confirm-dialog.component';
import { extractErrorMessage } from '../../shared/http-error/http-error.util';
import { NotificationService } from '../../shared/notification/notification.service';
import { BackupDownloadService } from './backup-download.service';
import {
  MINIMUM_BACKUP_PASSPHRASE_LENGTH,
  buildBackupFileName,
  triggerBlobDownload,
} from './backup.model';

/**
 * Rejects a passphrase that is not empty but only whitespace, as the backend does with a 400.
 * An empty value passes, since that means an unencrypted backup. The value is never trimmed.
 */
function notBlank(control: AbstractControl<string>): ValidationErrors | null {
  const value = control.value;
  return value.length > 0 && value.trim().length === 0 ? { blank: true } : null;
}

/**
 * This component is the Backup & Restore page (`/backup`, `backup:read`). "Create a backup"
 * downloads an archive of all stored data plus the Data Protection key ring, the TLS certificate
 * and the runtime configuration file (`POST /api/backup`, `backup:read`, through
 * {@link BackupDownloadService}). "Restore from a backup" uploads an archive and, after a
 * confirmation, wipes all stored data and replaces it (`POST /api/backup/restore`,
 * `backup:write`).
 *
 * <p>The passphrase is optional. With one the backend encrypts the archive; without one it writes
 * it unencrypted. The create section shows a warning while the passphrase field is empty
 * ({@link passphraseOmitted}).
 *
 * <p>The restore section is shown only with `backup:write` ({@link canRestore}); a user who can
 * only create backups never sees it.
 */
@Component({
  selector: 'app-backup',
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
    IconDirective,
    ListGroupDirective,
    ListGroupItemDirective,
    SpinnerComponent,
    ConfirmDialogComponent,
  ],
  templateUrl: './backup.component.html',
  styleUrl: './backup.component.scss',
})
export class BackupComponent {
  private readonly formBuilder = inject(NonNullableFormBuilder);
  private readonly backupDownloadService = inject(BackupDownloadService);
  private readonly backupApi = inject(BackupApiService);
  private readonly authService = inject(AuthService);
  private readonly notificationService = inject(NotificationService);

  /** Whether the restore confirmation dialog is shown. */
  readonly confirmDialogVisible = signal(false);

  /** The text the confirmation dialog shows, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Settles the pending {@link confirm} promise, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /** Whether the user holds `backup:write`; the restore section renders only then. */
  readonly canRestore = this.authService.hasPermission('backup:write');

  /** The minimum passphrase length; see {@link MINIMUM_BACKUP_PASSPHRASE_LENGTH}. */
  readonly minimumPassphraseLength = MINIMUM_BACKUP_PASSPHRASE_LENGTH;

  /**
   * The "Create a backup" form: an optional passphrase, typed twice. Neither field is required,
   * since an empty passphrase means no encryption; {@link passphraseOmitted} warns about it
   * instead of a validation error.
   */
  readonly createForm = this.formBuilder.group({
    passphrase: ['', [notBlank, Validators.minLength(MINIMUM_BACKUP_PASSPHRASE_LENGTH)]],
    confirmPassphrase: [''],
  });

  /**
   * The "Restore from a backup" form: the passphrase the archive was encrypted with, blank for an
   * unencrypted archive. The backend reads from the archive header whether it is encrypted. The
   * file itself is in {@link selectedArchiveFile}.
   */
  readonly restoreForm = this.formBuilder.group({
    passphrase: [''],
  });

  /** The create passphrase as a signal, so {@link passphraseOmitted} updates while typing. */
  private readonly createPassphraseValue = signal('');

  /** Whether the create passphrase is empty; shows the "not encrypted" warning. */
  readonly passphraseOmitted = computed(() => this.createPassphraseValue().length === 0);

  constructor() {
    this.createForm.controls.passphrase.valueChanges.subscribe((value) =>
      this.createPassphraseValue.set(value),
    );
  }

  /** The archive file chosen in the restore section. */
  readonly selectedArchiveFile = signal<File | null>(null);

  /** Whether a backup is being created; disables the create button. */
  readonly creatingBackup = signal(false);

  /** Whether a restore is running; disables the restore button. */
  readonly restoringBackup = signal(false);

  /** The most recent create-section error, or `null` when there is none to show. */
  readonly createErrorMessage = signal<string | null>(null);

  /** The most recent restore-section error, or `null` when there is none to show. */
  readonly restoreErrorMessage = signal<string | null>(null);

  /** The summary of the last successful restore, shown until the page is left. */
  readonly lastRestoreSummary = signal<BackupRestoreResponse | null>(null);

  /**
   * This method stores the file picked in the restore section. It is a plain `change` handler
   * because Reactive Forms cannot bind a file input.
   */
  onArchiveFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedArchiveFile.set(input.files?.[0] ?? null);
  }

  /**
   * This method checks that the two passphrases match, requests the archive (encrypted when a
   * passphrase was given) and saves it as a browser download. Errors show in the create
   * section.
   */
  async createBackup(): Promise<void> {
    this.createErrorMessage.set(null);

    const { passphrase, confirmPassphrase } = this.createForm.getRawValue();
    if (passphrase !== confirmPassphrase) {
      this.createErrorMessage.set('The two passphrases you entered do not match.');
      return;
    }

    if (this.createForm.invalid) {
      this.createForm.markAllAsTouched();
      return;
    }

    this.creatingBackup.set(true);
    try {
      const archive = await this.backupDownloadService.downloadBackup(passphrase);
      triggerBlobDownload(archive, buildBackupFileName());
      this.notificationService.success('Backup archive downloaded.');
      this.createForm.reset();
    } catch (error: unknown) {
      this.createErrorMessage.set(
        error instanceof Error ? error.message : 'Failed to create the backup archive.',
      );
    } finally {
      this.creatingBackup.set(false);
    }
  }

  /**
   * This method asks for confirmation (all stored data is wiped and replaced), then uploads the
   * archive and passphrase and shows the restored entity counts.
   */
  async confirmAndRestoreBackup(): Promise<void> {
    this.restoreErrorMessage.set(null);
    this.lastRestoreSummary.set(null);

    const file = this.selectedArchiveFile();
    if (!file) {
      this.restoreErrorMessage.set('Choose a backup archive file first.');
      return;
    }

    if (this.restoreForm.invalid) {
      this.restoreForm.markAllAsTouched();
      return;
    }

    const confirmData: ConfirmDialogData = {
      title: 'Restore from this backup?',
      message:
        'This wipes and replaces every zone, record, key, credential, user, role, and every ' +
        'other entity this server currently persists with the contents of the archive you ' +
        'selected. This cannot be undone. Are you sure?',
      confirmLabel: 'Restore',
    };

    const confirmed = await this.confirm(confirmData);
    if (!confirmed) return;

    const { passphrase } = this.restoreForm.getRawValue();

    this.restoringBackup.set(true);
    try {
      const summary = await firstValueFrom(
        this.backupApi.apiBackupRestorePost({ archive: file, passphrase }),
      );

      this.lastRestoreSummary.set(summary ?? null);
      this.notificationService.success('Restore succeeded.');
      this.restoreForm.reset();
      this.selectedArchiveFile.set(null);
    } catch (error: unknown) {
      this.restoreErrorMessage.set(
        extractErrorMessage(error, 'Failed to restore from this backup archive.'),
      );
    } finally {
      this.restoringBackup.set(false);
    }
  }

  /**
   * This method shows {@link ConfirmDialogComponent} with the given text and resolves once the
   * operator confirms or dismisses it.
   */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method hides the confirmation dialog and settles the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  /** This method returns the entity type and count pairs from {@link lastRestoreSummary}, sorted by name. */
  restoredEntityCounts(): [string, number][] {
    const counts = this.lastRestoreSummary()?.entityCounts;
    if (!counts) return [];
    return Object.entries(counts).sort(([a], [b]) => a.localeCompare(b));
  }
}
