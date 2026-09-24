# Ninlil

Ninlil is a from-scratch, clean-room, **MIT-licensed** DNS / DHCPv4 / DHCPv6 server suite, built
on **.NET 10**, with a full REST API (OpenAPI/Swagger/ReDoc) and an **Angular** admin console for
day-to-day operation. It runs as a single combined host process — the DNS engine, the DHCP engine,
the REST API, a real-time SignalR event feed, and the admin console are all served from one
`dotnet run`, with no separate services to wire together.

Ninlil is an independent, clean-room implementation that brings together, in one project, the
kind of functionality more commonly found split across three separate categories of tool: a
full-featured, standards-based authoritative-and-recursive DNS server (in the spirit of Technitium
DNS Server), a network-wide ad/tracker blocker with per-client filtering and parental controls (in
the spirit of Pi-hole and AdGuard Home), and a lightweight DNS/DHCP forwarder and address
management service (in the spirit of dnsmasq) — designed and built entirely against public IETF
RFCs and IANA registries, never by reading or porting any other project's source code. See
[`NOTICE.md`](./NOTICE.md) for the full clean-room statement and third-party dependency license
table, and [`LICENSE`](./LICENSE) for the MIT license text.

## What's in the box

### DNS server

- **Authoritative DNS**: primary, secondary, stub, forwarder, and RFC 9432 catalog zones; RFC 1996
  NOTIFY; RFC 2136 dynamic update; AXFR/IXFR zone transfers (including DNSSEC-signed content and
  RFC 8945 §5.3.1 multi-message TSIG chaining) over plain TCP, TLS, or QUIC.
- **Recursive & validating resolution**: a full RFC 1035 §7 iterative walk with RFC 9156 QNAME
  minimization, RFC 8767 serve-stale, prefetch, EDNS Client Subnet, and DNS64.
- **DNSSEC**: RSA/DSA/ECDSA/EdDSA signing and validation, NSEC/NSEC3 (with RFC 5155 opt-out),
  automated proactive re-signing, and a full RFC 6781 automated key-rollover state machine
  (Pre-Publish for ZSKs, Double-Signature for KSKs, with automatic DS cutover when the parent zone
  is also hosted locally). RFC 5011 automated root trust-anchor rollover on the recursive side.
