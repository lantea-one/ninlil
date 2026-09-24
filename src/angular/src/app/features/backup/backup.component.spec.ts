import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { BackupService as BackupApiService } from '../../api-client';
import { AuthService } from '../../core/auth/auth.service';
import { NotificationService } from '../../shared/notification/notification.service';
import { BackupDownloadService } from './backup-download.service';
import { BackupComponent } from './backup.component';

describe('BackupComponent', () => {
  let backupApiSpy: { apiBackupRestorePost: jasmine.Spy };
  let backupDownloadSpy: { downloadBackup: jasmine.Spy };
  let authServiceStub: { hasPermission: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };

  async function createComponent(canRestore: boolean) {
    backupApiSpy = jasmine.createSpyObj('BackupService', ['apiBackupRestorePost']);
    backupDownloadSpy = jasmine.createSpyObj('BackupDownloadService', ['downloadBackup']);
    authServiceStub = { hasPermission: jasmine.createSpy().and.returnValue(canRestore) };
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);

    await TestBed.configureTestingModule({
      imports: [BackupComponent],
      providers: [
        { provide: BackupApiService, useValue: backupApiSpy },
        { provide: BackupDownloadService, useValue: backupDownloadSpy },
        { provide: AuthService, useValue: authServiceStub },
        { provide: NotificationService, useValue: notificationSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(BackupComponent);
    fixture.detectChanges();
    return fixture;
  }

  it('checks backup:write via AuthService.hasPermission to decide whether restore is offered', async () => {
    const fixture = await createComponent(true);

    expect(authServiceStub.hasPermission).toHaveBeenCalledWith('backup:write');
    expect(fixture.componentInstance.canRestore).toBeTrue();
  });

  it('hides restore capability when the session lacks backup:write', async () => {
    const fixture = await createComponent(false);

    expect(fixture.componentInstance.canRestore).toBeFalse();
  });

  it('rejects create when the two passphrase fields do not match, without calling the API', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;

    component.createForm.setValue({
      passphrase: 'a-strong-test-passphrase',
      confirmPassphrase: 'a-different-passphrase',
    });

    await component.createBackup();

    expect(backupDownloadSpy.downloadBackup).not.toHaveBeenCalled();
    expect(component.createErrorMessage()).toContain('do not match');
  });

  it('downloads and triggers a save of the archive when the create form is valid', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;
    const archiveBlob = new Blob(['archive bytes']);
    backupDownloadSpy.downloadBackup.and.resolveTo(archiveBlob);
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake-url');
    spyOn(URL, 'revokeObjectURL');
    spyOn(HTMLAnchorElement.prototype, 'click');

    component.createForm.setValue({
      passphrase: 'a-strong-test-passphrase',
      confirmPassphrase: 'a-strong-test-passphrase',
    });

    await component.createBackup();

    expect(backupDownloadSpy.downloadBackup).toHaveBeenCalledWith('a-strong-test-passphrase');
    expect(URL.createObjectURL).toHaveBeenCalledWith(archiveBlob);
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(component.createErrorMessage()).toBeNull();
  });

  it('shows a prominent unencrypted-backup warning while the passphrase field is empty', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;

    expect(component.passphraseOmitted()).toBeTrue();

    component.createForm.controls.passphrase.setValue('a-strong-test-passphrase');
    expect(component.passphraseOmitted()).toBeFalse();

    component.createForm.controls.passphrase.setValue('');
    expect(component.passphraseOmitted()).toBeTrue();
  });

  it('allows creating a backup with no passphrase at all (skips encryption deliberately)', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;
    const archiveBlob = new Blob(['unencrypted archive bytes']);
    backupDownloadSpy.downloadBackup.and.resolveTo(archiveBlob);
    spyOn(URL, 'createObjectURL').and.returnValue('blob:fake-url');
    spyOn(URL, 'revokeObjectURL');
    spyOn(HTMLAnchorElement.prototype, 'click');

    component.createForm.setValue({ passphrase: '', confirmPassphrase: '' });

    await component.createBackup();

    expect(backupDownloadSpy.downloadBackup).toHaveBeenCalledWith('');
    expect(notificationSpy.success).toHaveBeenCalled();
    expect(component.createErrorMessage()).toBeNull();
  });

  it('rejects a create with a non-empty passphrase shorter than the minimum length', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;

    component.createForm.setValue({ passphrase: 'short', confirmPassphrase: 'short' });

    await component.createBackup();

    expect(backupDownloadSpy.downloadBackup).not.toHaveBeenCalled();
    expect(component.createForm.controls.passphrase.hasError('minlength')).toBeTrue();
  });

  it('surfaces a failed backup creation as a visible error message, not a toast', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;
    backupDownloadSpy.downloadBackup.and.rejectWith(new Error('The passphrase is incorrect.'));

    component.createForm.setValue({
      passphrase: 'a-strong-test-passphrase',
      confirmPassphrase: 'a-strong-test-passphrase',
    });

    await component.createBackup();

    expect(component.createErrorMessage()).toBe('The passphrase is incorrect.');
    expect(notificationSpy.success).not.toHaveBeenCalled();
  });

  it('requires an archive file to be selected before attempting a restore', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;
    component.restoreForm.setValue({ passphrase: 'a-strong-test-passphrase' });

    await component.confirmAndRestoreBackup();

    expect(component.confirmDialogVisible()).toBeFalse();
    expect(component.restoreErrorMessage()).toContain('Choose a backup archive');
  });

  it('never calls the restore API when the confirmation dialog is dismissed', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;
    component.selectedArchiveFile.set(new File(['bytes'], 'backup.ltbk'));
    component.restoreForm.setValue({ passphrase: 'a-strong-test-passphrase' });

    const restorePromise = component.confirmAndRestoreBackup();
    component.onConfirmDialogResolved(false);
    await restorePromise;

    expect(backupApiSpy.apiBackupRestorePost).not.toHaveBeenCalled();
  });

  it('restores and shows the returned summary once the destructive confirmation is accepted', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;
    const archiveFile = new File(['bytes'], 'backup.ltbk');
    component.selectedArchiveFile.set(archiveFile);
    component.restoreForm.setValue({ passphrase: 'a-strong-test-passphrase' });
    backupApiSpy.apiBackupRestorePost.and.returnValue(
      of({
        totalEntitiesImported: 3,
        entityCounts: { Zones: 1, Records: 2 },
        note: 'Restart this process now.',
      }),
    );

    const restorePromise = component.confirmAndRestoreBackup();
    component.onConfirmDialogResolved(true);
    await restorePromise;

    expect(backupApiSpy.apiBackupRestorePost).toHaveBeenCalledWith({
      archive: archiveFile,
      passphrase: 'a-strong-test-passphrase',
    });
    expect(component.lastRestoreSummary()?.totalEntitiesImported).toBe(3);
    expect(component.restoredEntityCounts()).toEqual([
      ['Records', 2],
      ['Zones', 1],
    ]);
    expect(notificationSpy.success).toHaveBeenCalled();
  });

  it('restores with a blank passphrase when the archive being restored was unencrypted', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;
    const archiveFile = new File(['bytes'], 'backup.ltbk');
    component.selectedArchiveFile.set(archiveFile);
    component.restoreForm.setValue({ passphrase: '' });
    backupApiSpy.apiBackupRestorePost.and.returnValue(
      of({
        totalEntitiesImported: 1,
        entityCounts: { Zones: 1 },
        note: 'Restart this process now.',
      }),
    );

    const restorePromise = component.confirmAndRestoreBackup();
    component.onConfirmDialogResolved(true);
    await restorePromise;

    expect(backupApiSpy.apiBackupRestorePost).toHaveBeenCalledWith({
      archive: archiveFile,
      passphrase: '',
    });
    expect(component.lastRestoreSummary()?.totalEntitiesImported).toBe(1);
  });

  it('surfaces a failed restore as a visible error message, not a toast', async () => {
    const fixture = await createComponent(true);
    const component = fixture.componentInstance;
    component.selectedArchiveFile.set(new File(['bytes'], 'backup.ltbk'));
    component.restoreForm.setValue({ passphrase: 'the-wrong-passphrase' });
    backupApiSpy.apiBackupRestorePost.and.returnValue(
      throwError(
        () =>
          new HttpErrorResponse({
            status: 400,
            error: { detail: 'Failed to decrypt this backup archive.' },
          }),
      ),
    );

    const restorePromise = component.confirmAndRestoreBackup();
    component.onConfirmDialogResolved(true);
    await restorePromise;

    expect(component.restoreErrorMessage()).toBe('Failed to decrypt this backup archive.');
    expect(notificationSpy.success).not.toHaveBeenCalled();
    expect(component.lastRestoreSummary()).toBeNull();
  });
});
