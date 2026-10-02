import {
  RelationalStorageConnectionFields,
  StorageEngineEnum,
  StorageEngineSwitchApiRequest,
} from '../../../api-client';

/**
 * This interface is the value of the `StorageSettingsComponent` form: one section per engine, of
 * which {@link buildStorageSwitchRequest} reads only the one matching `targetEngine`. It is a named
 * type so that function can be tested with plain objects.
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
 * This constant lists the valid `encryptionMode` values per server engine, the values
 * `StorageConnectionStringAssembler` accepts: Npgsql's `SslMode`, MySqlConnector's `MySqlSslMode`
 * and Microsoft.Data.SqlClient's `SqlConnectionEncryptOption`. The API does not expose them, so
 * they are copied here and must follow the backend.
 */
export const ENCRYPTION_MODE_OPTIONS_BY_ENGINE: Readonly<
  Record<'postgreSql' | 'mySql' | 'sqlServer', readonly string[]>
> = {
  postgreSql: ['Disable', 'Allow', 'Prefer', 'Require', 'VerifyCA', 'VerifyFull'],
  mySql: ['None', 'Disabled', 'Preferred', 'Required', 'VerifyCA', 'VerifyFull'],
  sqlServer: ['Optional', 'Mandatory', 'Strict'],
};

/**
 * This function builds the `POST /api/storage/switch` body from the form value, filling only the
 * section for `value.targetEngine`, the one `StorageConnectionStringAssembler` reads. An engine with
 * no section here (only MongoDB, which the form never offers) gets a body with just
 * `targetEngine`, which `IStorageEngineSwitchService` rejects with its own message.
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
