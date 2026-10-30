# Database Initialization Scripts

Ninlil stores every table and index it needs through Entity Framework Core migrations, which
run automatically the first time the application starts (see
`Ninlil.Dns.Data.EntityFrameworkCore`). What EF Core's migrations *cannot* do for you is the
one-time, server-administrator-level setup each engine needs before an application user is even
allowed to connect: creating the database itself, creating a login for the application to use, and
granting that login the right permissions. That one-time setup is what the scripts in this
directory are for.

| Engine                | Script                          | Run as                                   |
| ---------------------- | -------------------------------- | ----------------------------------------- |
| SQLite                 | *(none needed)*                  | n/a -- see `sqlite/README.md`             |
| PostgreSQL             | `postgresql/init.sql`            | `psql` connected as a superuser (e.g. `postgres`) |
| MySQL / MariaDB        | `mysql/init.sql`                 | `mysql` connected as a user with `CREATE USER`/`GRANT` privileges |
| Microsoft SQL Server   | `sqlserver/init.sql`             | `sqlcmd` connected as `sa` or an equivalent administrator |
| MongoDB                | `mongodb/init.js`                | `mongosh` connected as a user with the `userAdmin`/`root` role |

Every script creates the same conceptual things -- a database named `ninlil` and an
application login named `ninlil` -- using an intentionally obvious placeholder password.
**Change the password before using any of these scripts against a real server.**

Once the database and login exist, point `Storage:ConnectionString` (and set `Storage:Engine`
appropriately) in the application's configuration at them, start the application, and Entity
Framework Core takes over from there.

## What the application account may do

The accounts these scripts create hold exactly the rights Ninlil needs, and no more:

| Engine | Rights of the `ninlil` account |
| --- | --- |
| PostgreSQL | Owner of the `ninlil` database; no right to create databases or roles |
| MySQL / MariaDB | `ALL PRIVILEGES` on `ninlil.*` only, from any host until you [narrow it](#narrow-the-mysql-account-to-the-application-host) |
| Microsoft SQL Server | The `db_owner` role in the `ninlil` database only |
| MongoDB | The `readWrite` role on the `ninlil` database |

These rights are enough for everything Ninlil does on its own, including the **migration
snapshots** it takes before an upgrade. A snapshot is a schema (PostgreSQL, SQL Server) or a set of
tables (MySQL) created inside the application's own database, so it needs no extra privilege and
no tool such as `pg_dump`. See
[Upgrades and migration snapshots](../../../README.md#upgrades-and-migration-snapshots).

## Extra grants for a read replica

Only if you configure a read replica
([Read replicas and database clusters](../../../README.md#read-replicas-and-database-clusters)),
Ninlil also measures how far the replica is behind. That needs one more right per engine. The
scripts do not grant it, because it widens the account beyond its own database. Run the command
once, connected as an administrator, and restart Ninlil. Without it the replica's lag is unknown and
Ninlil never reads from the replica.

| Engine | Where to run it | Command |
| --- | --- | --- |
| PostgreSQL | nowhere | Nothing to grant. |
| MySQL / MariaDB | `mysql` on the primary (account changes replicate; if yours do not, run it on each replica) | `GRANT REPLICATION CLIENT ON *.* TO 'ninlil'@'%';` (use the account's host if you narrowed it) |
| Microsoft SQL Server | `sqlcmd` on every instance that can become the primary | `GRANT VIEW SERVER STATE TO [ninlil];` |
| MongoDB | `mongosh` connected to the primary, when authentication is on | `db.getSiblingDB("ninlil").grantRolesToUser("ninlil", [{ role: "clusterMonitor", db: "admin" }])` |

For SQL Server, also create the `ninlil` login on every replica instance with the same SID as on
the primary, because a login belongs to the server and not to the replicated database.

## Narrow the MySQL account to the application host

### What it does

`mysql/init.sql` creates the login as `'ninlil'@'%'`. In MySQL an account is a name plus the host it
may connect from, and `%` means any host. Anyone who can reach the server's port and knows the
password can then log in as `ninlil` from anywhere. This section limits the account to the machine
that runs Ninlil. The rights stay as they are: `ALL PRIVILEGES` on `ninlil.*` only.

### Before you start

1. The MySQL (or MariaDB) server holds the `ninlil` database and login (you ran `mysql/init.sql`),
   and Ninlil has connected to it at least once.
2. A MySQL account that may rename users, for example `root`.
3. The machine on which Ninlil runs. Find the address the server sees it from while Ninlil is running,
   with this query (the `host` column holds `address:port`):

   ```sql
   SELECT DISTINCT SUBSTRING_INDEX(host, ':', 1) AS application_host FROM information_schema.processlist WHERE user = 'ninlil';
   ```

   The commands below call the result `<application-host>`. With Ninlil in a container, the server
   usually sees the container network's gateway address, not the container's address.

### Steps

1. Rename the account. The rights move with it:

   ```sql
   RENAME USER 'ninlil'@'%' TO 'ninlil'@'<application-host>';
   ```

2. If you set up a read replica, repeat the lag-monitoring grant for the renamed account
   (see [Extra grants for a read replica](#extra-grants-for-a-read-replica)): the command there
   names `'ninlil'@'%'`, so write `'ninlil'@'<application-host>'` instead.

### Check that it worked

```sql
SELECT user, host FROM mysql.user WHERE user = 'ninlil';
SHOW GRANTS FOR 'ninlil'@'<application-host>';
```

```text
user	host
ninlil	<application-host>
Grants for ninlil@<application-host>
GRANT USAGE ON *.* TO `ninlil`@`<application-host>`
GRANT ALL PRIVILEGES ON `ninlil`.* TO `ninlil`@`<application-host>`
```

Restart Ninlil and open `/status`; the answer says `Healthy`.

### When something goes wrong

- *Ninlil stops at start with `Access denied for user 'ninlil'@'<address>'`.* The address in the
  message is the one the server sees. Rename the account to that address:
  `RENAME USER 'ninlil'@'<application-host>' TO 'ninlil'@'<address>';`.
- *Ninlil runs on several machines.* Use a host pattern that covers them, such as
  `'ninlil'@'172.2.2.%'` for a subnet: `RENAME USER 'ninlil'@'<application-host>' TO 'ninlil'@'172.2.2.%';`.
