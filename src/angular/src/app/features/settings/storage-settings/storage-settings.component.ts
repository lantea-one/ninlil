import { KeyValuePipe } from '@angular/common';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import {
  NonNullableFormBuilder,
  ReactiveFormsModule,
  ValidatorFn,
  Validators,
} from '@angular/forms';
import {
  AlertComponent,
  BadgeComponent,
  ButtonDirective,
  CardBodyComponent,
  CardComponent,
  CardFooterComponent,
  CardHeaderComponent,
  ColComponent,
  FormControlDirective,
  FormDirective,
  FormLabelDirective,
  FormSelectDirective,
  RowComponent,
  SpinnerComponent,
} from '@coreui/angular';
import { firstValueFrom } from 'rxjs';
import {
  StorageEngineEnum,
  StorageEngineSwitchResult,
  StorageService,
  StorageStatus,
} from '../../../api-client';
import { AuthService } from '../../../core/auth/auth.service';
import { extractErrorMessage } from '../../../shared/http-error/http-error.util';
import { NotificationService } from '../../../shared/notification/notification.service';
import {
  ENCRYPTION_MODE_OPTIONS_BY_ENGINE,
  StorageSettingsFormValue,
  buildStorageSwitchRequest,
} from './storage-settings.model';

/**
 * This map lists the connection-field controls of each storage engine (not `targetEngine`).
 * `applyValidatorsForEngine` makes only the selected engine's fields required, so a hidden section
 * never blocks the switch. `StorageEngineEnum.MongoDb` has no entry because it is never offered:
 * the engine select lists only the backend's `liveSwitchSupportedEngines`.
 */
const CONTROL_NAMES_BY_ENGINE: Readonly<Record<string, readonly string[]>> = {
  [StorageEngineEnum.Sqlite]: ['sqliteFilePath'],
  [StorageEngineEnum.PostgreSql]: [
    'postgreSqlHost',
    'postgreSqlPort',
    'postgreSqlDatabaseName',
    'postgreSqlUserName',
    'postgreSqlEncryptionMode',
  ],
  [StorageEngineEnum.MySql]: [
    'mySqlHost',
    'mySqlPort',
    'mySqlDatabaseName',
    'mySqlUserName',
    'mySqlEncryptionMode',
  ],
  [StorageEngineEnum.SqlServer]: [
    'sqlServerHost',
    'sqlServerPort',
    'sqlServerDatabaseName',
    'sqlServerUserName',
    'sqlServerEncryptionMode',
  ],
};

/** This constant lists every engine's connection-field controls, so all can be cleared before the selected engine's are set. */
const ALL_ENGINE_CONTROL_NAMES: readonly string[] = Object.values(CONTROL_NAMES_BY_ENGINE).flat();

/** This constant names each engine's port control, which also gets a 1-65535 range check. */
const PORT_CONTROL_NAMES: ReadonlySet<string> = new Set([
  'postgreSqlPort',
  'mySqlPort',
  'sqlServerPort',
]);

/**
 * This component is the "Storage" settings page (`/settings/storage`). It shows the active storage
 * engine from `GET /api/storage` (`storage:read`) and, with `storage:manage`, switches the running
 * server to another engine through `POST /api/storage/switch`: the backend backs up the current
 * engine, prepares and migrates the target, moves the process onto it and restores the backup into
 * it, without a restart (see `StorageEngineSwitchService`). The form asks for structured
 * connection fields per engine (a file path for SQLite; host, port, database, user, password and
 * encryption mode for the others) rather than a connection string.
 *
 * <p>Only the selected engine's fields are required (see {@link CONTROL_NAMES_BY_ENGINE}), matching
 * the checks in `StorageConnectionStringAssembler`: host, database name, user name and encryption
 * mode required, port 1 to 65535.
 *
 * <p>Only the engines in {@link StorageStatus.liveSwitchSupportedEngines} are offered. MongoDB is not,
 * since it uses separate repositories this switch cannot move to or from; it needs a configuration
 * change and a restart.
 */
@Component({
  selector: 'app-storage-settings',
  imports: [
    ReactiveFormsModule,
    KeyValuePipe,
    AlertComponent,
    BadgeComponent,
    ButtonDirective,
    CardBodyComponent,
    CardComponent,
    CardFooterComponent,
    CardHeaderComponent,
    ColComponent,
    FormControlDirective,
    FormDirective,
    FormLabelDirective,
    FormSelectDirective,
    RowComponent,
    SpinnerComponent,
  ],
  templateUrl: './storage-settings.component.html',
  styleUrl: './storage-settings.component.scss',
})
export class StorageSettingsComponent implements OnInit {
  private readonly storageApi = inject(StorageService);
  private readonly authService = inject(AuthService);
  private readonly notification = inject(NotificationService);
  private readonly formBuilder = inject(NonNullableFormBuilder);

