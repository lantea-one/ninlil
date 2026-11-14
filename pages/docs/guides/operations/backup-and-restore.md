# Backup and restore

This page lets you create an encrypted backup of a Ninlil installation and restore it on the same
machine or on another one. The README holds the complete reference:
[Backup and restore](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#backup-and-restore).

**Backup & Restore** writes one file, with the extension `.ltbk`. It holds:

- the database tables for configuration and identity: zones and records, keys and credentials, users,
  roles, sessions, API tokens, filtering, clients, forwarders, rewrites, webhooks, cluster peers and
  every DHCP table;
- the Data Protection key ring (`key-*.xml`), because every secret in the database opens only with
  it;
- the shared TLS certificate, including its private key;
- the runtime configuration file (`appsettings.json`), which holds the settings you saved in the
  console.

It leaves out the query log, the operational log and metrics, the zone change journal, webhook
delivery attempts, secondary-zone refresh state and cluster sync tombstones. It also leaves out the
JWT signing key, the cluster node id, the root trust anchor file and the ACME certificate files. A
restore replaces all stored data with the content of the file. There is no partial or merged restore.
Ninlil takes no backups by itself.

## Before you start

1. An account with `backup:read` to create a backup and `backup:write` to restore one (the bootstrap
   administrator has both), and its access token, called `<token>` below. The commands use the HTTPS
   port `18443` from the [quick start](../../getting-started/quick-start.md).
2. A passphrase of at least 12 characters
   ([`NINLIL_DNS_SERVER_BACKUP_MINIMUM_PASSPHRASE_LENGTH`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#webhooks-backup-self-update--clustering)).
   Ninlil keeps no copy of it, and a lost passphrase makes the file unreadable. Keep it apart from the
   file, in a password manager for example. The commands call it `<passphrase>`.
3. Memory for the whole archive. Ninlil holds it in memory while it builds or restores it, and it
   refuses a request body above 2 GiB.
4. To restore: a running Ninlil you can sign in to, and the `.ltbk` file. A restore replaces the
   accounts too, so sign-in afterwards uses the accounts from the backup.

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Create a backup

1. In the console, open **Backup & Restore** (`/backup`), enter the passphrase twice and choose
   **Download backup**. Or call the API:

    ```bash
    curl -k -o ninlil-backup.ltbk https://127.0.0.1:18443/api/backup -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"passphrase":"<passphrase>"}'
    ```

    The file `ninlil-backup.ltbk` appears in the current folder. `curl -o` creates it with your
    shell's default permissions, which usually let other users read it. The file holds your whole
    configuration, even though it is encrypted, so restrict it at once (or run `umask 077` before the
    call):

    ```bash
    chmod 600 ninlil-backup.ltbk
    ```

    Without a `passphrase`, Ninlil writes the file unencrypted. An unencrypted file holds the key
    ring and the certificate's private key in plain text, so always set a passphrase.
2. Copy the file to a place that is not on the same machine, and keep the passphrase elsewhere.

### Check that it worked

A file made with a passphrase starts with the five characters `LTBK1`. An unencrypted file starts
with `LTBK0`:

```bash
head -c 5 ninlil-backup.ltbk; echo
```

```text
LTBK1
```

## Restore a backup

1. On another machine, install Ninlil first ([Installation](../../getting-started/installation/index.md))
   and start it once, so you can sign in with its bootstrap administrator.
2. Sign in. In the console, open **Backup & Restore**, choose the file under **Restore from a backup**,
   type the passphrase and confirm the warning. Or call the API:

    ```bash
    curl -k https://127.0.0.1:18443/api/backup/restore -H "Authorization: Bearer <token>" -F archive=@ninlil-backup.ltbk -F passphrase='<passphrase>'
    ```

    ```text
    {"totalEntitiesImported":10,"entityCounts":{"Roles":2,"Users":2,"Sessions":4,"ApiTokens":1,"Zones":1,"Records":0, ...},"note":"Restore succeeded. Restart this process now so the restored database connection, Data Protection key ring, and TLS certificate are actually picked up -- none of them take effect for this already-running process on their own."}
    ```

    (your counts differ). Leave out `-F passphrase=...` for an unencrypted file.
3. Restart Ninlil (`sudo systemctl restart ninlil`, `docker restart ninlil` or
   `Restart-Service Ninlil`). The restored key ring, certificate and settings take effect at the
   restart.
4. To keep access tokens from the old machine valid, set
   [`NINLIL_JWT_SIGNING_KEY`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#identity-authentication--sso)
   on the other machine to the content of the old machine's `data/jwt-signing-key.txt` before the
   restart. Without it, clients get a fresh access token from their refresh token, or sign in again.

### Check that it worked

After the restart, Ninlil is healthy, the old accounts sign in, and the restored data is there:

```bash
curl http://127.0.0.1:18080/status
curl -k https://127.0.0.1:18443/api/zones -H "Authorization: Bearer <token>"
```

```text
{"status":"Healthy","components":null}
{"items":[{"id":"...","name":"example.test", ...}],"skip":0,"take":50,"totalCount":1}
```

Sign in first with an account from the backup, because the restore replaced the accounts of the
machine you restored on. The bootstrap password of that machine answers `401`.

## Behaviour worth knowing

- **A restore checks the whole file before it writes anything.** A wrong passphrase, a damaged file or
  an unknown format returns `400` and leaves your data as it is.
- **On a relational database the restore is all or nothing.** On MongoDB it is not atomic (Ninlil logs
  a warning): an error after the wipe can leave a partly restored database.
- **The key ring is merged.** The keys from the file are added and no key on the machine is deleted.
- **A restore refuses a key ring this server cannot read.** If the backup's key files are encrypted with
  DPAPI from another machine or account, or with a certificate that is neither
  `NINLIL_DATA_PROTECTION_CERTIFICATE_PATH` nor `NINLIL_DATA_PROTECTION_PREVIOUS_CERTIFICATE_PATH`, the
  restore returns `400` before it changes anything. Set the certificate the key ring was encrypted
  with as the previous certificate, restart and restore again
  ([Security](../security/security.md#encrypt-the-key-ring)).
- **Only one restore runs at a time.** A second request returns `409`.
- **Tokens after a restore.** Refresh tokens and API tokens come back, because they are database
  rows. Access tokens are signed with the JWT key, which a backup does not hold, so on a machine with
  another key an old access token answers `401` until the client refreshes it. The console does that
  for you. API tokens keep working.
- **The query log and other history stay as they are.** A backup never captures them, and a restore
  never touches them.
- **SQLite files.** To copy the database file itself instead, stop Ninlil first, or copy
  `ninlil-dns.sqlite3`, `-wal` and `-shm` together.

## When something goes wrong

- *The restore answers `400` with `Failed to decrypt this backup archive. The passphrase may be incorrect, or the archive may be corrupted or have been tampered with.`* Check the passphrase for typing errors and the file for damage (compare its size with the original).
- *Creating a backup answers `400` with `A backup passphrase must be at least 12 characters long. (Parameter 'passphrase')`.* Use a longer passphrase, or none.
- *The restore answers `400` with `An archive file is required.`* The `archive` form field is missing or empty. Use `-F archive=@<file>`.
- *The restore answers `409`.* Another restore is still running. Wait for it to finish.
- *Sign-in fails after the restore.* The accounts and passwords are those of the backup. Use an account
  that existed when the backup was made.
- *Requests fail with `401` after you restore on another machine.* The access token is signed by the
  old JWT key. Sign in again, or refresh the token.

## Next steps

- [Security](../security/security.md): encrypt the key ring with a certificate.
- [Upgrades](../../upgrades.md): take a backup before each upgrade.
- [Storage](storage.md)
