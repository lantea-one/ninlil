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
