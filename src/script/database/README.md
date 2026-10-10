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
| MySQL / MariaDB | `ALL PRIVILEGES` on `ninlil.*` only |
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
| MySQL / MariaDB | `mysql` on the primary (account changes replicate; if yours do not, run it on each replica) | `GRANT REPLICATION CLIENT ON *.* TO 'ninlil'@'%';` |
| Microsoft SQL Server | `sqlcmd` on every instance that can become the primary | `GRANT VIEW SERVER STATE TO [ninlil];` |
| MongoDB | `mongosh` connected to the primary, when authentication is on | `db.getSiblingDB("ninlil").grantRolesToUser("ninlil", [{ role: "clusterMonitor", db: "admin" }])` |

For SQL Server, also create the `ninlil` login on every replica instance with the same SID as on
the primary, because a login belongs to the server and not to the replicated database.