  readonly loading = signal(true);
  readonly switching = signal(false);
  readonly errorMessage = signal<string | null>(null);
  readonly status = signal<StorageStatus | null>(null);
  readonly lastResult = signal<StorageEngineSwitchResult | null>(null);

  /** Whether the user may switch engines; without `storage:manage` the page only shows the status. */
  readonly canManage = computed(() => this.authService.hasPermission('storage:manage'));

  /** The valid `encryptionMode` values per engine (see {@link ENCRYPTION_MODE_OPTIONS_BY_ENGINE}). */
  readonly encryptionModeOptions = ENCRYPTION_MODE_OPTIONS_BY_ENGINE;

  readonly form = this.formBuilder.group({
    targetEngine: this.formBuilder.control<StorageEngineEnum>(StorageEngineEnum.Sqlite, [
      Validators.required,
    ]),
    sqliteFilePath: [''],
    postgreSqlHost: [''],
    postgreSqlPort: [5432],
    postgreSqlDatabaseName: [''],
    postgreSqlUserName: [''],
    postgreSqlPassword: [''],
    postgreSqlEncryptionMode: ['Require'],
    mySqlHost: [''],
    mySqlPort: [3306],
    mySqlDatabaseName: [''],
    mySqlUserName: [''],
    mySqlPassword: [''],
    mySqlEncryptionMode: ['Required'],
    sqlServerHost: [''],
    sqlServerPort: [1433],
    sqlServerDatabaseName: [''],
    sqlServerUserName: [''],
    sqlServerPassword: [''],
    sqlServerEncryptionMode: ['Mandatory'],
  });

  ngOnInit(): void {
    this.applyValidatorsForEngine(this.form.controls.targetEngine.value);
    this.form.controls.targetEngine.valueChanges.subscribe((engine) =>
      this.applyValidatorsForEngine(engine),
    );

    void this.loadStatus();
  }

  /** This method switches the storage engine, sending only the selected engine's connection fields. */
  async switchEngine(): Promise<void> {
    if (this.form.invalid || this.switching() || !this.canManage()) return;

    this.switching.set(true);
    this.errorMessage.set(null);
    this.lastResult.set(null);

    const value: StorageSettingsFormValue = this.form.getRawValue();

    try {
      const result = await firstValueFrom(
        this.storageApi.apiStorageSwitchPost({
          storageEngineSwitchApiRequest: buildStorageSwitchRequest(value),
        }),
      );
      this.lastResult.set(result);
      this.notification.success(
        `Switched from ${result.previousEngine} to ${result.newEngine}, restoring ${result.totalEntitiesRestored} rows -- no restart required.`,
      );
      // The switch response has no password, and a later switch needs it typed again.
      this.form.patchValue({
        postgreSqlPassword: '',
        mySqlPassword: '',
        sqlServerPassword: '',
      });
      await this.loadStatus();
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not switch the storage engine.'));
    } finally {
      this.switching.set(false);
    }
  }

  /**
   * This method clears the validators of every engine's connection fields, then makes the given
   * engine's fields required (ports also 1 to 65535), so only the visible section is validated.
   */
  private applyValidatorsForEngine(engine: StorageEngineEnum): void {
    for (const controlName of ALL_ENGINE_CONTROL_NAMES) {
      const control = this.form.get(controlName);
      control?.clearValidators();
      control?.updateValueAndValidity({ emitEvent: false });
    }

    const controlNames = CONTROL_NAMES_BY_ENGINE[engine] ?? [];
    for (const controlName of controlNames) {
      const control = this.form.get(controlName);
      if (!control) continue;

      const validators: ValidatorFn[] = [Validators.required];
      if (PORT_CONTROL_NAMES.has(controlName))
        validators.push(Validators.min(1), Validators.max(65535));

      control.setValidators(validators);
      control.updateValueAndValidity({ emitEvent: false });
    }
  }

  private async loadStatus(): Promise<void> {
    this.loading.set(true);
    this.errorMessage.set(null);

    try {
      const status = await firstValueFrom(this.storageApi.apiStorageGet());
      this.status.set(status);
    } catch (error) {
      this.errorMessage.set(extractErrorMessage(error, 'Could not load storage status.'));
    } finally {
      this.loading.set(false);
    }
  }
}
