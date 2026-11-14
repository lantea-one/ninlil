# Configuration

This page shows where Ninlil reads its settings from, how to change one and how to turn on an
extra transport.

## Where settings come from

Ninlil reads its settings from three sources. A later source overrides an earlier one:

1. `appsettings.json` in the content root. The content root is the folder Ninlil starts in. The
   Linux packages and the container image set it to the data directory.
2. `NINLIL_*` environment variables.
3. Command-line arguments, such as `--Storage:MigrationSafety:RestoreSnapshot=<id>`.

Three more facts:

- The admin console's settings pages write your changes to `appsettings.json` and show a note that
  says whether the change applies at once or after a restart.
- Ninlil reads environment variables once, at start. Restart Ninlil after you change one. A value
  set by an environment variable overrides the same value saved on a page.
- You can load another settings file. Set
  [`NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding)
  to its path. Ninlil loads and watches it like `appsettings.json`. It ranks above
  `appsettings.{Environment}.json` and below the environment variables. The path itself comes from
  an environment variable or from `appsettings.json`, never from that file.

## Before you start

Ninlil is installed. The examples use a binary named `./ninlil-linux-amd64` and unprivileged ports.
Start it from the same folder each time, so it finds the same `data/` folder.

## Change a setting with an environment variable

1. Find the setting's row in the [environment variables](reference/environment-variables.md).
2. Build the variable name. Take the setting's configuration path, for example
   `DnsServer:Web:HttpPort`. Split every segment into its PascalCase words, uppercase them, join
   them with `_` and prefix `NINLIL_`. The result is `NINLIL_DNS_SERVER_WEB_HTTP_PORT`. `IPv4` and
   `IPv6` each count as one word. The `Section__Key` form does nothing here, and a list-valued
   setting has no variable.
3. Set the variable in the environment of the Ninlil process and start Ninlil:

    ```bash
    NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
    NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
    NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15300 \
    ./ninlil-linux-amd64
    ```

    Where the variable goes depends on how Ninlil runs: `/etc/ninlil/ninlil.env` for the Deb and
    RPM packages, `-e` for Docker, the service's `Environment` value on Windows.

## Check that it worked

The log lists the ports Ninlil listens on:

```text
      Now listening on: http://[::]:18080
      Now listening on: https://[::]:18443
```

A command-line argument beats an environment variable. Starting the same command with
`--DnsServer:Web:HttpPort=18081` listens on `http://[::]:18081`.

## Settings most installations change

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_STORAGE_ENGINE` | Storage backend: `Sqlite`, `PostgreSql`, `MySql`, `SqlServer`, `MongoDb` | `Sqlite` |
| `NINLIL_STORAGE_CONNECTION_STRING` | Connection string for the selected engine | none (SQLite creates a local file) |
| `NINLIL_DNS_SERVER_WEB_HTTP_PORT` and `NINLIL_DNS_SERVER_WEB_HTTPS_PORT` | The REST API and admin console ports | `80` and `443` |
| `NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT` | The plain DNS port | `53` |
| `NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD` | The bootstrap administrator's password | random, printed once |
| `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_RECURSION` | Whether the server answers recursive queries | `false` |
| `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE` and `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV6_ENABLE` | Whether the DHCPv4 and DHCPv6 engines run | `false` |

The [environment variables](reference/environment-variables.md) page lists the rest.

## Turn on an extra transport

Every DNS transport beyond plain DNS, and both DHCP protocols, are off until you turn them on with
their own `Enable` setting. The admin console's **Presets & Settings** page applies a curated
bundle instead. To turn on DNS-over-TLS, use either form.

With an environment variable:

```bash
NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_ENABLE=true ./ninlil-linux-amd64
```

Or in `appsettings.json`:

```json
{
  "DnsServer": {
    "Protocol": {
      "DnsOverTls": { "Enable": true }
    }
  }
}
```

Check it: with the default port, `ss -ltn 'sport = :853'` lists a listener. The
[ports table](reference/ports.md) lists the default port of every transport, and the
[Encrypted transports guide](guides/dns/encrypted-transports.md) will cover each one.

## Next

- [Environment variables](reference/environment-variables.md)
- [Ports](reference/ports.md)
- [Docker](docker.md), for setting variables in a container
