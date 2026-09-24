import { Component, computed, inject, signal } from '@angular/core';
import { NonNullableFormBuilder, ReactiveFormsModule, Validators } from '@angular/forms';
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
 * This component is the full-system backup/restore page: a "Create a backup" section that
 * downloads an archive of every entity this server persists (plus the Data Protection key ring,
 * the shared TLS certificate, and the runtime configuration file), and a "Restore from a backup"
 * section that uploads one back and, once confirmed, wipes and replaces this server's own live
 * data with its contents. This is a single, standalone page with no list/form split and no route
 * beyond its own root -- there is exactly one backup archive an operator can create at a time,
 * and exactly one restore action, neither of which is itself a persisted, listable resource.
 *
 * The passphrase is optional on both sides, matching the real backend behavior
 * (`SystemBackupService.CreateBackupAsync`): supplied, the archive is encrypted with it;
 * omitted, the archive is written out deliberately unencrypted, never a weak or
 * default-derived key standing in for a real one. Skipping it is ill-advised, so the create
 * section shows a prominent warning the moment the passphrase field is left empty (see
 * {@link passphraseOmitted}), rather than merely relaxing the requirement silently.
 *
 * The restore section is shown only to a session holding `backup:write` (checked directly via
 * {@link AuthService.hasPermission}, mirroring `SettingsShellComponent`'s own "hide, don't merely
 * disable, a section the current session cannot use" precedent) -- a caller who can only create a
 * backup (`backup:read`) never sees the far more destructive restore form at all, even though the
 * route itself, gated only by `backup:read`, does not block navigating to this page in the first
 * place.
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

  /** Whether {@link ConfirmDialogComponent} is currently shown for this page's pending restore. */
  readonly confirmDialogVisible = signal(false);

  /** The text {@link ConfirmDialogComponent} is currently showing, if any. */
  readonly confirmDialogData = signal<ConfirmDialogData | null>(null);

  /** Resolves the {@link confirm} promise currently awaiting the operator's choice, if any. */
  private confirmResolve: ((confirmed: boolean) => void) | null = null;

  /** Whether the current session holds `backup:write` -- gates whether the restore section renders at all. */
  readonly canRestore = this.authService.hasPermission('backup:write');

  /** The fewest characters a supplied passphrase must contain -- see {@link MINIMUM_BACKUP_PASSPHRASE_LENGTH}'s own doc comment for where this real value comes from. */
  readonly minimumPassphraseLength = MINIMUM_BACKUP_PASSPHRASE_LENGTH;

  /**
   * The "Create a backup" form -- one optional passphrase field, confirmed by typing it twice
   * when supplied. Neither field is `Validators.required`: an empty passphrase is a deliberate,
   * valid choice to skip encryption entirely, surfaced instead via {@link passphraseOmitted}'s
   * own prominent warning rather than a validation error.
   */
  readonly createForm = this.formBuilder.group({
    passphrase: ['', [Validators.minLength(MINIMUM_BACKUP_PASSPHRASE_LENGTH)]],
    confirmPassphrase: [''],
  });

  /**
   * The "Restore from a backup" form -- an uploaded archive file plus the passphrase it was
   * encrypted with, if any. Left blank when restoring an archive that was itself created without
   * one; the backend detects which case applies from the archive's own contents, not from
   * whether this field is populated.
   */
  readonly restoreForm = this.formBuilder.group({
    passphrase: [''],
  });

  /** Tracks the create-section passphrase's own live value as a signal, so {@link passphraseOmitted} updates as the operator types (`valueChanges` itself is an `Observable`, not a signal). */
  private readonly createPassphraseValue = signal('');

  /** Whether the operator has currently left the create-section passphrase empty -- drives the "this backup will not be encrypted" warning. */
  readonly passphraseOmitted = computed(() => this.createPassphraseValue().length === 0);

  constructor() {
    this.createForm.controls.passphrase.valueChanges.subscribe((value) =>
      this.createPassphraseValue.set(value),
    );
  }

  /** The archive file most recently chosen in the restore section's own file input. */
  readonly selectedArchiveFile = signal<File | null>(null);

  /** Whether a backup is currently being created, disabling the create button while it runs. */
  readonly creatingBackup = signal(false);

  /** Whether a restore is currently in progress, disabling the restore button while it runs. */
  readonly restoringBackup = signal(false);

  /** The most recent create-section error, or `null` when there is none to show. */
  readonly createErrorMessage = signal<string | null>(null);

  /** The most recent restore-section error, or `null` when there is none to show. */
  readonly restoreErrorMessage = signal<string | null>(null);

  /** The summary returned by the most recently successful restore, shown until the page is left. */
  readonly lastRestoreSummary = signal<BackupRestoreResponse | null>(null);

  /**
   * This method reads the file the operator picked in the restore section's own file input --
   * a plain DOM `change` event handler, since Reactive Forms has no native binding for a file
   * input's own `FileList`.
   */
  onArchiveFileSelected(event: Event): void {
    const input = event.target as HTMLInputElement;
    this.selectedArchiveFile.set(input.files?.[0] ?? null);
  }

  /**
   * This method requests a real backup archive -- encrypted with the form's own confirmed
   * passphrase when one was supplied, deliberately unencrypted when it was left blank -- and
   * triggers a real browser download of it once the request succeeds.
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
   * This method opens a confirmation prompt naming exactly what a restore does (every entity
   * this server persists is wiped and replaced), and, if confirmed, uploads the selected archive
   * and passphrase to actually perform it.
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
   * operator confirms or dismisses it -- the declarative analogue of the old
   * `MatDialog.open(ConfirmDialogComponent, { data }).afterClosed()` this project used before
   * migrating onto CoreUI for Angular.
   */
  private confirm(data: ConfirmDialogData): Promise<boolean> {
    this.confirmDialogData.set(data);
    this.confirmDialogVisible.set(true);
    return new Promise<boolean>((resolve) => {
      this.confirmResolve = resolve;
    });
  }

  /** This method runs once {@link ConfirmDialogComponent} resolves, hiding it and settling the pending {@link confirm} promise. */
  onConfirmDialogResolved(confirmed: boolean): void {
    this.confirmDialogVisible.set(false);
    this.confirmResolve?.(confirmed);
    this.confirmResolve = null;
  }

  /** The entity-type/count pairs from {@link lastRestoreSummary}, sorted alphabetically for display. */
  restoredEntityCounts(): [string, number][] {
    const counts = this.lastRestoreSummary()?.entityCounts;
    if (!counts) return [];
    return Object.entries(counts).sort(([a], [b]) => a.localeCompare(b));
  }
}
