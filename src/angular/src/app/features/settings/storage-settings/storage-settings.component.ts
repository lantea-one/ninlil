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
 * This map lists every reactive form control name that belongs to a given storage engine's own
 * structured connection fields (never the always-present `targetEngine` control itself), used by
 * {@link StorageSettingsComponent.applyValidatorsForEngine} to attach real, required-field
 * validators only to whichever engine's section is currently selected -- clearing every other
 * engine's own controls back to unvalidated, so this form never blocks submission over a section
 * the operator was never shown. `StorageEngineEnum.MongoDb` deliberately has no entry: it is never
 * offered as a selectable option in this form's own dropdown, itself populated only from the
 * backend's own `liveSwitchSupportedEngines`.
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

/** This constant lists every structured-field control name across every engine, used to clear stale validators before re-applying the currently-selected engine's own set. */
const ALL_ENGINE_CONTROL_NAMES: readonly string[] = Object.values(CONTROL_NAMES_BY_ENGINE).flat();

/** This constant names the one `port` control within each engine's own control-name set, so its validators can additionally carry the 1-65535 range check every other required field doesn't need. */
const PORT_CONTROL_NAMES: ReadonlySet<string> = new Set([
  'postgreSqlPort',
  'mySqlPort',
  'sqlServerPort',
]);

/**
 * This component is this host's own storage-engine migration wizard, presenting real, structured,
 * per-engine connection fields (a file path for SQLite; host/port/database name/username/password/
 * encryption mode for the other three) rather than a single raw free-text connection string --
 * mirroring `EventBrokerSettingsComponent`'s own established
 * `@if (form.controls.transport.value === '...')` pattern, substituted here for the selected
 * storage engine. It reports this host's own currently active storage engine
 * (`GET /api/storage`) and drives a real, in-process switch onto a different one
 * (`POST /api/storage/switch`) -- backing up the current engine, provisioning and migrating the
 * target, flipping this already-running process onto it, and restoring the backup into it, with
 * no restart at any point (see `StorageEngineSwitchService`'s own doc comment, backend-side, for
 * exactly how).
 *
 * <p>Required-field and port-range validators are applied dynamically, matching only the
 * currently-selected engine's own section (see {@link CONTROL_NAMES_BY_ENGINE} and
 * {@link applyValidatorsForEngine}) -- deliberately kept in parity with
 * `StorageConnectionStringAssembler`'s own real server-side validation (host/database name/
 * username/encryption mode required, port 1-65535), matching this project's established
 * validation-rule parity standard across every other settings form.
 *
 * <p>Only the four relational engines this mechanism actually supports are offered as a switch
 * target ({@link StorageStatus.liveSwitchSupportedEngines}, read from the backend rather than
 * hardcoded here) -- MongoDB is deliberately not selectable, since it is served by an entirely
 * separate repository implementation this in-process mechanism cannot switch to or from yet
 * (still requires editing configuration and restarting).
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

  /** Whether the current session may actually trigger a switch -- a read-only session still sees this page's current status. */
  readonly canManage = computed(() => this.authService.hasPermission('storage:manage'));

  /** The real, valid `encryptionMode` values for each server-based relational engine -- see {@link ENCRYPTION_MODE_OPTIONS_BY_ENGINE}'s own doc comment. */
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

  /** This method drives a real, in-process storage-engine switch through the real REST endpoint, sending only the structured section that matches the currently-selected target engine. */
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
      // Never re-populate a password field from the response -- POST /api/storage/switch never
      // returns one, and this form never needs to pre-fill a password for an action that only
      // ever runs once, synchronously, for the duration of a single switch.
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
   * This method attaches real, required-field (and, for a `port` control, 1-65535 range)
   * validators to whichever engine's own structured-field controls
   * {@link CONTROL_NAMES_BY_ENGINE} names for `engine`, after first clearing every engine's own
   * controls back to unvalidated -- so this form's own client-side validation always matches only
   * the section currently shown to the operator, never blocking submission over a section left at
   * its own default, unfilled value.
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
