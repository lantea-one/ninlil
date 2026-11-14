# Storage

This page lets you choose the database Ninlil stores its data in, move an installation to another
engine, and add a read replica for a large installation. The README holds the complete reference:
[Storage](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#storage). Every setting
here has a row in
[`ENVIRONMENT.md`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#storage-engine).

## Choose a storage engine

| Engine | `NINLIL_STORAGE_ENGINE` | Needs |
|---|---|---|
| SQLite (default) | `Sqlite` | Nothing. Ninlil creates `ninlil-dns.sqlite3` in the data directory. |
| PostgreSQL | `PostgreSql` | A database and an account |
| MySQL | `MySql` | A database and an account |
| SQL Server | `SqlServer` | A database and an account |
| MongoDB | `MongoDb` | A database and an account. Run it as a replica set. |

Ninlil creates and migrates the tables of the four relational engines at start. MongoDB has no
tables and needs no migration step.

### Before you start

1. Ninlil is stopped, or you start it for the first time with these settings. To move an existing
   installation, follow [Switch to another engine](#switch-to-another-engine) instead.
2. For PostgreSQL, MySQL, SQL Server and MongoDB, create the database and its account first. The
   scripts in
   [`src/script/database/`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/src/script/database/README.md)
   do this. They are written for an administrator who runs them against the server.
3. Keep the connection string secret. It holds the password.

### Steps

1. Set the engine and its connection string in Ninlil's environment
   ([`NINLIL_STORAGE_ENGINE` and `NINLIL_STORAGE_CONNECTION_STRING`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#storage-engine)).
   A PostgreSQL example (`<password>` is the account's password):

    ```bash
    NINLIL_STORAGE_ENGINE=PostgreSql
    NINLIL_STORAGE_CONNECTION_STRING="Host=localhost;Database=ninlil;Username=ninlil;Password=<password>"
    ```

    The same settings in `appsettings.json`:

    ```json
    {
      "Storage": {
        "Engine": "PostgreSql",
        "ConnectionString": "Host=localhost;Database=ninlil;Username=ninlil;Password=<password>"
      }
    }
    ```

    The connection strings of the other engines use their driver's format. The README shows MySQL,
    SQL Server and MongoDB examples under
    [Read replicas and database clusters](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#read-replicas-and-database-clusters).
2. Start Ninlil.

### Check that it worked

The Storage page (**Settings**, **Storage**, `/settings/storage`) names the engine. So does the API
(needs `storage:read`):

```bash
curl -k https://127.0.0.1:18443/api/storage -H "Authorization: Bearer <token>"
```

```text
{"engine":"PostgreSql","pendingSwitch":null,"lastSwitchOutcome":null,"switchSupportedEngines":["Sqlite","PostgreSql","MySql","SqlServer"],"primary":{"hosts":["127.0.0.1:32768"]},"readReplica":null, ...}
```

(your values differ). The log has a line such as
`Storage: PostgreSql, primary 127.0.0.1:32768, no read replica.` It never holds a user name,
password or database name.

## Switch to another engine

You can move an installation between SQLite, PostgreSQL, MySQL and SQL Server. MongoDB cannot be the
source or the target. A switch takes effect at the **next restart**: you ask for it, then restart
Ninlil, and the copy runs at that start before anything else uses the database.

The copy moves what an administrator configured: zones, records, users, roles, filtering, clients,
DHCP scopes, reservations and leases. It leaves out the query log, the operational log and
metrics, the zone change journal, webhook delivery attempts and the cluster sync tombstones. The
key ring, the certificates and the configuration file stay where they are. Ninlil does not delete the
old database.

### Before you start

1. Create the target database and its account (see above). The target must be empty, or hold Ninlil
   data you accept losing: the copy replaces it.
2. Remove any read replica and engine-specific variable (`NINLIL_STORAGE_READ_REPLICA_*`,
   `NINLIL_STORAGE_POSTGRESQL_*`, `..._MYSQL_*`, `..._SQLSERVER_*`, `..._MONGODB_*`). Ninlil refuses
   to schedule a switch while one is set. Set them up again for the new engine afterwards.
3. An account with the `storage:manage` permission (the bootstrap administrator has it) and its
   access token, called `<token>` below.
4. A recent [backup](backup-and-restore.md).

### Steps

1. In the console, open **Settings**, **Storage**, choose the target engine, fill in the connection
   fields and choose **Schedule switch**. Through the API (`encryptionMode` is the name of one of
   the engine's driver values; the error for a wrong one lists them):

    ```bash
    curl -k https://127.0.0.1:18443/api/storage/switch -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"targetEngine":"PostgreSql","postgreSql":{"host":"<host>","port":5432,"databaseName":"ninlil","userName":"ninlil","password":"<password>","encryptionMode":"Require"}}'
    ```

    ```text
    {"targetEngine":"PostgreSql","requestedAtUtc":"2026-11-14T02:58:26.6999478+00:00","appliesAutomatically":true,"targetHasData":false,"note":"Restart Ninlil to switch. The data is copied at that start."}
    ```

    Ninlil connects to the target at once and refuses the request when it cannot reach it, when a
    newer Ninlil version already upgraded it, or when the target is MongoDB. Nothing is copied yet.
    `DELETE /api/storage/switch` (the console's **Cancel**) removes the request. It lives in
    `storage-switch.json` in the data directory, so keep that directory across the restart.
2. Read the `note`:
    - *Restart Ninlil to switch.* (`appliesAutomatically` is `true`.) The configuration file holds the
      storage settings. Do nothing else.
    - *Set `NINLIL_STORAGE_ENGINE` and `NINLIL_STORAGE_CONNECTION_STRING` ...* Something other than the
      file, usually environment variables (Docker, `/etc/ninlil/ninlil.env`, a Windows service),
      decides the storage, and Ninlil never overrides it. Set `NINLIL_STORAGE_ENGINE` to the target
      engine and `NINLIL_STORAGE_CONNECTION_STRING` to the target's connection string.
3. Restart Ninlil (`sudo systemctl restart ninlil`, `docker restart ninlil` or
   `Restart-Service Ninlil`). At that start Ninlil migrates the target, copies the data and, in the
   first case, writes the new engine and connection string into `appsettings.json`.

### Check that it worked

Read the status again. The outcome of the latest start is in `lastSwitchOutcome`:

```text
{"engine":"PostgreSql","pendingSwitch":null,"lastSwitchOutcome":{"succeeded":true,"fromEngine":"Sqlite","toEngine":"PostgreSql","atUtc":"...","rowsCopied":13,"message":"Copied 13 rows from Sqlite to PostgreSql."}, ...}
```

The log has `Switched the storage from Sqlite to PostgreSql, copying 13 rows.` (your row count
differs). Sign in again with an account that existed before the switch.

### When something goes wrong

- *The request answers `400` with `The target database could not be reached: ...`.* Check the host,
  port, account and `encryptionMode`.
- *The request answers `400` with `Storage switching supports SQLite, PostgreSQL, MySQL, and SQL Server only ...`.*
  The target is MongoDB. Choose another engine.
- *The request answers `400` with `A storage switch is refused while read replica or cluster settings are configured ...`.*
  Remove those settings, switch, then configure them again.
- *The configuration file decided the storage and the copy failed.* Ninlil keeps running on the old
  database, deletes the request and shows the failure on the Storage page. Fix the cause and
  schedule the switch again.
- *Your environment variables already point at the target and the copy failed.* Ninlil cannot run on
  an empty target, so it refuses to start with [exit code 78](../../reference/exit-codes.md). Point
  the variables back at the old database, or fix the cause, and start again. The request stays until
  you cancel it or the copy succeeds.

## Add a read replica

A **read replica** is a second database server that holds a copy of the primary and answers some of
Ninlil's reads. Read replicas work with PostgreSQL, MySQL, SQL Server and MongoDB, not with SQLite.
Nothing changes until you configure one. Ninlil does not set up replication: you create the replica
with your database's own tools, and Ninlil only needs to know where to read.

The admin console's list, detail and statistics pages read from an eligible replica, and so do the
in-memory caches when a refresh is only due to age. Everything else uses the primary: startup,
migrations, snapshots, writes, sign-in, DHCP packet handling, backup export and the DNS server's zone
and key lookups. The full table is in the
[README](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#read-replicas-and-database-clusters).

### Before you start

1. A primary that Ninlil already uses, on PostgreSQL, MySQL, SQL Server or MongoDB.
2. A replica built with the database's own replication, holding the same database as the primary
   (name, tables and accounts) and read-only.
3. The Ninlil account on the replica. PostgreSQL, MySQL and MongoDB copy accounts with the data. A
   SQL Server login belongs to the server, so create it on every replica instance with the same SID
   as on the primary.
4. The extra grant that lets Ninlil measure the lag (below).

### Steps

1. Grant the lag-measuring right for your engine:

    | Engine | Run once as an administrator |
    |---|---|
    | PostgreSQL | Nothing. |
    | MySQL | `GRANT REPLICATION CLIENT ON *.* TO 'ninlil'@'%';` on the primary |
    | SQL Server | `GRANT VIEW SERVER STATE TO [ninlil];` on every instance that can become the primary |
    | MongoDB | `db.getSiblingDB("ninlil").grantRolesToUser("ninlil", [{ role: "clusterMonitor", db: "admin" }])` |

    Without the grant, the lag is unknown and Ninlil never uses the replica.
2. Set the replica's connection string
   ([`NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#storage-engine)).
   For MongoDB, set `NINLIL_STORAGE_MONGODB_READ_PREFERENCE` instead. PostgreSQL, with a primary and a
   standby:

    ```bash
    NINLIL_STORAGE_ENGINE=PostgreSql
    NINLIL_STORAGE_CONNECTION_STRING="Host=db1.example;Database=ninlil;Username=ninlil;Password=<password>"
    NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING="Host=standby1.example;Database=ninlil;Username=ninlil;Password=<password>"
    ```

    The README gives the strings for MySQL (through a router), SQL Server (an Always On listener) and
    MongoDB (a replica set), and the settings that each engine adds.
3. Restart Ninlil. A setting that contradicts another setting stops the start with exit code 78 and a
   message that names both.

### Check that it worked

1. Find this line in the log:

    ```text
    Storage: PostgreSql, primary db1.example:5432, read replica standby1.example:5432; a user who saved reads the primary for 10 s, a replica more than 5 s behind gets no reads.
    ```

2. Read the status. The `readReplica` object shows `"eligible":true` once the first check passes
   (within 5 seconds by default), with its lag and how many reads went to each side:

    ```bash
    curl -k https://127.0.0.1:18443/api/storage -H "Authorization: Bearer <token>"
    ```

    ```text
    {"engine":"PostgreSql", ... ,"readReplica":{"configured":true,"eligible":true,"lagSeconds":0,"schemaCurrent":true,"lastCheckedAtUtc":"...","reason":"","readsToReplica":1,"readsToPrimary":18,"hosts":["standby1.example:5432"],"readPreference":null}}
    ```

    The console's **Primary and read replica** card on **Settings**, **Storage** shows the same.

### How a replica is used

- A replica is **eligible** when its lag is known and at most
  `NINLIL_STORAGE_READ_REPLICA_MAX_LAG_SECONDS` (5), and it has the same layout as the primary.
  Otherwise every read goes to the primary, and the `storage` component of `GET /status` and the
  Storage page show *Degraded*. Everything still works.
- After you save something, your own reads go to the primary for
  `NINLIL_STORAGE_READ_REPLICA_STICKY_AFTER_WRITE_SECONDS` (10), so you see your own change.
- If the read string names the same hosts and database as the primary, Ninlil starts and logs
  `Storage:ReadReplica:ConnectionString names the same hosts and database as Storage:ConnectionString; reads gain nothing from the split.`
- A change made on another cluster node reaches this node's DNS answers after at most the cache
  refresh interval (60 seconds) plus the lag limit.

## Behaviour worth knowing

- **SQLite runs in WAL mode.** While Ninlil runs, `ninlil-dns.sqlite3-wal` and `-shm` sit next to
  `ninlil-dns.sqlite3`. Do not copy only the main file of a running installation. Use
  [Backup and restore](backup-and-restore.md), or stop Ninlil and copy all three files.
- **MongoDB needs a replica set for all-or-nothing saves.** On a standalone server Ninlil logs a warning
  at every start. A single-node replica set is enough. The README lists the four steps under
  [MongoDB](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#mongodb), with the
  open-file limit to raise.
- **MySQL accounts from the script accept any host.** `src/script/database/mysql/init.sql` creates
  `'ninlil'@'%'`. Narrow it once Ninlil runs
  ([steps](https://git.lantea.one/lantea-one/ninlil/src/branch/main/src/script/database/README.md#narrow-the-mysql-account-to-the-application-host)).
- **Snapshots and upgrades.** A relational database is migrated at start, after a snapshot. See
  [Upgrades](../../upgrades.md).

## Next steps

- [Upgrades](../../upgrades.md): migration snapshots and exit code 78.
- [Backup and restore](backup-and-restore.md)
- [Clustering, events and webhooks](clustering.md)
