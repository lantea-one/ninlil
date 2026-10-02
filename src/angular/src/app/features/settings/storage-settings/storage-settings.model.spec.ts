import { StorageEngineEnum } from '../../../api-client';
import { buildStorageSwitchRequest, StorageSettingsFormValue } from './storage-settings.model';

function baseFormValue(
  overrides: Partial<StorageSettingsFormValue> = {},
): StorageSettingsFormValue {
  return {
    targetEngine: StorageEngineEnum.Sqlite,
    sqliteFilePath: '',
    postgreSqlHost: '',
    postgreSqlPort: 5432,
    postgreSqlDatabaseName: '',
    postgreSqlUserName: '',
    postgreSqlPassword: '',
    postgreSqlEncryptionMode: 'Require',
    mySqlHost: '',
    mySqlPort: 3306,
    mySqlDatabaseName: '',
    mySqlUserName: '',
    mySqlPassword: '',
    mySqlEncryptionMode: 'Required',
    sqlServerHost: '',
    sqlServerPort: 1433,
    sqlServerDatabaseName: '',
    sqlServerUserName: '',
    sqlServerPassword: '',
    sqlServerEncryptionMode: 'Mandatory',
    ...overrides,
  };
}

/**
 * These tests cover buildStorageSwitchRequest for each engine. Plain unit tests.
 */
describe('storage-settings.model', () => {
  it('builds a SQLite request with only the sqlite section populated', () => {
    const request = buildStorageSwitchRequest(
      baseFormValue({
        targetEngine: StorageEngineEnum.Sqlite,
        sqliteFilePath: '/var/lib/ninlil/ninlil.sqlite3',
      }),
    );

    expect(request).toEqual({
      targetEngine: StorageEngineEnum.Sqlite,
      sqlite: { filePath: '/var/lib/ninlil/ninlil.sqlite3' },
    });
  });

  it('builds a PostgreSQL request with only the postgreSql section populated', () => {
    const request = buildStorageSwitchRequest(
      baseFormValue({
        targetEngine: StorageEngineEnum.PostgreSql,
        postgreSqlHost: 'db.example.internal',
        postgreSqlPort: 5432,
        postgreSqlDatabaseName: 'ninlil',
        postgreSqlUserName: 'ninlil_app',
        postgreSqlPassword: 'correct-horse-battery-staple',
        postgreSqlEncryptionMode: 'Require',
      }),
    );

    expect(request).toEqual({
      targetEngine: StorageEngineEnum.PostgreSql,
      postgreSql: {
        host: 'db.example.internal',
        port: 5432,
        databaseName: 'ninlil',
        userName: 'ninlil_app',
        password: 'correct-horse-battery-staple',
        encryptionMode: 'Require',
      },
    });
    expect('sqlite' in request).toBeFalse();
    expect('mySql' in request).toBeFalse();
    expect('sqlServer' in request).toBeFalse();
  });

  it('builds a MySQL request with only the mySql section populated', () => {
    const request = buildStorageSwitchRequest(
      baseFormValue({
        targetEngine: StorageEngineEnum.MySql,
        mySqlHost: 'db.example.internal',
        mySqlPort: 3306,
        mySqlDatabaseName: 'ninlil',
        mySqlUserName: 'ninlil_app',
        mySqlPassword: 'correct-horse-battery-staple',
        mySqlEncryptionMode: 'Required',
      }),
    );

    expect(request).toEqual({
      targetEngine: StorageEngineEnum.MySql,
      mySql: {
        host: 'db.example.internal',
        port: 3306,
        databaseName: 'ninlil',
        userName: 'ninlil_app',
        password: 'correct-horse-battery-staple',
        encryptionMode: 'Required',
      },
    });
  });

  it('builds a SQL Server request with only the sqlServer section populated', () => {
    const request = buildStorageSwitchRequest(
      baseFormValue({
        targetEngine: StorageEngineEnum.SqlServer,
        sqlServerHost: 'db.example.internal',
        sqlServerPort: 1433,
        sqlServerDatabaseName: 'ninlil',
        sqlServerUserName: 'ninlil_app',
        sqlServerPassword: 'correct-horse-battery-staple',
        sqlServerEncryptionMode: 'Mandatory',
      }),
    );

    expect(request).toEqual({
      targetEngine: StorageEngineEnum.SqlServer,
      sqlServer: {
        host: 'db.example.internal',
        port: 1433,
        databaseName: 'ninlil',
        userName: 'ninlil_app',
        password: 'correct-horse-battery-staple',
        encryptionMode: 'Mandatory',
      },
    });
  });

  it('builds a bare request with no structured section for MongoDB', () => {
    const request = buildStorageSwitchRequest(
      baseFormValue({ targetEngine: StorageEngineEnum.MongoDb }),
    );

    expect(request).toEqual({ targetEngine: StorageEngineEnum.MongoDb });
  });
});
