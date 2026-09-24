import {
  RelationalStorageConnectionFields,
  StorageEngineEnum,
  StorageEngineSwitchApiRequest,
} from '../../../api-client';

/**
 * This interface carries every field {@link StorageSettingsComponent}'s own reactive form exposes
 * -- one section per storage engine, of which only the section matching `targetEngine` is ever
 * actually read by {@link buildStorageSwitchRequest}. Kept as its own named shape (rather than
 * inlining `form.getRawValue()`'s own inferred type at every call site) so
 * {@link buildStorageSwitchRequest} stays independently unit-testable with plain object literals,
 * with no `ReactiveFormsModule` involved at all.
 */
export interface StorageSettingsFormValue {
  targetEngine: StorageEngineEnum;
  sqliteFilePath: string;
  postgreSqlHost: string;
  postgreSqlPort: number;
  postgreSqlDatabaseName: string;
  postgreSqlUserName: string;
  postgreSqlPassword: string;
  postgreSqlEncryptionMode: string;
  mySqlHost: string;
  mySqlPort: number;
  mySqlDatabaseName: string;
  mySqlUserName: string;
  mySqlPassword: string;
  mySqlEncryptionMode: string;
  sqlServerHost: string;
  sqlServerPort: number;
  sqlServerDatabaseName: string;
  sqlServerUserName: string;
  sqlServerPassword: string;
  sqlServerEncryptionMode: string;
}

/**
 * This constant lists the real, valid `encryptionMode` values for each server-based relational
 * engine, sourced directly from the exact same real values
 * `StorageConnectionStringAssembler` (backend-side) validates against -- Npgsql's own
 * `SslMode` enum, `MySqlConnector`'s own `MySqlSslMode` enum, and
 * `Microsoft.Data.SqlClient`'s own `SqlConnectionEncryptOption` values, respectively. Kept as a
 * literal, hand-copied list rather than fetched from the backend at runtime, the same way
 * {@link StorageEngineEnum}'s own values are already hand-copied into this generated API client.
 */
export const ENCRYPTION_MODE_OPTIONS_BY_ENGINE: Readonly<
  Record<'postgreSql' | 'mySql' | 'sqlServer', readonly string[]>
> = {
  postgreSql: ['Disable', 'Allow', 'Prefer', 'Require', 'VerifyCA', 'VerifyFull'],
  mySql: ['None', 'Disabled', 'Preferred', 'Required', 'VerifyCA', 'VerifyFull'],
  sqlServer: ['Optional', 'Mandatory', 'Strict'],
};

/**
 * This function builds the real, structured `POST /api/storage/switch` request body from this
 * form's own flat set of per-engine fields, populating only the one section matching
 * `value.targetEngine` -- exactly mirroring `StorageConnectionStringAssembler`'s own backend-side
 * expectation that only the matching section is ever read. A target engine with no structured
 * section defined here at all (in practice, only `StorageEngineEnum.MongoDb` -- never offered as
 * a selectable option in this form's own dropdown, itself populated from the backend's own
 * `liveSwitchSupportedEngines`) falls through to a request naming only `targetEngine`, which
 * `IStorageEngineSwitchService` itself already rejects with its own dedicated, clear message.
 */
export function buildStorageSwitchRequest(
  value: StorageSettingsFormValue,
): StorageEngineSwitchApiRequest {
  switch (value.targetEngine) {
    case StorageEngineEnum.Sqlite:
      return {
        targetEngine: value.targetEngine,
        sqlite: { filePath: value.sqliteFilePath },
      };
    case StorageEngineEnum.PostgreSql: {
      const postgreSql: RelationalStorageConnectionFields = {
        host: value.postgreSqlHost,
        port: value.postgreSqlPort,
        databaseName: value.postgreSqlDatabaseName,
        userName: value.postgreSqlUserName,
        password: value.postgreSqlPassword,
        encryptionMode: value.postgreSqlEncryptionMode,
      };
      return { targetEngine: value.targetEngine, postgreSql };
    }
    case StorageEngineEnum.MySql: {
      const mySql: RelationalStorageConnectionFields = {
        host: value.mySqlHost,
        port: value.mySqlPort,
        databaseName: value.mySqlDatabaseName,
        userName: value.mySqlUserName,
        password: value.mySqlPassword,
        encryptionMode: value.mySqlEncryptionMode,
      };
      return { targetEngine: value.targetEngine, mySql };
    }
    case StorageEngineEnum.SqlServer: {
      const sqlServer: RelationalStorageConnectionFields = {
        host: value.sqlServerHost,
        port: value.sqlServerPort,
        databaseName: value.sqlServerDatabaseName,
        userName: value.sqlServerUserName,
        password: value.sqlServerPassword,
        encryptionMode: value.sqlServerEncryptionMode,
      };
      return { targetEngine: value.targetEngine, sqlServer };
    }
    default:
      return { targetEngine: value.targetEngine };
  }
}