- **Every major DNS transport**: plain UDP/TCP (on by default — this is a DNS server, after all),
  DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC, and DNSCrypt v2. Every transport beyond plain DNS is
  off by default; see [Enabling additional transports](#enabling-additional-transports).
- **General upstream forwarding** with health-checked failover, independent of the recursive
  resolver, supporting the full `sdns://` DNSCrypt stamp grammar.
- **TSIG (RFC 8945)** signed transfers and dynamic updates, and an **ACME (RFC 8555) DNS-01
  client** for automated certificate issuance against zones this server hosts itself, with
  optional automated CAA record and PTR/reverse-zone management (including RFC 2317 classless
  delegation).
- **Cloud DNS zone sync**: Cloudflare, AWS Route 53, and DigitalOcean, kept in sync alongside
  classic AXFR/IXFR zones, covering A/AAAA/CNAME/MX/NS/TXT/SRV/CAA records.
- **Filtering and parental controls**: block/allow-list filtering with subscribed external
  blocklists, a curated "blocked services" catalog, Safe Search enforcement, per-client and
  per-client-group policy, time-of-day schedules, and a one-click time-boxed pause — all resolved
  against a persistent per-device client identity (via DHCP lease data, live ARP/neighbor-table
  observation, or an operator-defined static IP/CIDR pin, in that order of preference).
- **DNS rewrites** (lightweight domain-to-address/CNAME overrides), general **rate limiting**, and
  **RFC 7873 DNS Cookies** for abuse hardening.
- **A persistent, searchable query log** with configurable privacy levels and a per-client activity
  view.
- **Outbound subscribable webhooks** for zone changes, DNSSEC re-signing, ACME renewal outcomes,
  cloud sync failures, and trust-anchor rollovers, with persisted delivery retry that survives a
  restart.
- **Multi-node clustering**: membership, heartbeat, catalog-zone auto-sync, operational config
  sync, and identity/RBAC federation across nodes, with an aggregate cluster dashboard.
- **Configuration presets** (`Open` / `Baseline` / `Protective` / `Family` / `FIPS`) that apply a
  curated bundle of hardening settings and blocklist subscriptions in one call.
- **Full-system, passphrase-encrypted backup and restore**, and a check-only self-update notifier.

### DHCP, DHCPv6 & Router Advertisement

- **A full dual-stack DHCPv4/DHCPv6 service** running in the same host process as DNS: DHCPv4
  answers a real DORA exchange (plus DECLINE/RELEASE/INFORM) with static reservations, standard
  option delivery, and tag-based conditional option delivery (Vendor Class Identifier/User
  Class/Relay Agent Circuit/Remote ID matching). DHCPv6 answers SOLICIT/REQUEST/RENEW/REBIND/
  RELEASE/DECLINE/CONFIRM/INFORMATION-REQUEST with stateful address assignment (IA_NA) and Prefix
  Delegation (IA_PD) side by side, plus optional Rapid Commit.
- **A genuine, mutually-exclusive relay-agent mode** for each protocol (RFC 3046 for DHCPv4, RFC
  8415 §7/16 RELAY-FORW/RELAY-REPL for DHCPv6), as an alternative to running the local engine.
- **RFC 4861/4862 Router Advertisement/SLAAC**, including per-interface-distinct prefix/flag
  configuration.
- **A real, read-only RFC 1350 TFTP server** for PXE boot delivery, driven by a scope's own boot
  file name and next-server address.
- **Automatic DNS registration**: a granted lease auto-registers a real A/AAAA record (including a
  host name presented via the DHCPv6 Client FQDN option) in a scope-configured DNS zone, removed
  again on release or natural expiry — with a reverse PTR record as a free side effect of the
  existing PTR-sync feature.

### Admin API & console

- **A complete REST API** (65+ controllers) covering full CRUD for every DNS/DHCP resource, every
  settings surface, and identity/admin management, documented via Swagger UI (`/swagger`) and
  ReDoc (`/documentation`), with real, pushed-down pagination throughout.
- **JWT bearer authentication** with refresh-token rotation, static API tokens, TOTP and email
  two-factor authentication, recovery codes, and generic OpenID Connect SSO federation.
- **Fine-grained, permission-claim-based authorization** (`resource:action`-shaped, e.g.
  `zones:write`) with full role/user/permission management.
- **A full Angular 22 admin console** — standalone components, signals-based state, zoneless
  change detection, built on CoreUI — covering every REST resource with a live dashboard, a
  SignalR real-time event feed, self-service account security, configurable branding, and both
  light and dark themes across five color palettes.
- **A `GET /status` health endpoint** with ten independent subsystem checks.

## Installation

Every release, and every build of every branch, is published from the project's Forgejo pipeline
(see [Releases and versioning](#releases-and-versioning)). Pick whichever fits:

### Download a binary

Each [release](https://git.lantea.one/Lantea.One/ninlil/releases) carries a self-contained,
single-file executable per platform — no .NET installation needed; the runtime, every native
library, and the admin console are all inside the one file:

| Platform | File |
|---|---|
| Linux x86-64 | `ninlil-linux-amd64` |
| Linux ARM64 | `ninlil-linux-arm64` |
| macOS Intel | `ninlil-macos-amd64` |
| macOS Apple Silicon | `ninlil-macos-arm64` |

```bash
curl -LO https://git.lantea.one/Lantea.One/ninlil/releases/download/<version>/ninlil-linux-amd64
curl -LO https://git.lantea.one/Lantea.One/ninlil/releases/download/<version>/SHA256SUMS
sha256sum --check --ignore-missing SHA256SUMS
chmod +x ninlil-linux-amd64
sudo ./ninlil-linux-amd64        # or override the ports, see "Ports" below
```

The server keeps all of its state in a `data/` directory **next to the executable**, so put the
binary somewhere its user can write to (for example `/opt/ninlil/`), not in `/usr/local/bin`.
On first start, the native libraries bundled in the file are extracted once to
`~/.net/` (override with `DOTNET_BUNDLE_EXTRACT_BASE_DIR`). A systemd unit to start from lives in
[`src/script/systemd/`](./src/script/systemd/).

**macOS** binaries are ad-hoc signed, not notarized: remove the download quarantine flag once with
`xattr -d com.apple.quarantine ninlil-macos-arm64` before the first run. On macOS the DNS server,
REST API, and admin console work fully; the DHCP, Router Advertisement, and passive device-presence
features rely on Linux kernel interfaces and are unavailable, and DNS-over-QUIC needs `libmsquic`
from Homebrew.

### Arch Linux

Every release carries installable `ninlil-bin` packages for `x86_64` and `aarch64`:

```bash
sudo pacman -U https://git.lantea.one/Lantea.One/ninlil/releases/download/<tag>/ninlil-bin-<pkgver>-1-x86_64.pkg.tar.zst
sudo systemctl enable --now ninlil
```

[`src/archlinux/`](./src/archlinux/) also has `ninlil`, a PKGBUILD that builds the newest release
branch from source. Both install a `ninlil` systemd service running as an unprivileged user with
its state in `/var/lib/ninlil`.

### Run the container image

A hardened, multi-architecture (`linux/amd64`, `linux/arm64`) image is published to the Forgejo
container registry:

```bash
docker run -d --name ninlil \
  -p 80:80 -p 443:443 -p 53:53/udp -p 53:53/tcp \
  -v ninlil-data:/app/data \
  git.lantea.one/lantea.one/ninlil:latest
```

See [Running with Docker](#running-with-docker) below for what the image contains and how to run
DHCP from it.

### Build and run from source

| Tool | Why | Notes |
|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Builds and runs everything under `src/dotnet/` | Version pinned by [`global.json`](./global.json) |
| [Node.js](https://nodejs.org/) + npm | Builds the Angular admin console | Only needed to build the admin console; the DNS/DHCP engines and REST API build without it |
| A Java runtime (JRE/JDK) | `@openapitools/openapi-generator-cli` (used to regenerate the Angular app's typed API client from the backend's own OpenAPI spec) wraps the real, Java-based OpenAPI Generator tool | Only needed when the Angular build actually runs — see [Frontend](#frontend-srcangular) below |

You do **not** need PostgreSQL, MySQL, SQL Server, or MongoDB to get started — the default storage
engine is SQLite, and the database file is created and migrated automatically on first run.

```bash
git clone https://git.lantea.one/Lantea.One/ninlil.git
cd ninlil
dotnet run --project src/dotnet/src/Ninlil.Web
```

That's it. A single `dotnet build`/`dotnet run`/`dotnet publish` of `Ninlil.Web` automatically
builds the Angular workspace too (via a custom MSBuild target, `NinlilBuildAngularAdminSpa`) and
copies its production output into that project's own `wwwroot/`, so the admin console is served
from the exact same process and port as the REST API — no separate `npm run build`/`ng serve`
step, no CORS configuration, nothing else to run. (If `npm` isn't on your `PATH` at all, the build
logs a warning and skips the Angular step rather than failing outright — useful if you only care
about the DNS/DHCP engine or REST API for now — but you won't get a working admin console until
it's built at least once.)

## First run

On first run, the host will:

1. Create and migrate a SQLite database under a local `data/` directory.
2. Generate and persist a Data Protection key ring and a JWT signing key (also under `data/`).
3. Generate a self-signed TLS certificate (also persisted under `data/`), shared by every
   TLS-based listener (the HTTPS/API/admin-console port, DNS-over-TLS, DNS-over-HTTPS,
   DNS-over-QUIC) so an ACME renewal later updates all of them at once with no restart.
4. Create a one-time **bootstrap administrator** account (username `admin` by default) with a
   **randomly generated password printed once to the startup log**. Copy it down before it
   scrolls past; there is no REST endpoint to retrieve it again afterward. (You can also provide
   one explicitly via the `NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD` environment variable,
   which is handy for a scripted or unattended first boot.)

Once it's up, open a browser to the host's configured HTTPS port (see below) to reach the admin
console, or `/documentation` for a browsable REST API reference.

### Ports

| Port | Purpose | Default state |
|---|---|---|
| `80` / `443` (`DnsServer:Web:HttpPort`/`HttpsPort`) | REST API, SignalR hub, the Angular admin console, and DNS-over-HTTPS — all on one Kestrel listener | Always on |
| `53` UDP/TCP (`DnsServer:Protocol:Plain:Port`) | Plain DNS | On by default (the one transport that is) |
| `853` (`DnsServer:Protocol:DnsOverTls:Port`) | DNS-over-TLS | Off by default |
| `853` UDP (`DnsServer:Protocol:DnsOverQuic:Port`) | DNS-over-QUIC | Off by default (needs `libmsquic`/OpenSSL on the host OS) |
| `5353` UDP/TCP (`DnsServer:Protocol:DnsCrypt:Port`) | DNSCrypt v2 | Off by default |
| `67`/`68` UDP (`DhcpServer:Protocol:Dhcpv4:*Port`) | DHCPv4 | Off by default |
| `547`/`546` UDP (`DhcpServer:Protocol:Dhcpv6:*Port`) | DHCPv6 | Off by default |
| `69` UDP (`DhcpServer:Tftp:Port`) | TFTP (PXE boot delivery) | Off by default |

Ports 53/67/69/80/443 are privileged on Linux/macOS. Either run with elevated privileges (or grant
the process `CAP_NET_BIND_SERVICE` on Linux), or override the ports in configuration to something
unprivileged for local development, e.g.:

```bash
NINLIL_DNS_SERVER_WEB_HTTP_PORT=8080 \
NINLIL_DNS_SERVER_WEB_HTTPS_PORT=8443 \
NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=5300 \
dotnet run --project src/dotnet/src/Ninlil.Web
```

Every environment variable this application reads follows this same single, `NINLIL_`-prefixed,
single-underscore naming scheme. **See [`ENVIRONMENT.md`](./ENVIRONMENT.md) for the complete,
generated-and-verified reference of every supported variable**, organized by subsystem — the
table below covers only the handful you're most likely to set for a first deployment.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_STORAGE_ENGINE` | Storage backend: `Sqlite`, `PostgreSql`, `MySql`, `SqlServer`, `MongoDb` | `Sqlite` |
| `NINLIL_STORAGE_CONNECTION_STRING` | Connection string for the selected engine | *(none — SQLite auto-generates a local file)* |
| `NINLIL_DNS_SERVER_WEB_HTTP_PORT` / `..._HTTPS_PORT` | The REST API/admin-console ports | `80` / `443` |
| `NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT` | The plain DNS port | `53` |
| `NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD` | Set the bootstrap admin password explicitly instead of accepting a random one | *(random, printed once)* |
| `NINLIL_JWT_SIGNING_KEY` | The JWT signing key — set this explicitly for any real deployment | *(none — must be supplied)* |
| `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_RECURSION` | Whether this server answers recursive queries | `false` |
| `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE` / `...DHCPV6_ENABLE` | Whether the local DHCPv4/DHCPv6 engines are active | `false` |

### Enabling additional transports

Every transport beyond plain DNS, and both DHCP protocols, are off by default and turned on via
their own `Enable` flag in configuration (or through the admin console's **Presets & Settings**
page for a curated bundle). For example, to turn on DNS-over-TLS:

```json
{
  "DnsServer": {
    "Protocol": {
      "DnsOverTls": { "Enable": true }
    }
  }
}
```

### Choosing a storage engine

The default is SQLite (`Storage:Engine = Sqlite`); PostgreSQL, MySQL, SQL Server, and MongoDB are
all also fully supported. Set `Storage:Engine` and the matching connection string — for example,
pointing at a real PostgreSQL instance via environment variables:

```bash
NINLIL_STORAGE_ENGINE=PostgreSql \
NINLIL_STORAGE_CONNECTION_STRING="Host=localhost;Database=ninlil;Username=ninlil;Password=change-me" \
dotnet run --project src/dotnet/src/Ninlil.Web
```

or the equivalent in `appsettings.json`:

```json
{
  "Storage": { "Engine": "PostgreSql" },
  "ConnectionStrings": { "PostgreSql": "Host=localhost;Database=ninlil;Username=ninlil;Password=change-me" }
}
```

Relational engines have their EF Core migrations applied automatically on startup; MongoDB is
schemaless and needs no migration step at all.

## Using the admin console

Sign in at the host's HTTPS port with the bootstrap administrator credentials from first-run
setup, then use the **Users**/**Roles** pages to create real, appropriately-scoped accounts (and
consider disabling the bootstrap account once you have) — it holds a wildcard permission claim
so it can always self-recover access, and is never automatically removed.

From there, the console covers zone and record management (every registered record type has its
own form), TSIG/DNSSEC key and cloud provider credential management (secrets are shown to you
exactly once, at creation — copy them down immediately), filtering and client/parental-control
management, DHCP scopes/reservations/leases, forwarders, outbound webhooks, server settings
(presets, rate limiting, the event broker, DNS Cookies, clustering), and your own account's
two-factor authentication setup (via the account menu in the top-right corner).

## REST API

- **Swagger UI**: `/swagger`
- **Raw OpenAPI document**: `/swagger/v1/swagger.json`
- **ReDoc reference**: `/documentation`
- **Health/status**: `GET /status` — an unauthenticated caller gets a high-level summary only; an
  authenticated caller holding the `status:read` permission gets a full per-subsystem breakdown.

Authentication is JWT bearer (`POST /api/auth/login`, with refresh-token rotation and optional
TOTP/e-mail two-factor), checked per-endpoint against a `resource:action`-shaped permission claim
(e.g. `zones:write`). There is no API versioning and no CORS support — the API and the admin
console are meant to be served from the same origin, as they are by default.

## Running with Docker

The published image (`git.lantea.one/lantea.one/ninlil`) is the same single combined host
described above — the DNS and DHCP engines, the REST API, and the admin console in one process —
built for a small attack surface:

- **Chiseled base image** (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra`): no shell,
  no package manager, nothing but the .NET runtime, ICU, time-zone data, OpenSSL, and
  `libmsquic` for DNS-over-QUIC.
- **Non-root**: the server runs as uid `1654`. The application files are read-only to it; only
  `/app/data` is writable.
- **Everything persistent in one volume**: `/app/data` holds the database (with the default SQLite
  engine), the Data Protection key ring, the JWT signing key, certificates, and the
  `appsettings.json` the admin console's settings pages write to.

| Tag | What it is |
|---|---|
| `latest` | The newest release |
| `<yyyy>.<mm>.<dd>.<increment>` | A specific release, e.g. `2026.09.24.1` |
| `<yyyy>.<mm>.<dd>.<increment>-<branch>-<short-sha>` | A branch build; removed after 24 hours |

### Run it

```bash
docker run -d --name ninlil \
  -p 80:80 -p 443:443 -p 53:53/udp -p 53:53/tcp \
  -v ninlil-data:/app/data \
  git.lantea.one/lantea.one/ninlil:latest
```

This starts a fully working instance on the zero-config SQLite default — open `https://<host>/`
for the admin console (the self-signed certificate generated on first run will trigger a browser
warning; that's expected) and sign in with the bootstrap administrator credentials printed once to
`docker logs ninlil`. **Always mount a volume at `/app/data`**: without it, every previously
encrypted secret becomes unreadable the moment the container is replaced.

The unprivileged user can bind ports 53/80/443 because Docker's bridge networking allows any
process in a container to bind a low port. Plain DNS (53) and the REST API/admin console (80/443)
are the only ports published above; every other transport and both DHCP protocols are off by
default and need their own port published once enabled (see [Ports](#ports)).

`.NET` runtime settings such as server garbage collection can be set with the same `NINLIL_`
scheme as everything else — `NINLIL_DOTNET_GC_SERVER=1` — see
[`ENVIRONMENT.md`](./ENVIRONMENT.md#net-runtime-settings).

### DHCP and Router Advertisement

A DHCP/RA server has to see raw broadcast and multicast traffic from the LAN, which Docker's
bridge network does not forward. Run the container on the host's network instead, as root, with
the raw-socket and interface capabilities DHCP/RA need:

```bash
docker run -d --name ninlil --network host \
  --user 0:0 --cap-add NET_ADMIN --cap-add NET_RAW \
  -e NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE=true \
  -v ninlil-data:/app/data \
  git.lantea.one/lantea.one/ninlil:latest
```

### A fuller example: `docker-compose.yml`

[`docker-compose.yml`](./docker-compose.yml) at the repository root demonstrates a more realistic
deployment: Ninlil wired up against a real PostgreSQL backing store (instead of the SQLite
default) with named volumes for both services' own persistent data, explicit `NINLIL_` environment
variables for the storage engine and bootstrap administrator credentials, and heavily commented
guidance on the two tradeoffs every real deployment eventually runs into — privileged host ports
(including the common `systemd-resolved` conflict on port 53), and DHCP/RA's need for host
networking, shown by its `ninlil-host-network` service behind the `host-network` profile:

```bash
docker compose up --build                                                   # DNS + API + console
docker compose --profile host-network up --build ninlil-host-network postgres  # with DHCP/RA
```

Every secret-shaped value in that file (database/admin passwords) is a placeholder — change them,
along with the connection string and any port remapping, before using it as anything but a local
trial.

### Building the image yourself

The [`Dockerfile`](./Dockerfile) builds every distributable output, selected with `--target`, and
cross-compiles on the build machine's own architecture — no CPU emulation is needed for either
image architecture:

```bash
docker build -t ninlil .                                                    # the runtime image
docker buildx build --platform linux/amd64,linux/arm64 -t <registry>/ninlil --push .
docker buildx build --target binaries --output type=local,dest=dist .       # the four binaries
```

## Development

### Backend (`src/dotnet/`)

```bash
dotnet tool restore                      # one-time: restores dotnet-ef, csharpier, reportgenerator
dotnet restore Ninlil.slnx
dotnet build Ninlil.slnx -c Release      # should build with 0 warnings
dotnet csharpier check .                 # formatting gate — CI enforces this
dotnet test Ninlil.slnx -c Release       # the full xUnit test suite
```

Run `dotnet csharpier format .` before committing — CI runs `dotnet csharpier check .` and will
fail on anything that doesn't already match.

### Frontend (`src/angular/`)

```bash
cd src/angular
npm install
npm run build          # regenerates the typed API client from the committed OpenAPI snapshot, then builds
npm run lint            # eslint
npm run format:check    # prettier
npm test                # Jasmine/Karma unit tests (needs Chromium/Chrome; set CHROME_BIN if it
                         # is not found automatically, e.g. CHROME_BIN=/usr/bin/chromium)
```

`npm run fetch:openapi` re-fetches `openapi/swagger.json` from a locally running instance of the
backend (adjust the URL in `package.json` to match how you're running it) whenever the backend's
own REST surface changes; the committed snapshot is what every `npm run build` actually codegens
against (via the automatic `prebuild` step, `npm run generate:api`), so this is a deliberate,
separate, manual step, not something that happens automatically. **Building this workspace
requires a Java runtime** on top of Node/npm, since the generator wraps a Java-based tool.

### Standard verification loop

Before considering a change done, both sides of the codebase have the same shape of gate:

1. `dotnet build Ninlil.slnx` → fix errors → `dotnet csharpier format .` →
   `dotnet test Ninlil.slnx` (must stay green) → re-run any relevant scratch smoke test for
   anything touching runtime wiring (DI composition, migrations, event delivery, real
   sockets/TLS/HTTP) — a passing build is not evidence anything works at runtime.
2. For Angular: `npm run lint` / `npm run format:check` / `npm run build` / `npm test`, all clean.

The pipeline runs exactly these checks inside Docker, and so can you, with nothing but Docker
installed:

```bash
docker buildx build --target test-admin-console .          # lint, format check, Karma suite
docker build --target test-dotnet -t ninlil-test .          # format check and Release build
docker run --rm --add-host host.docker.internal:host-gateway \
  -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
  --user "$(id -u):$(id -g)" --group-add "$(stat -c %g /var/run/docker.sock)" \
  -v /var/run/docker.sock:/var/run/docker.sock ninlil-test  # every xUnit project
```

The test container needs the Docker socket because a few tests start their own database
containers through Testcontainers, which it reaches through the host gateway. It runs in its own
network namespace, so no DNS, DHCP, or Router Advertisement test can collide with a real service
on the machine or answer a real client on its network.

## Releases and versioning

Versions are generated by the pipeline at build time, never edited by hand, and are date-based:

| Build | Version | Published as |
|---|---|---|
| Any push to a branch | `v<yyyy>.<mm>.<dd>.<increment>R<branch>+<short-sha>` | A pre-release, a container tag, and NuGet packages — the pre-release, its tag, its container tag, and its NuGet versions are all removed after 24 hours |
| A push to a release branch `v<yyyy>.<mm>.<dd>.<increment>` | `v<yyyy>.<mm>.<dd>.<increment>R<short-sha>` | A permanent release, container tags for the version and `latest`, and NuGet packages |

For a branch build, the date is the build's UTC date and the increment is the next free one for
that branch on that day; for a release, both come from the release branch's own name. The same
version is written into every output in the form each one accepts:

| Where | Example (release) | Example (branch build of `develop`) |
|---|---|---|
| Release name, git tag, `InformationalVersion` | `v2026.09.24.1Ra1b2c3d` | `v2026.09.24.3Rdevelop+a1b2c3d` |
| `AssemblyVersion` / `FileVersion` | `2026.9.24.1` | `2026.9.24.3` |
| NuGet package | `2026.9.24.1+a1b2c3d` | `2026.9.24.3-develop+a1b2c3d` |
| Container tag | `2026.09.24.1` | `2026.09.24.3-develop-a1b2c3d` |

Branch names are reduced to lower-case letters, digits, and hyphens for these labels
(`feature/DoT-tuning` becomes `feature-dot-tuning`). A local build is always `0.0.0-local`.

### Using the libraries from NuGet

Every library project under `src/dotnet/src/` — the DNS wire-format layer, the DNS and DHCP
server engines, the transports, the storage layers, and the shared helpers; everything except the
`Ninlil.Web` host — is published as a NuGet package (`Ninlil.Dns.Common`, `Ninlil.Dhcp.Server.Core`,
and so on) to the Forgejo package registry, with its XML documentation and embedded debug symbols.
Add the feed once:

```bash
dotnet nuget add source https://git.lantea.one/api/packages/Lantea.One/nuget/index.json \
  --name ninlil --username <user> --password <access-token> --store-password-in-clear-text
dotnet add package Ninlil.Dns.Common --version 2026.9.24.1
```

Packages from one build depend on exactly that build's other packages. Branch builds publish
pre-release versions (`2026.9.24.3-develop`) that are removed after 24 hours; depend on a release.

### Cutting a release

1. Merge `develop` (or a `hotfix/*` branch) into `main` through a pull request.
2. Create the release branch from `main`, named for the release's date and that day's increment:

   ```bash
   git switch main && git pull
   git switch -c v2026.09.24.1
   git push -u origin v2026.09.24.1
   ```

3. The pipeline verifies the branch points at a commit on `main`, runs every test, and publishes
   the release. A release version is published once: pushing the same release branch again with a
   different commit fails rather than overwriting it.
4. Merge `main` back down into `develop`.

### The pipeline

The workflows live in [`.forgejo/workflows/`](./.forgejo/workflows/) and run on the project's own
`asgard` Forgejo runner:

| Workflow | Runs on | Does |
|---|---|---|
| `build.yml` | Every push to a non-release branch; pull requests into `develop`/`main` | Tests, then (pushes only) builds and publishes a pre-release |
| `release.yml` | Every push to a `v<yyyy>.<mm>.<dd>.<increment>` branch | Tests, then builds and publishes a release |
| `pipeline.yml` | Called by both of the above | The shared jobs: version; .NET and admin console tests; then, in parallel, one job per binary, the Arch Linux packages, the multi-architecture image, and one job per NuGet package family; and finally publish |
| `cleanup.yml` | Hourly | Removes pre-release releases, tags, container tags, and NuGet versions older than 24 hours |

A build publishes nothing until every test has passed, and its release stays an invisible draft
until every binary, both Arch Linux packages, the multi-architecture image, and every NuGet
package have been uploaded. The pipeline needs a `NINLIL_CI_TOKEN` secret (a Forgejo access token
with repository and package write access) and a `NINLIL_CI_USER` variable (that token's user name)
on the repository.

## Contributing: adding a feature

### Branches

| Branch | Created from | Pushed to directly | Merges into | Through |
|---|---|---|---|---|
| `feature/*` | `develop` | Yes | `develop` | Pull request |
| `hotfix/*` | `main` | Yes | `main` | Pull request |
| `develop` | — | No | `main` | Pull request |
| `main` | — | No | `v<yyyy>.<mm>.<dd>.<increment>` | A release branch created from it |

`main` is merged back down into `develop` after every release. Every push to any of these
branches produces a pre-release build, so a feature branch can be tried out as a binary or image
before it is merged.

### Conventions

Ninlil has a few binding conventions every contribution follows, regardless of which side of the
codebase it touches:

- **Clean-room licensing, no exceptions**: never read, fetch, or port source code from any
  existing DNS/DHCP/ad-blocking project as an implementation guide, even for a feature whose
  publicly documented behavior inspired the work. Every protocol detail (enum values, wire
  formats, algorithm numbers) is sourced from IANA registries and the actual IETF RFC text. See
  [`NOTICE.md`](./NOTICE.md) for the full policy.
- **Every public member gets a doc comment** (`///` in C#, TSDoc in TypeScript) explaining *why*,
  not just what, and a method/function body gets inline comments orienting a reader who drops into
  the middle of it cold.
- **Format before you're done**: `dotnet csharpier format .` for any C# change, `npm run format`
  for any Angular change — both are hard CI gates.
- Follow the [standard verification loop](#standard-verification-loop) above before calling
  anything finished.

### Backend: adding a new entity end-to-end

A recent, complete, real worked example to read start-to-finish is the static IP/CIDR-to-client
identity pinning feature — a small, self-contained, top-level entity with no owning parent, which
makes it a clean template for the whole pattern:

- `src/dotnet/src/Ninlil.Dns.Data.Abstractions/Entity/DnsClientStaticAddressMappingEntity.cs` —
  the storage-agnostic entity.
- `src/dotnet/src/Ninlil.Dns.Data.Abstractions/Repository/IDnsClientStaticAddressMappingRepository.cs`
  — the repository interface, extending the shared `IRepository<TEntity>` with real, pushed-down
  `SearchAsync`/`CountAsync` pagination (translated `ORDER BY`/`OFFSET`/`LIMIT`, never an
  in-memory `Skip`/`Take` over a fully materialized list).
- `src/dotnet/src/Ninlil.Dns.Data.EntityFrameworkCore/Configuration/DnsClientStaticAddressMappingConfiguration.cs`
  and `.../Repository/DnsClientStaticAddressMappingRepository.cs` — the EF Core entity
  configuration and repository implementation, plus one migration per relational provider (see
  below).
- `src/dotnet/src/Ninlil.Dns.Data.MongoDb/Repository/DnsClientStaticAddressMappingMongoRepository.cs`
  — the MongoDB implementation; schemaless, so this is typically just a class-map registration
  plus the same `SearchAsync`/`CountAsync` shape, no migration needed.
- `src/dotnet/src/Ninlil.Web/Controllers/DnsClientStaticAddressMappingsController.cs` — the REST
  controller, along with `src/dotnet/src/Ninlil.Web/Models/DnsClientStaticAddressMappingModels.cs`
  (request/response shapes) and `src/dotnet/src/Ninlil.Web/Examples/ClientStaticAddressMappings/`
  (Swashbuckle example providers for Swagger).
- `src/dotnet/tests/Ninlil.Web.Tests/ClientStaticAddressMappingCrudTests.cs` — real
  `WebApplicationFactory<Program>` integration tests covering the full CRUD/pagination surface.

The general shape, in order:

1. **Entity** in the relevant `*.Data.Abstractions` project, inheriting `EntityBase` or
   `AuditableEntityBase`, with a doc comment on the class and every property.
2. **Repository interface** in the same project, extending `IRepository<TEntity>` with whatever
   real query shapes the feature needs — a `SearchAsync`/`CountAsync` pair pushed down to the
   database is the established convention for anything that could return more than a handful of
   rows — never a full `ListAsync()` sliced with `Skip`/`Take` in memory.
3. **EF Core implementation**: an `IEntityTypeConfiguration<TEntity>` class under
   `Ninlil.Dns.Data.EntityFrameworkCore/Configuration/`, a repository implementation under
   `.../Repository/`, and a migration generated **once per relational provider** — from inside
   each provider's own migrations project (each one carries its own
   `IDesignTimeDbContextFactory<NinlilDnsContext>`, so no `--startup-project` flag is needed):

   ```bash
   cd src/dotnet/src/Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite
   dotnet ef migrations add AddYourNewEntity

   cd ../Ninlil.Dns.Data.EntityFrameworkCore.Migrations.PostgreSql
   dotnet ef migrations add AddYourNewEntity

   cd ../Ninlil.Dns.Data.EntityFrameworkCore.Migrations.MySql
   dotnet ef migrations add AddYourNewEntity

   cd ../Ninlil.Dns.Data.EntityFrameworkCore.Migrations.SqlServer
   dotnet ef migrations add AddYourNewEntity
   ```

   All four providers share one model (`NinlilDnsContext`, covering DNS *and* DHCP entities
   alike), but **each provider's migration history must live in its own separate assembly** —
   putting two providers' migrations in one assembly is a real, `Migrate()`-time-only runtime bug
   that neither `dotnet build` nor `migrations add` itself will catch.
4. **MongoDB implementation**: a repository under `Ninlil.Dns.Data.MongoDb/Repository/` against
   the shared `NinlilDnsMongoContext` — no migration step, since a schemaless store just needs
   `AutoMap()` to pick up the new properties (skip an explicit `MapIdProperty` on `Id`, which
   throws; `AutoMap()` already handles it). Verify against a real `mongo:7` container via a
   throwaway scratchpad console app, not just "matches the EF Core shape on paper."
5. **REST controller** under `Ninlil.Web/Controllers/`, with request/response model classes and
   Swashbuckle example providers, using the same `SearchAsync`/`CountAsync` pagination pushed all
   the way through to the HTTP layer.
6. **DI wiring**: one line in the owning project's own `ServiceCollectionXyzExtension`, following
   the existing `Configure<TOptions>`/repository-registration pattern already established there.
7. **xUnit tests**: repository tests in the matching `*.Tests` project, and a
   `WebApplicationFactory<Program>`-based integration test in `Ninlil.Web.Tests` covering full
   CRUD and pagination — mirror an existing test class's shape rather than inventing a new one.

### Frontend: adding a feature page

The matching Angular feature for the same example lives at
`src/angular/src/app/features/client-static-address-mappings/`, with a list component
(`static-address-mapping-list/`) and a create/edit form component
(`static-address-mapping-form/`) as siblings under one feature folder — the established
"list + form, each its own subfolder" shape for a CRUD-style feature page.

The general shape:

1. **Regenerate the typed API client** against your new backend endpoint: run the backend
   locally, then from `src/angular/`, `npm run fetch:openapi` (re-fetches the committed
   `openapi/swagger.json` snapshot) followed by `npm run generate:api` (regenerates
   `src/app/api-client/`, which is 100% generated and never hand-edited) — or just `npm run build`,
   since `generate:api` also runs automatically as a `prebuild` step against whatever snapshot is
   already committed.
2. **Feature folder** under `src/app/features/<feature-name>/`, standalone components only (no
   `NgModule`), signals-based state, zoneless-compatible (no dependency on `zone.js`). Kebab-case
   file names with a type suffix (`*.component.ts`, `*.service.ts`, `*.guard.ts`), one exported
   artifact per file, no hand-written barrel `index.ts`.
3. **CoreUI components** for the actual UI — this app's own CoreUI-based visual language, themed
   via the shared token/palette system in `src/styles/`, never a copy of another product's visual
   design (see the clean-room note above — this extends even to a narrow, function-only exception
   for *organizational* UI/UX research; visual design is always independently built).
4. **Routing/nav wiring**: a route entry in the relevant routing module and a nav item in the
   admin shell's own navigation config, following an existing sibling feature's entry as a
   template.
5. **TSDoc** on every exported service/guard/component, matching the C# XML-doc convention's depth
   — explain *why*, not just what.
6. **Tests**: a Jasmine/Karma `.spec.ts` per component, following an existing spec's shape (TestBed
   setup, mocked API client, signal-based state assertions).
7. Run `npm run lint` / `npm run format:check` / `npm run build` / `npm test` before considering
   the page done.

## Project layout

```
src/dotnet/src/     .NET solution — DNS/DHCP engines, storage, identity, REST host, one project per concern
src/dotnet/tests/   xUnit test projects, one per src/ project
src/angular/         Angular 22 admin console — a separate npm workspace, sibling to src/dotnet/
Ninlil.slnx          Solution file (the newer XML-based .slnx format)
NOTICE.md             Clean-room statement and third-party dependency licenses
ENVIRONMENT.md         Complete, generated reference of every NINLIL_ environment variable
Dockerfile              Every build output: the container image, the binaries, and the test stages
docker/                 Build-time helper scripts for the container image
docker-compose.yml      Example deployment: Ninlil + PostgreSQL, with a host-networking DHCP/RA variant
.forgejo/               CI/CD workflows and the scripts they run (versioning, releases, cleanup)
src/archlinux/          The ninlil-bin and ninlil (from source) Arch Linux packages
src/script/             Operational scripts: database helpers and a systemd unit
src/terraform/          Reusable Terraform modules for packaging and publishing
```

The reasoning behind a specific design choice lives with the code: every class and public member
carries a doc comment explaining *why* it is shaped the way it is.

## License

MIT — see [`LICENSE`](./LICENSE). See [`NOTICE.md`](./NOTICE.md) for the clean-room statement and
third-party dependency license table.
