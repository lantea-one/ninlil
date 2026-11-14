# Upgrades

This page lets you move an installation to a newer Ninlil version, shows what happens to the
database at the next start, and shows how to go back when an upgrade fails or misbehaves. The README
holds the complete reference:
[Upgrades and migration snapshots](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#upgrades-and-migration-snapshots).

A new version can change the database layout. For SQLite, PostgreSQL, MySQL and SQL Server, Ninlil
applies those changes itself at start (these are the **migrations**). Before it does, it takes a
**snapshot** of the data, so a failed or unwanted upgrade can be undone. MongoDB has no migrations
and no snapshots.

## Before you start

1. Know how Ninlil is installed ([Installation](getting-started/installation/index.md)) and where
   its data directory is ([First run](getting-started/first-run.md)).
2. Take a [backup](guides/operations/backup-and-restore.md). On MongoDB this is required, because
   there is no snapshot: also run `mongodump --uri "<connection string>"`.
3. Read the release notes in
   [`CHANGELOG.md`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/CHANGELOG.md).
4. Note the running version. It is `currentVersion` in the answer of this call (it needs the
   `selfupdate:read` permission; `<token>` is an access token and `18443` the HTTPS port from the
   [quick start](getting-started/quick-start.md)):

    ```bash
    curl -k https://127.0.0.1:18443/api/self-update -H "Authorization: Bearer <token>"
    ```

    ```text
    {"configured":false,"currentVersion":"2026.9.24.1", ... }
    ```

    (your version differs). A build from source reports `0.0.0.0`.

5. Free disk space of about the size of the database. A snapshot copies every table, including the
   query log, so it roughly doubles the database until Ninlil prunes it.

## Upgrade each kind of installation

Use the step that matches how you installed Ninlil. Every kind keeps the data directory.

1. **Deb and RPM packages.** Install the newer package the same way, then restart. Your edits to
   `/etc/ninlil/ninlil.env` and everything in `/var/lib/ninlil` stay:

    ```bash
    sudo apt install ./newer.deb
    sudo systemctl restart ninlil
    ```

    On Fedora, RHEL and Rocky Linux, use `sudo dnf install ./newer.rpm`.
2. **Arch Linux.** Install the newer package, as in
   [Arch Linux](getting-started/installation/arch-linux.md), then restart:

    ```bash
    sudo pacman -U <package-address-or-file>
    sudo systemctl restart ninlil
    ```

3. **Docker.** Pull the newer tag, remove the container and start it again with the same volume.
   Keep the `-v ninlil-data:/app/data` mount and your other options
   ([Docker](docker.md)). `<version>` is the release tag:

    ```bash
    docker pull git.lantea.one/lantea-one/ninlil:<version>
    docker rm -f ninlil
    ```

    Then run the `docker run` command from the Docker page with the new tag.
4. **A downloaded binary.** Stop Ninlil, replace the executable with the newer one from
   [Download a binary](getting-started/installation/binary.md) and start it from the same folder, so
   it finds the same `data/` folder.
5. **Windows service.** Stop the service, replace the `.exe` and `msquic.dll` with the files from the
   new zip and start it again ([Windows](getting-started/installation/windows.md)):

    ```powershell
    Stop-Service Ninlil
    Start-Service Ninlil
    ```

    Replace the files between the two commands.

## What happens at every start

1. Ninlil reads which migrations the database has already applied.
2. If the database applied a migration this version does not know, a newer version used it. Ninlil
   refuses to start (exit code 78).
3. If an earlier attempt failed and may have left the database half migrated, Ninlil refuses to
   start until you restore the snapshot of that attempt or set
   [`NINLIL_STORAGE_MIGRATION_SAFETY_RETRY_FAILED_MIGRATION=true`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#storage-engine).
4. If no migration is pending, startup continues.
5. If migrations are pending and the database holds data, Ninlil takes a snapshot and logs a line that
   starts with `Took migration snapshot`. A brand-new database gets no snapshot.
6. Ninlil applies the migrations. On success it deletes the oldest snapshots beyond the number to keep
   (3 by default).

If the snapshot cannot be taken (no disk space, no permission), Ninlil leaves the database unchanged
and refuses to start with a message that says why. Fix the cause, or set
`NINLIL_STORAGE_MIGRATION_SAFETY_TAKE_SNAPSHOT=false` to migrate without a snapshot (Ninlil logs a
warning).

## Check that it worked

1. Read the version again with the call from "Before you start". `currentVersion` is the new one.
2. Check health:

    ```bash
    curl http://127.0.0.1:18080/status
    ```

    ```text
    {"status":"Healthy","components":null}
    ```

3. When migrations were pending, the log shows the snapshot. A snapshot id is the UTC time it was
   taken, as `yyyyMMddHHmmss`:

    ```text
    Took migration snapshot 20261114025629 at /var/lib/ninlil/migration-snapshots/ninlil-dns-20261114025629.sqlite3: 44 tables, 67 rows, 72 ms.
    ```

    (your id, path and counts differ). Read the log with `sudo journalctl -u ninlil -e` (Linux
    packages), `docker logs ninlil` or the terminal output.

### Where a snapshot lives

| Engine | Where | List them |
|---|---|---|
| SQLite | `<data directory>/migration-snapshots/ninlil-dns-<id>.sqlite3` and a `.json` manifest, readable by the owner only | `ls <data directory>/migration-snapshots` |
| PostgreSQL | A schema `ninlil_snapshot_<id>` in the Ninlil database | `psql -d ninlil -c '\dn ninlil_snapshot_*'` |
| SQL Server | A schema `ninlil_snapshot_<id>` in the Ninlil database | `SELECT name FROM sys.schemas WHERE name LIKE 'ninlil_snapshot_%'` |
| MySQL | Tables named `nsnap_<id>__<table>` next to the real tables | `SHOW TABLES LIKE 'nsnap\_%\_\_manifest'` |
| MongoDB | None | |

## Go back to the previous version

A restore drops the Ninlil tables, rebuilds the layout the snapshot had and copies the rows back, so
**everything written after the snapshot is lost**.

1. Stop Ninlil.
2. Find the snapshot id: in the log line `Took migration snapshot` of the upgrade start, in the
   refusal message, or in the listing above. Take the newest one.
3. Install the previous Ninlil version the way you installed the new one. It must be a version that
   has the `NINLIL_STORAGE_MIGRATION_SAFETY_*` settings. For an older one, restore a database backup
   from before the upgrade instead.
4. Start Ninlil with the id in the restore setting, as an environment variable or a command-line
   argument ([Configuration](configuration.md)):

    ```bash
    NINLIL_STORAGE_MIGRATION_SAFETY_RESTORE_SNAPSHOT=20261114025629
    ```

    ```bash
    ./ninlil-linux-amd64 --Storage:MigrationSafety:RestoreSnapshot=20261114025629
    ```

5. Read the log. Success looks like this:

    ```text
    Restoring the database from snapshot 20261114025629.
    Snapshot 20261114025629 is restored. Remove the setting NINLIL_STORAGE_MIGRATION_SAFETY_RESTORE_SNAPSHOT before the next start.
    ```

6. Remove the setting and restart. If you forget, the next start logs a warning that the snapshot
   was already restored and does not restore it again.

To stay on the new version and try the upgrade again after you fixed the cause of a failure, follow
the same steps without step 3. The same start then applies the pending migrations again, with a new
snapshot.

A restore is not atomic, but you can repeat it: if it is interrupted, start again with the same
setting. A snapshot made by one engine cannot be restored into another.

## When the start is refused (exit code 78)

Whenever Ninlil must not run on the database it finds, or the storage settings contradict each
other, it logs a critical entry that begins with `Startup refused:` and exits with **exit code 78**.
[Exit codes](reference/exit-codes.md) shows how to read the message for each kind of installation
and what restarts a refused start. The common causes:

| The message mentions | What to do |
|---|---|
| The database was upgraded by a newer Ninlil version | Start the newer version, or restore a snapshot or backup from before that upgrade. |
| Applying migrations failed and was rolled back | PostgreSQL, SQL Server and SQLite undo a failed migration, so the database is unchanged. Fix the cause the message names and start again, or start the previous version. |
| Applying migrations failed and was not rolled back, or An earlier attempt failed | MySQL cannot undo schema changes. Restore the snapshot the message names. Set `NINLIL_STORAGE_MIGRATION_SAFETY_RETRY_FAILED_MIGRATION=true` only when you know the half-migrated state is safe. |
| The snapshot before the migration could not be taken | Free disk space or fix permissions, or set `NINLIL_STORAGE_MIGRATION_SAFETY_TAKE_SNAPSHOT=false`. |
| There is no snapshot ... to restore | Correct or remove the restore setting. The message lists the ids that exist. |
| The MySQL server has read_only set | Point the connection string at the writable primary. |
| A setting under `Storage:` is wrong | Fix the setting the message names. |

For example, a restore setting with an id that does not exist ends the start like this:

```text
Startup refused: There is no snapshot 20260101000000 to restore. Snapshots of this database: none. Correct or remove NINLIL_STORAGE_MIGRATION_SAFETY_RESTORE_SNAPSHOT.
```

The README lists every cause, including the MongoDB ones, under
[When the start is refused](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#when-the-start-is-refused-exit-code-78).

## Settings

| Variable | Default | Meaning |
|---|---|---|
| `NINLIL_STORAGE_MIGRATION_SAFETY_TAKE_SNAPSHOT` | `true` | Take a snapshot before migrating a database that holds data. |
| `NINLIL_STORAGE_MIGRATION_SAFETY_SNAPSHOTS_TO_KEEP` | `3` | Finished snapshots kept after a successful migration. At least 1. |
| `NINLIL_STORAGE_MIGRATION_SAFETY_RETRY_FAILED_MIGRATION` | `false` | Try again on a database that a failed migration may have left half migrated. |
| `NINLIL_STORAGE_MIGRATION_SAFETY_RESTORE_SNAPSHOT` | empty | The id of a snapshot to restore at the next start. |

Each has a row in
[`ENVIRONMENT.md`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#storage-engine).

## When something goes wrong

- *The start is refused and the log says the database was upgraded by a newer Ninlil version.* You
  started an older program on a database a newer one migrated. Run the newer version, or restore a
  snapshot or backup from before that upgrade.
- *MongoDB refuses to start with an unknown schema step.* The same cause: a newer version used the
  database. Run the newer version, or restore the `mongodump` you took.
- *A snapshot eats disk space.* Ninlil prunes them after the next successful migration. To free
  space sooner, delete the SQLite snapshot files, or see the README's
  [deleting a snapshot by hand](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#settings-12).
- *systemd stops retrying after the refusal.* That is intended: the units that ship with Ninlil set
  `RestartPreventExitStatus=78`. After you fix the cause, run `sudo systemctl restart ninlil`.

## Next steps

- [Exit codes](reference/exit-codes.md)
- [Storage](guides/operations/storage.md)
- [Backup and restore](guides/operations/backup-and-restore.md)
