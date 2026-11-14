# Build and run from source

## Before you start

Check each tool with the command in the last column. The first must print a `10.x` version.

| Tool | Why | Check |
|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) (the version in `global.json`) | Builds and runs everything under `src/dotnet/` | `dotnet --version` |
| [Node.js](https://nodejs.org/) and npm | Builds the Angular admin console. Without them the DNS and DHCP engines and the REST API still build | `node --version` |
| A Java runtime | The OpenAPI generator that builds the console's API client is a Java tool. Needed only when the console builds | `java -version` |

You need no database server. The default storage engine is SQLite, and Ninlil creates and migrates
the file on first run.

## Steps

1. Clone the repository:

    ```bash
    git clone https://git.lantea.one/lantea-one/ninlil.git
    cd ninlil
    ```

2. Build and run it. The ports are moved off the privileged defaults:

    ```bash
    NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
    NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
    NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15300 \
    dotnet run --project src/dotnet/src/Ninlil.Web
    ```

## Check that it worked

The log ends with these lines (the `Storage:` line comes earlier; your paths differ):

```text
      Storage: Sqlite, primary a local file, no read replica.
      Now listening on: http://[::]:18080
      Now listening on: https://[::]:18443
      Application started. Press Ctrl+C to shut down.
```

```bash
curl http://localhost:18080/status
```

```text
{"status":"Healthy","components":null}
```

## Behaviour worth knowing

A `dotnet build`, `dotnet run` or `dotnet publish` of `Ninlil.Web` also builds the Angular workspace
and copies it into the project's `wwwroot/`. When `npm` is not on your `PATH`, the build logs a
warning and skips that step: the engines and the REST API work, and the admin console is missing.
To change the code, follow
[CONTRIBUTING.md](https://git.lantea.one/lantea-one/ninlil/src/branch/main/CONTRIBUTING.md).
