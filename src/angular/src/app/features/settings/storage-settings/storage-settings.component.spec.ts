import { HttpErrorResponse } from '@angular/common/http';
import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { StorageEngineEnum, StorageService } from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { NotificationService } from '../../../shared/notification/notification.service';
import { StorageSettingsComponent } from './storage-settings.component';

function flushMicrotasks(): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

/**
 * These tests cover the Storage page: loading the status, per-engine validation, the switch request
 * and its result, permissions and errors. TestBed with a spy-backed `StorageService`,
 * `NotificationService` and a stubbed `AuthService`.
 */
describe('StorageSettingsComponent', () => {
  let apiSpy: { apiStorageGet: jasmine.Spy; apiStorageSwitchPost: jasmine.Spy };
  let notificationSpy: { success: jasmine.Spy; error: jasmine.Spy };
  let authSpy: { hasPermission: jasmine.Spy };

  async function createComponent(options?: { configureApi?: () => void; canManage?: boolean }) {
    apiSpy = jasmine.createSpyObj('StorageService', ['apiStorageGet', 'apiStorageSwitchPost']);
    notificationSpy = jasmine.createSpyObj('NotificationService', ['success', 'error']);
    authSpy = jasmine.createSpyObj('AuthService', ['hasPermission']);
    authSpy.hasPermission.and.returnValue(options?.canManage ?? true);

    apiSpy.apiStorageGet.and.returnValue(
      of({
        engine: StorageEngineEnum.Sqlite,
        switchInProgress: false,
        liveSwitchSupportedEngines: [
          StorageEngineEnum.Sqlite,
          StorageEngineEnum.PostgreSql,
          StorageEngineEnum.MySql,
          StorageEngineEnum.SqlServer,
        ],
      }),
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

  it('switchEngine() posts only the structured section matching the target engine and surfaces a success notification', async () => {
    const fixture = await createComponent({
      configureApi: () => {
        apiSpy.apiStorageSwitchPost.and.returnValue(
          of({
            previousEngine: StorageEngineEnum.Sqlite,
            newEngine: StorageEngineEnum.PostgreSql,
            totalEntitiesRestored: 5,
            entityCounts: { Zones: 1 },
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
    expect(notificationSpy.success).toHaveBeenCalledWith(
      jasmine.stringContaining('no restart required'),
    );
    expect(fixture.componentInstance.lastResult()?.newEngine).toBe(StorageEngineEnum.PostgreSql);
    // The password is cleared after a successful switch; it is never returned.
    expect(fixture.componentInstance.form.controls.postgreSqlPassword.value).toBe('');
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
