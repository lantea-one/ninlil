# First run

This page describes what Ninlil does the first time it starts, how to find the one-time
administrator password and where its files live.

## Before you start

Ninlil is installed and started ([Installation](installation/index.md) or the
[quick start](quick-start.md)).

## What happens at the first start

Ninlil does these things once, in the data directory (see below):

1. Creates and migrates a SQLite database.
2. Generates a Data Protection key ring and a JWT signing key.
3. Generates a self-signed TLS certificate. Every TLS-based listener shares it: the HTTPS port for
   the API and console, DNS-over-TLS, DNS-over-HTTPS and DNS-over-QUIC.
4. Creates a one-time bootstrap administrator account, user name `admin`. The password is random
   and Ninlil prints it once to the startup log. No REST endpoint returns it later. To choose the
   password yourself, set
   [`NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD`](../reference/environment-variables.md)
   before the first start.

## Steps

1. Find the administrator line in the log. Use the command for your installation:

    | Installed as | Command |
    |---|---|
    | Linux package | `sudo journalctl -u ninlil` |
    | Docker | `docker logs ninlil` |
    | Binary or `dotnet run` | the terminal that runs Ninlil |

    The log holds one warning line with the account. A generated password follows `Password:`. If
    you set the variable, the line says so and prints no password:

    ```text
    warn: Ninlil.Web.Startup.AdministratorBootstrapper[0]
          No user account existed yet, so a bootstrap administrator account was created using the configured Identity:BootstrapAdministratorPassword. Username: admin -- sign in and change this password, or create a real account and delete this one, as soon as possible.
    ```

2. Open the HTTPS port of the host in a browser (`https://<host>/` on the default port 443). The
   browser warns about the self-signed certificate. Accept it for a first look.
3. Sign in as `admin`, then change the password or create a real account and delete this one.

## Check that it worked

The data directory holds these files. Your list can hold more (for example SQLite's `-shm` and
`-wal` files while Ninlil runs):

```text
cluster-node-id.txt
jwt-signing-key.txt
key-<guid>.xml
ninlil-dns.sqlite3
protocol-tls-certificate.pfx
recursive-trust-anchors.json
```

## The data directory

Ninlil keeps its files in one folder. Where it is depends on how you installed it:

| Installed as | Data directory |
|---|---|
| Linux package (Deb, RPM, Arch) | `/var/lib/ninlil` |
| Docker image | `/app/data` (the volume you mount) |
| Windows service | the `data` folder next to the executable, such as `C:\Ninlil\data` |
| A downloaded binary or `dotnet run` | the `data` folder next to the executable or the built program |

On Linux and macOS the folder is readable by its owner only (mode 0700). Back it up by copying all
of it while Ninlil is stopped, or with
[Backup and restore](../guides/operations/backup-and-restore.md).

## When a port is in use

A web port that is in use stops Ninlil. A DNS port that is in use does not: Ninlil logs that the
transport could not listen, keeps running, and `GET /status` reports `Unhealthy`. Free the port or
choose another one (see [Ports](../reference/ports.md)), then restart Ninlil.

## Next

[Configuration](../configuration.md) shows how to change ports and settings.
