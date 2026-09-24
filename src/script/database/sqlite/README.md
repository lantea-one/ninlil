# SQLite

SQLite needs no initialization script at all -- that is the entire point of it being Ninlil's
default storage engine. There is no server to install, no login to create, and no database to
provision ahead of time.

The first time the application starts with `Storage:Engine` left unset (or explicitly set to
`Sqlite`) and no `Storage:ConnectionString` configured, it creates a `data/` directory next to the
application binary and generates `data/ninlil-dns.sqlite3` inside it automatically. Entity
Framework Core's migrations then create every table the application needs directly inside that
file the moment it is created.

If you would rather control exactly where the file lives, set `Storage:ConnectionString` to a
SQLite connection string yourself, for example:

```json
{
  "Storage": {
    "Engine": "Sqlite",
    "ConnectionString": "Data Source=/var/lib/ninlil-dns/ninlil-dns.sqlite3"
  }
}
```

Just make sure the directory the file lives in already exists and is writable by whichever user
account the application runs as.
