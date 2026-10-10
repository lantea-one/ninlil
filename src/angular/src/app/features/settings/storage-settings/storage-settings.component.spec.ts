import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { StorageEngineEnum, StorageService, StorageStatus } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { StorageSettingsComponent } from './storage-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Storage page: loading the status, per-engine validation, scheduling a
 * switch and its toast, the pending-switch card and cancel, the last outcome, the `targetHasData`
 * warning, permissions and errors. TestBed with a spy-backed `StorageService`,
 * `NotificationService` and a stubbed `AuthService`.
 */
describe('StorageSettingsComponent', () => {
  let apiSpy: {
    apiStorageGet: jasmine.Spy;
    apiStorageSwitchPost: jasmine.Spy;
    apiStorageSwitchDelete: jasmine.Spy;
  };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let authSpy: { hasPermission: jasmine.Spy };

  const allEngines = [
    StorageEngineEnum.Sqlite,
    StorageEngineEnum.PostgreSql,
    StorageEngineEnum.MySql,
    StorageEngineEnum.SqlServer,
  ];

  async function createComponent(options?: {
    configureApi?: () => void;
    canManage?: boolean;
    status?: StorageStatus;
  }) {
    apiSpy = jasmine.createSpyObj('StorageService', [
      'apiStorageGet',
      'apiStorageSwitchPost',
      'apiStorageSwitchDelete',
    ]);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    authSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authSpy.hasPermission.and.returnValue(options?.canManage ?? true);

    apiSpy.apiStorageGet.and.returnValue(
      of(
        options?.status ?? { engine: StorageEngineEnum.Sqlite, switchSupportedEngines: allEngines },
      ),
    );
    options?.configureApi?.();

    await TestBed.configureTestingModule({
      imports: [StorageSettingsComponent],
      providers: [
        { provide: StorageService, useValue: apiSpy },
        { provide: NotificationService, useValue: notificationSpy },
        { provide: AuthService, useValue: authSpy },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(StorageSettingsComponent);
    fixture.detectChanges();
    await flushMicrotasks();
    fixture.detectChanges();
    return fixture;
  }

  it('loads the current status on init', async () => {
    const fixture = await createComponent();

    expect(apiSpy.apiStorageGet).toHaveBeenCalled();
    expect(fixture.componentInstance.status()?.engine).toBe(StorageEngineEnum.Sqlite);
    expect(fixture.componentInstance.loading()).toBeFalse();
  });

  it('the sqlite file path is required by default, and the form is valid once it is filled in', async () => {
    const fixture = await createComponent();

    expect(fixture.componentInstance.form.valid).toBeFalse();

    fixture.componentInstance.form.patchValue({
      targetEngine: StorageEngineEnum.Sqlite,
      sqliteFilePath: '/var/lib/ninlil/new.sqlite3',
    });

    expect(fixture.componentInstance.form.valid).toBeTrue();
  });

  it('switching targetEngine to PostgreSql requires host/database name/username/ssl mode, not the sqlite file path', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.controls.targetEngine.setValue(StorageEngineEnum.PostgreSql);

    // The sqlite-only field is no longer required once a different engine is selected.
    expect(fixture.componentInstance.form.controls.sqliteFilePath.valid).toBeTrue();
    // The PostgreSQL fields are required until filled in.
    expect(fixture.componentInstance.form.valid).toBeFalse();

    fixture.componentInstance.form.patchValue({
      postgreSqlHost: 'db.example.internal',
      postgreSqlDatabaseName: 'ninlil',
      postgreSqlUserName: 'ninlil_app',
    });

    expect(fixture.componentInstance.form.valid).toBeTrue();
  });

  it('a PostgreSQL port outside 1-65535 is rejected client-side', async () => {
    const fixture = await createComponent();

    fixture.componentInstance.form.controls.targetEngine.setValue(StorageEngineEnum.PostgreSql);
    fixture.componentInstance.form.patchValue({
      postgreSqlHost: 'db.example.internal',
      postgreSqlDatabaseName: 'ninlil',
      postgreSqlUserName: 'ninlil_app',
      postgreSqlPort: 70000,
    });

    expect(fixture.componentInstance.form.valid).toBeFalse();

    fixture.componentInstance.form.patchValue({ postgreSqlPort: 5432 });

    expect(fixture.componentInstance.form.valid).toBeTrue();
  });

  it('switchEngine() posts only the structured section matching the target engine and shows the server note as the toast', async () => {
    const note = 'Set NINLIL_STORAGE_ENGINE and restart Ninlil.';
    const fixture = await createComponent({
      configureApi: () => {
        apiSpy.apiStorageSwitchPost.and.returnValue(
          of({
            targetEngine: StorageEngineEnum.PostgreSql,
            appliesAutomatically: false,
            targetHasData: false,
            note,
          }),
        );
      },
    });

    fixture.componentInstance.form.controls.targetEngine.setValue(StorageEngineEnum.PostgreSql);
    fixture.componentInstance.form.patchValue({
      postgreSqlHost: 'db.example.internal',
      postgreSqlPort: 5432,
      postgreSqlDatabaseName: 'ninlil',
      postgreSqlUserName: 'ninlil_app',
      postgreSqlPassword: 'correct-horse-battery-staple',
      postgreSqlEncryptionMode: 'Require',
    });
    await fixture.componentInstance.switchEngine();

    expect(apiSpy.apiStorageSwitchPost).toHaveBeenCalledWith({
      storageEngineSwitchApiRequest: {
        targetEngine: StorageEngineEnum.PostgreSql,
        postgreSql: {
          host: 'db.example.internal',
          port: 5432,
          databaseName: 'ninlil',
          userName: 'ninlil_app',
          password: 'correct-horse-battery-staple',
          encryptionMode: 'Require',
        },
      },
    });
    expect(notificationSpy.success).toHaveBeenCalledWith(note);
    // The password is cleared after a successful request; it is never returned.
    expect(fixture.componentInstance.form.controls.postgreSqlPassword.value).toBe('');
  });

  it('warns that the copy replaces data when the target already holds Ninlil data', async () => {
    const fixture = await createComponent({
      configureApi: () => {
        apiSpy.apiStorageSwitchPost.and.returnValue(
          of({ targetEngine: StorageEngineEnum.Sqlite, targetHasData: true, note: 'Restart.' }),
        );
      },
    });
    expect(fixture.nativeElement.querySelector('.target-has-data')).toBeNull();

    fixture.componentInstance.form.patchValue({ sqliteFilePath: '/var/lib/ninlil/new.sqlite3' });
    await fixture.componentInstance.switchEngine();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.target-has-data')?.textContent).toContain(
      'replaces it',
    );
  });

  it('shows no data warning when the target is empty', async () => {
    const fixture = await createComponent({
      configureApi: () => {
        apiSpy.apiStorageSwitchPost.and.returnValue(
          of({ targetEngine: StorageEngineEnum.Sqlite, targetHasData: false, note: 'Restart.' }),
        );
      },
    });

    fixture.componentInstance.form.patchValue({ sqliteFilePath: '/var/lib/ninlil/new.sqlite3' });
    await fixture.componentInstance.switchEngine();
    fixture.detectChanges();

    expect(fixture.nativeElement.querySelector('.target-has-data')).toBeNull();
  });

  describe('pending switch', () => {
    const pendingStatus: StorageStatus = {
      engine: StorageEngineEnum.Sqlite,
      switchSupportedEngines: allEngines,
      pendingSwitch: {
        targetEngine: StorageEngineEnum.PostgreSql,
        requestedAtUtc: '2026-10-09T10:00:00Z',
        requestedBy: 'admin',
        appliesAutomatically: false,
      },
    };

    it('is hidden when nothing is pending', async () => {
      const fixture = await createComponent();

      expect(fixture.nativeElement.querySelector('.pending-switch')).toBeNull();
    });

    it('shows the target, requester and the environment variable case', async () => {
      const fixture = await createComponent({ status: pendingStatus });
      const card: HTMLElement = fixture.nativeElement.querySelector('.pending-switch');

      expect(card.textContent).toContain('PostgreSql');
      expect(card.textContent).toContain('admin');
      expect(card.textContent).toContain('environment variables');
      expect(card.querySelector('.cancel-switch')).not.toBeNull();
    });

    it('hides Cancel without storage:manage', async () => {
      const fixture = await createComponent({ status: pendingStatus, canManage: false });

      expect(fixture.nativeElement.querySelector('.pending-switch')).not.toBeNull();
      expect(fixture.nativeElement.querySelector('.cancel-switch')).toBeNull();
    });

    it('Cancel sends DELETE and reloads the status', async () => {
      const fixture = await createComponent({
        status: pendingStatus,
        configureApi: () => apiSpy.apiStorageSwitchDelete.and.returnValue(of(null)),
      });
      apiSpy.apiStorageGet.and.returnValue(
        of({ engine: StorageEngineEnum.Sqlite, switchSupportedEngines: allEngines }),
      );

      fixture.nativeElement.querySelector('.cancel-switch').click();
      await flushMicrotasks();
      fixture.detectChanges();

      expect(apiSpy.apiStorageSwitchDelete).toHaveBeenCalled();
      expect(fixture.nativeElement.querySelector('.pending-switch')).toBeNull();
    });
  });

  describe('last outcome', () => {
    it('shows rows copied after a success', async () => {
      const fixture = await createComponent({
        status: {
          engine: StorageEngineEnum.PostgreSql,
          switchSupportedEngines: allEngines,
          lastSwitchOutcome: {
            succeeded: true,
            fromEngine: StorageEngineEnum.Sqlite,
            toEngine: StorageEngineEnum.PostgreSql,
            atUtc: '2026-10-09T10:00:00Z',
            rowsCopied: 1234,
            message: 'Copied.',
          },
        },
      });
      const card: HTMLElement = fixture.nativeElement.querySelector('.switch-outcome');

      expect(card.textContent).toContain('succeeded');
      expect(card.textContent).toContain('1234');
    });

    it('shows the failure message', async () => {
      const fixture = await createComponent({
        status: {
          engine: StorageEngineEnum.Sqlite,
          switchSupportedEngines: allEngines,
          lastSwitchOutcome: {
            succeeded: false,
            fromEngine: StorageEngineEnum.Sqlite,
            toEngine: StorageEngineEnum.MySql,
            atUtc: '2026-10-09T10:00:00Z',
            rowsCopied: 0,
            message: 'Could not reach the target.',
          },
        },
      });
      const card: HTMLElement = fixture.nativeElement.querySelector('.switch-outcome');

      expect(card.textContent).toContain('failed');
      expect(card.textContent).toContain('Could not reach the target.');
    });
  });

  it('shows an error message when the switch fails', async () => {
    const fixture = await createComponent({
      configureApi: () => {
        apiSpy.apiStorageSwitchPost.and.returnValue(
          throwError(() => new HttpErrorResponse({ status: 400, error: 'unreachable target' })),
        );
      },
    });

    fixture.componentInstance.form.patchValue({
      targetEngine: StorageEngineEnum.Sqlite,
      sqliteFilePath: '/var/lib/ninlil/new.sqlite3',
    });
    await fixture.componentInstance.switchEngine();

    expect(fixture.componentInstance.errorMessage()).toBe('unreachable target');
  });

  it('does not switch when the session lacks storage:manage', async () => {
    const fixture = await createComponent({ canManage: false });

    fixture.componentInstance.form.patchValue({
      targetEngine: StorageEngineEnum.Sqlite,
      sqliteFilePath: '/var/lib/ninlil/new.sqlite3',
    });
    await fixture.componentInstance.switchEngine();

    expect(apiSpy.apiStorageSwitchPost).not.toHaveBeenCalled();
    expect(fixture.componentInstance.canManage()).toBeFalse();
  });
});
