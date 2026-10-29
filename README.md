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
- **DNSSEC**: RSA/ECDSA/EdDSA signing and validation (the SHA-1, DSA, MD5 and GOST algorithms
  are validated but never signed with, per RFC 8624), NSEC/NSEC3 (with RFC 5155 opt-out),
  automated proactive re-signing, and a full RFC 6781 automated key-rollover state machine
  (Pre-Publish for ZSKs, Double-Signature for KSKs, with automatic DS cutover when the parent zone
  is also hosted locally). RFC 5011 automated root trust-anchor rollover on the recursive side: the first check runs a
  minute after start, later ones follow the RFC's refresh rule (at most the configured interval),
  and a root key counts as revoked only if it signed its own revocation.
  The validator builds the chain of trust from the root trust anchor and keeps the zones it has
  proven in the `dnssec` cache (listed, and flushable, with the other caches). With a trust anchor
  configured, a DS or DNSKEY lookup that fails or does not verify makes the answer bogus, so the
  client gets SERVFAIL unless it set the CD bit; a network that blocks DNSSEC data needs
  `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_DNSSEC_VALIDATION=false`. A recursive answer carries the AD
  bit only when it validated and the client set DO or AD; a DO client also gets the RRSIGs, and the
  NSEC or NSEC3 records that prove a wildcard answer, in the authority section. Cached answers
  never outlive their signatures. With DNS64 on, the A answer is validated before AAAA is
  synthesized: a bogus one gives SERVFAIL (NODATA if the client set CD), and a client that set both
  DO and CD gets no synthesis. A zone that fails validation is logged as a warning, at most once
  per zone per hour. Forwarded answers are validated the same way: the forwarder asks its upstream
  with DO and CD set, checks the answer itself, and builds the reply for each client, so AD is
  set only for an answer this server validated (never copied from the upstream) and only for a
  client that set DO or AD, a client without DO gets no RRSIG, NSEC or NSEC3 records, and no
  upstream EDNS option reaches a client. A bogus forwarded answer makes the forwarder count as
  failed unless the client set CD. The DS and DNSKEY records for the chain of trust are asked of the
  same forwarders, in the same order and with the same per-forwarder timeout, and cached, so a
  network that only reaches its forwarders still validates without iterating to the internet. A
  failed DS or DNSKEY lookup under a trust anchor makes the answer bogus. A cached forwarded answer
  never outlives its shortest RRSIG Original TTL or its earliest RRSIG expiration.
  **Private zones.** A zone that is served only on your network (`corp.internal`, `home.arpa`, a
  private reverse zone such as `10.in-addr.arpa`) has no chain of trust from the public root: the
  root or `arpa` proves, with a signed answer, that it does not exist, and validation would end in
  SERVFAIL. Two settings keep such names working with validation on. First, the **Insecure
  domains** list (a negative trust anchor list) on the Recursive Resolver settings page, in the
  REST API (`GET` and `PATCH /api/recursive-settings`, field `insecureDomains`) and in the
  configuration file (`DnsServer:Recursive:InsecureDomains` with
  `DnsServer:Recursive:UseCustomInsecureDomains` set to `true`): a query for a name at or below an
  entry is not validated, so it is answered as insecure, without the AD bit and without a DNSSEC
  SERVFAIL, whether it was forwarded or resolved recursively. The list starts as the built-in one:
  `home.arpa` (RFC 8375), `internal` (reserved by ICANN for private use) and the locally-served
  zones of RFC 6303 and RFC 7793 (the RFC 1918 reverse zones, `127.in-addr.arpa`,
  `254.169.in-addr.arpa`, the documentation and shared address space ranges, and the `ip6.arpa`
  zones for `::`, `::1`, `fd00::/8`, `fe80::/10` and `2001:db8::/32`). It is fully editable: saving
  a list replaces the built-in one entirely (an empty list exempts nothing), and "Restore
  defaults" (`insecureDomains: null` in the API) goes back to the built-in list, which then
  follows later releases. A change applies to the next query; the forwarded-answer cache is
  flushed, because it remembers whether an answer was validated. Only the queried name counts, so a
  CNAME from a public name into a listed domain is still validated. Add your own private zones, for
  example `corp.example`, to the list. Second, each **conditional forwarder zone** has a
  *Validate answers with DNSSEC* switch (`validateDnssec` in the zone API), **off by default** and
  off for every zone that existed before it was added: answers for the zone are forwarded without
  validation and never carry AD. Turn it on for a forwarded zone that is signed under the public
  root. It works only while validation is on globally, and a name on the Insecure domains list is
  still not validated. The global forwarders follow the global setting alone.
- **Every major DNS transport**: plain UDP/TCP (on by default — this is a DNS server, after all),
  DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC, and DNSCrypt v2. Every transport beyond plain DNS is
  off by default; see [Enabling additional transports](#enabling-additional-transports). Each one,
  and the web listener, can be limited to chosen IP addresses, to keep it off a WAN interface or
  share a host with another DNS service.
- **General upstream forwarding** with health-checked failover, independent of the recursive
  resolver, supporting the full `sdns://` DNSCrypt stamp grammar, and a TTL-honouring answer cache
  (RFC 2308 negative caching, serve-stale) that operators can inspect and flush from the console.
- **TSIG (RFC 8945)** signed transfers and dynamic updates with HMAC-SHA1, -SHA256 (the default),
  -SHA384 or -SHA512 keys, shown and stored with lowercase names (BIND lowercases them on the
  wire; keys saved earlier with capitals are lowercased at startup, and two names that differ
  only in case are left as they are with a warning in the log). The keys that clustering creates
  for its peers are labelled as cluster keys: they can be disabled but not renamed or deleted,
  never sign or verify zone transfers, and go away when the peer is removed. A key is
  not server-wide: each zone lists the keys allowed to update it and the keys allowed to transfer
  it (AXFR and IXFR), and an empty list accepts no key, so a verified key that is not listed gets
  a signed REFUSED. Bind keys on the zone form after creating them; a zone you upgrade from an
  earlier setup where any key worked needs its lists filled in. Also an **ACME (RFC 8555) DNS-01
  client** for automated certificate issuance against zones this server hosts itself, with
  optional automated CAA record and PTR/reverse-zone management (including RFC 2317 classless
  delegation).
- **Cloud DNS zone sync**: Cloudflare, AWS Route 53, and DigitalOcean, kept in sync alongside
  classic AXFR/IXFR zones, covering A/AAAA/CNAME/MX/NS/TXT/SRV/CAA records.
  A CloudPrimary zone also accepts RFC 2136 dynamic updates, with the same rules as a primary
  zone: the zone opts in, the update is TSIG-signed and the key is listed on the zone. An accepted
  update is stored locally and answered at once, then pushed to the provider within moments. If
  the push fails, the zone's regular sync retries it and a cloud-sync-failed event is raised;
  records added directly at the provider are removed by the next push. An update that adds a
  record type the providers cannot hold (anything but A, AAAA, CNAME, MX, NS, TXT, SRV and CAA)
  is refused as a whole, so a CloudPrimary reverse zone cannot take PTR updates; deletions are
  allowed. The provider keeps its own SOA and apex NS, so an update to them changes only what
  this server serves. An update to a zone with no SOA record gets SERVFAIL until you add one.
- **Filtering and parental controls**: block/allow-list filtering with subscribed external
  blocklists (hosts-file, plain-domain or adblock `||domain^`/`@@` syntax), CNAME-cloaking
  protection, a curated "blocked services" catalog, Safe Search enforcement, per-client and
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
| Windows x86-64 | `ninlil-windows-amd64.exe` |

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

#### Windows

The Windows executable is a plain console program that also runs as a Windows service. It is
**unsigned**, so Windows SmartScreen warns the first time you start it. Verify the download against
`SHA256SUMS` (in PowerShell: `(Get-FileHash .\ninlil-windows-amd64.exe).Hash` must equal the
`ninlil-windows-amd64.exe` line there, ignoring case) before you accept the warning. The DNS server,
REST API, and admin console work fully; the DHCP, Router Advertisement, and passive device-presence
features rely on Linux kernel interfaces and are unavailable, as on macOS.

Try it from a console first (an administrator PowerShell, because the defaults use ports 53, 80, and
443; or override the ports, see "Ports" below):

1. Create the folder and put the file in it: `New-Item -ItemType Directory C:\Ninlil`, then move
   `ninlil-windows-amd64.exe` there.
2. Remove the download mark: `Unblock-File C:\Ninlil\ninlil-windows-amd64.exe`.
3. Run `C:\Ninlil\ninlil-windows-amd64.exe`, wait for the log lines, and open `http://localhost/`.
   Press Ctrl+C to stop it. It created `C:\Ninlil\data\` next to the executable.

To run it as a service that starts with Windows (all in an administrator PowerShell):

1. Create the data folder: `New-Item -ItemType Directory -Force C:\Ninlil\data`. If you already ran the
   executable from a console, keep that folder: it holds your database and keys.
2. Create the service. The space after each `=` is required by `sc.exe`:

   ```powershell
   sc.exe create Ninlil binPath= "C:\Ninlil\ninlil-windows-amd64.exe" start= auto DisplayName= "Ninlil DNS server"
   ```

3. Run it as a virtual service account instead of LocalSystem, so it needs no password and owns
   nothing but its data folder:

   ```powershell
   sc.exe config Ninlil obj= "NT SERVICE\Ninlil"
   icacls C:\Ninlil\data /grant "NT SERVICE\Ninlil:(OI)(CI)M"
   ```

4. Let Windows restart it after a crash, open the ports you use, and start it:

   ```powershell
   sc.exe failure Ninlil reset= 86400 actions= restart/5000/restart/5000/restart/60000
   New-NetFirewallRule -DisplayName "Ninlil DNS" -Direction Inbound -Protocol UDP -LocalPort 53 -Action Allow
   New-NetFirewallRule -DisplayName "Ninlil DNS (TCP)" -Direction Inbound -Protocol TCP -LocalPort 53 -Action Allow
   New-NetFirewallRule -DisplayName "Ninlil web" -Direction Inbound -Protocol TCP -LocalPort 80,443 -Action Allow
   Start-Service Ninlil
   ```

5. Check it with `Get-Service Ninlil` and `Invoke-WebRequest http://localhost/status`. Service
   settings go in the service's environment: `Set-ItemProperty HKLM:\SYSTEM\CurrentControlSet\Services\Ninlil
   -Name Environment -Type MultiString -Value "NINLIL_DNS_SERVER_WEB_HTTP_PORT=8080"` (one `NAME=value`
   line per setting; see [`ENVIRONMENT.md`](./ENVIRONMENT.md)), then `Restart-Service Ninlil`.

To remove it: `Stop-Service Ninlil; sc.exe delete Ninlil`. Your `C:\Ninlil\data` folder stays.
Updating means stopping the service, replacing the `.exe`, and starting it again.

The Data Protection key ring and the JWT signing key are stored as plain files in `data\` (they are
not tied to a Windows account), so changing the service account later only needs the new account to
have modify rights on that folder. Protect the folder with its permissions.

### Arch Linux

Every release carries installable `ninlil` packages for `x86_64` and `aarch64`:

```bash
sudo pacman -U https://git.lantea.one/Lantea.One/ninlil/releases/download/<tag>/ninlil-<pkgver>-1-x86_64.pkg.tar.zst
sudo systemctl enable --now ninlil
```

[`src/archlinux/`](./src/archlinux/) also has the AUR PKGBUILDs that build from source:
`ninlil-git-stable` (the newest release branch) and `ninlil-git-next` (the latest `develop`
commit). All three install a `ninlil` systemd service running as an unprivileged user with its
state in `/var/lib/ninlil`.

### Debian, Ubuntu, Fedora, RHEL and Rocky Linux

Every release carries a `.deb` (Debian, Ubuntu) and an `.rpm` (Fedora, RHEL, Rocky Linux,
AlmaLinux) for `amd64`/`x86_64` and `arm64`/`aarch64`. They install the same layout as the Arch
Linux package: the server in `/usr/lib/ninlil/`, a `ninlil` systemd service that runs as an
unprivileged `ninlil` user, all state in `/var/lib/ninlil`, and the settings in
`/etc/ninlil/ninlil.env`. Installing never starts the service; you do that in step 4.

Each package is install-tested on every build, on `amd64`, on Debian 12, Ubuntu 22.04, Ubuntu 24.04,
Rocky Linux 9 and Fedora 41. The `arm64` packages are built from the same files but not run by the
pipeline. The package manager installs the libraries the server needs (the C++ runtime, OpenSSL and
ICU) from your distribution. DNS-over-QUIC needs `libmsquic`, which the package only suggests; see
[Microsoft's package repository](https://learn.microsoft.com/en-us/linux/packages) if you want it.

1. Open the [releases page](https://git.lantea.one/Lantea.One/ninlil/releases), pick a release
   and copy the link of the package for your distribution and CPU (`uname -m` prints `x86_64` or
   `aarch64`). The file names are:

   | Distribution | CPU | File |
   |---|---|---|
   | Debian, Ubuntu | x86-64 | `ninlil_<version>_amd64.deb` |
   | Debian, Ubuntu | ARM64 | `ninlil_<version>_arm64.deb` |
   | Fedora, RHEL, Rocky Linux, AlmaLinux | x86-64 | `ninlil-<version>-1.x86_64.rpm` |
   | Fedora, RHEL, Rocky Linux, AlmaLinux | ARM64 | `ninlil-<version>-1.aarch64.rpm` |

   `<version>` is the date version, such as `2026.10.09.1`. A branch build is a pre-release and its
   version carries the branch and commit (`2026.10.09.3~develop.a1b2c3d` for a Deb,
   `2026.10.09.3-0.develop.a1b2c3d` for an RPM); it sorts below the release it precedes.

2. Download the package and the checksum list, then check the download. Replace `<tag>` with the
   release's tag and the file name with yours:

   ```bash
   curl -LO https://git.lantea.one/Lantea.One/ninlil/releases/download/<tag>/ninlil_<version>_amd64.deb
   curl -LO https://git.lantea.one/Lantea.One/ninlil/releases/download/<tag>/SHA256SUMS
   sha256sum --check --ignore-missing SHA256SUMS
   ```

   The last command must print `OK` for your file. The packages are not signed; the checksums
   are your check that the file is the one the pipeline built.

3. Install it. The `./` in front of the file name is required, it tells the package manager to use
   the local file:

   ```bash
   sudo apt install ./ninlil_<version>_amd64.deb        # Debian, Ubuntu
   sudo dnf install ./ninlil-<version>-1.x86_64.rpm     # Fedora, RHEL, Rocky Linux, AlmaLinux
   ```

4. Start it now and at every boot, then look at the log for the one-time administrator password:

   ```bash
   sudo systemctl enable --now ninlil
   sudo journalctl -u ninlil
   ```

   Open `http://<this host>/` and sign in as `admin` (see [First run](#first-run)).
   To choose the password yourself, set `NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD` in
   `/etc/ninlil/ninlil.env` before the first start. That file takes every `NINLIL_*` setting listed in
   [`ENVIRONMENT.md`](./ENVIRONMENT.md); run `sudo systemctl restart ninlil` after editing it.

Day-to-day:

- **Upgrade:** install the newer package the same way (`apt install ./newer.deb`,
  `dnf install ./newer.rpm`), then `sudo systemctl restart ninlil`. Your edits to
  `/etc/ninlil/ninlil.env` and everything in `/var/lib/ninlil` are kept.
- **Remove:** `sudo apt remove ninlil` or `sudo dnf remove ninlil` stops the service and removes the
  program. It keeps `/var/lib/ninlil` (your database, keys and certificates) and the `ninlil` user.
  Your edited settings stay as `/etc/ninlil/ninlil.env` on Debian and Ubuntu, and are saved as
  `/etc/ninlil/ninlil.env.rpmsave` on Fedora and RHEL.
- **Remove everything:** after `sudo apt purge ninlil` (Debian and Ubuntu; it also deletes the
  settings file) or `dnf remove`, delete the data yourself with `sudo rm -r /var/lib/ninlil` and
  the user with `sudo userdel ninlil`. The package never deletes your data.

The package definition is [`src/linux-packages/nfpm.yaml`](./src/linux-packages/nfpm.yaml); the
pipeline builds it with [nFPM](https://nfpm.goreleaser.com/) through
[`linux-packages.sh`](./.forgejo/scripts/linux-packages.sh) and tests it with
[`linux-packages-smoke.sh`](./.forgejo/scripts/linux-packages-smoke.sh). To run that test on your
machine, build the packages with `in-container.sh packages` (it needs a `dist/` folder holding
`ninlil-linux-amd64` and `ninlil-linux-arm64` and the `DEB_VERSION`, `RPM_VERSION` and `RPM_RELEASE`
variables) and run `.forgejo/scripts/linux-packages-smoke.sh dist`.

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
| `5443` UDP/TCP (`DnsServer:Protocol:DnsCrypt:Port`) | DNSCrypt v2 | Off by default |
| `67`/`68` UDP (`DhcpServer:Protocol:Dhcpv4:*Port`) | DHCPv4 | Off by default |
| `547`/`546` UDP (`DhcpServer:Protocol:Dhcpv6:*Port`) | DHCPv6 | Off by default |
| `69` UDP (`DhcpServer:Tftp:Port`) | TFTP (PXE boot delivery) | Off by default |

The web and DNS listeners bind every address unless their `ListenAddresses` setting (for example
`DnsServer:Protocol:Plain:ListenAddresses`) names specific ones; see ENVIRONMENT.md.

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
  "Storage": {
    "Engine": "PostgreSql",
    "ConnectionString": "Host=localhost;Database=ninlil;Username=ninlil;Password=change-me"
  }
}
```

With SQLite and no connection string, Ninlil creates the database file `ninlil-dns.sqlite3` in the
`data` directory. For the other engines, create the database and its account first with the
scripts in [`src/script/database/`](./src/script/database/README.md).

Relational engines have their EF Core migrations applied automatically on startup, after a
snapshot of the existing data, so an upgrade can be undone (see
[Upgrades and migration snapshots](#upgrades-and-migration-snapshots)). MongoDB is schemaless and
needs no migration step. To move an installation to another engine later, see
[Switching the storage engine](#switching-the-storage-engine).

## Operating the database

This section is for the person who runs a Ninlil installation: moving to another database engine,
upgrading safely, and the settings that change how the database behaves. Every setting it names is
listed in [`ENVIRONMENT.md`](./ENVIRONMENT.md#storage-engine). Where a step needs a file or a
setting you have not met yet, the step says where to find it.

Where Ninlil keeps its files depends on how you installed it. This section calls that folder the
**data directory**:

| Installed as | Data directory |
|---|---|
| Linux package (Deb, RPM, Arch) | `/var/lib/ninlil` |
| Docker image | `/app/data` (the volume you mount) |
| Windows service | the `data` folder next to the executable, such as `C:\Ninlil\data` |
| `dotnet run` | the `data` folder next to the built program |

### Switching the storage engine

You can move an installation between SQLite, PostgreSQL, MySQL and SQL Server. MongoDB cannot be
the source or the target of a switch. A switch takes effect at the **next restart**: you ask for
it in the admin console, then restart Ninlil, and the copy runs at that start before anything else
uses the database.

The copy moves everything an administrator configured (zones, records, users, roles, filtering,
clients, DHCP scopes, reservations and leases). It leaves out history that can grow very large:
the query log, the operational log and metrics, the zone change journal, webhook delivery
attempts and the cluster sync tombstones. The Data Protection key ring, the certificates and the
configuration file stay where they are. Ninlil does not delete the old database.

**Before you start**

1. Create the target database and its account. For PostgreSQL, MySQL and SQL Server, the scripts
   in [`src/script/database/`](./src/script/database/README.md) do this.
2. Check that no read replica or cluster setting is in use (the `NINLIL_STORAGE_READ_REPLICA_*`
   and per-engine variables in [Read replicas and database clusters](#read-replicas-and-database-clusters)).
   Ninlil refuses to schedule a switch while one is set. Remove them, switch, then set them up again
   for the new engine.
3. Sign in with an account that holds the `storage:manage` permission (the bootstrap `admin` does).

**Steps**

1. Open **Settings**, then **Storage** (`/settings/storage`).
2. Choose the target engine and fill in its connection fields: a file path for SQLite; host,
   port, database, user name, password and encryption mode for the others. Choose **Schedule
   switch**.
3. Ninlil connects to the target at once. It refuses the request when the target cannot be
   reached, when a newer Ninlil version has already upgraded it, or when the target engine is
   MongoDB. When the target already holds Ninlil data, the page warns you that the copy replaces
   that data.
4. Ninlil saves the request and answers immediately. Nothing is copied yet and Ninlil keeps
   running on the old database. The page shows the pending switch. **Cancel** removes it. The
   request lives in `storage-switch.json` in the data directory, with its connection strings
   encrypted by the Data Protection key ring, so keep the data directory across the restart.
5. Read the note the page shows and follow the case that matches it:
   - *Restart Ninlil to switch.* Ninlil's own configuration file (`appsettings.json`) holds the
     storage settings. Do nothing else.
   - *Set `NINLIL_STORAGE_ENGINE` and `NINLIL_STORAGE_CONNECTION_STRING` ...* Something other than
     that file decides the storage, usually environment variables (Docker, the package's
     `/etc/ninlil/ninlil.env`, a Windows service). Ninlil never overrides those. Set
     `NINLIL_STORAGE_ENGINE` to the target engine and `NINLIL_STORAGE_CONNECTION_STRING` to the
     target's connection string (write it yourself in that engine's format, as in
     [Choosing a storage engine](#choosing-a-storage-engine); the page never shows one).
6. Restart Ninlil (`sudo systemctl restart ninlil`, `docker restart ninlil`, or
   `Restart-Service Ninlil`). At that start Ninlil migrates the target, copies the data, and, in
   the first case, writes the new engine and connection string into `appsettings.json`.
7. Open **Settings**, then **Storage** again. The last-outcome card shows that the copy worked and
   how many rows it moved, or why it failed. It shows the outcome of the latest start only.

**When something goes wrong**

- *The configuration file decided the storage and the copy failed.* Ninlil keeps running on the old
  database, deletes the saved request and shows the failure on the Storage page. Fix the cause and
  schedule the switch again.
- *Your environment variables already point at the target and the copy failed.* Ninlil cannot run
  on an empty target, so the start is refused with exit code 78 (see
  [When the start is refused](#when-the-start-is-refused-exit-code-78)). Point the variables back
  at the old database, or fix the cause, and start again. The saved request stays until you cancel
  it or the copy succeeds.
- *Neither the old nor the new database is the one configured.* Ninlil logs a warning that names
  the three engines, keeps the request, and changes nothing. Cancel the request on the Storage
  page, or set the variables to one of the two databases.

### Upgrades and migration snapshots

A new Ninlil version can change the database layout. For SQLite, PostgreSQL, MySQL and SQL Server,
Ninlil applies those changes itself at start (these are the *migrations*) and takes a **snapshot**
of your data first, so a failed or unwanted upgrade can be undone.

**What happens at every start, in order**

1. Ninlil reads which migrations the database has already applied.
2. If the database applied a migration this version does not know, a newer Ninlil used it. Ninlil
   refuses to start on it (exit code 78). Run the newer version, or restore a snapshot or backup
   taken before that upgrade.
3. If an earlier attempt failed and may have left the database half migrated, Ninlil refuses to
   start until you restore the snapshot of that attempt or set
   `NINLIL_STORAGE_MIGRATION_SAFETY_RETRY_FAILED_MIGRATION=true`.
4. If no migration is pending, startup continues.
5. If migrations are pending and the database already holds data, Ninlil takes a snapshot and logs
   a line that starts with `Took migration snapshot`, naming the snapshot id and where it is. A
   brand-new database gets no snapshot.
6. Ninlil applies the migrations. On success it deletes the oldest snapshots beyond the number to
   keep (3 by default).

**Where a snapshot lives**

A snapshot id is the UTC time it was taken, as `yyyyMMddHHmmss` (for example `20261009142650`).

| Engine | Where the snapshot is | How to list them |
|---|---|---|
| SQLite | A file `migration-snapshots/<database file name>-<id>.sqlite3` and a `.json` manifest next to it, in the folder that holds the database file (by default `<data directory>/migration-snapshots/ninlil-dns-<id>.sqlite3`). Both files are readable by the owner only. | `ls <data directory>/migration-snapshots` |
| PostgreSQL | A schema `ninlil_snapshot_<id>` inside the Ninlil database, with a copy of every table and a manifest table `__NinlilSnapshot` | `psql -d ninlil -c '\dn ninlil_snapshot_*'` |
| SQL Server | A schema `ninlil_snapshot_<id>` inside the Ninlil database, with a copy of every table and a manifest table `__NinlilSnapshot` | `SELECT name FROM sys.schemas WHERE name LIKE 'ninlil_snapshot_%'` |
| MySQL | Tables named `nsnap_<id>__<table>` next to the real tables, and a manifest table `nsnap_<id>__manifest` | `SHOW TABLES LIKE 'nsnap\_%\_\_manifest'` |
| MongoDB | None. See [MongoDB](#mongodb) below. | |

A snapshot copies every table, including the query log, so it **roughly doubles the size of the
database** until Ninlil prunes it. On a server engine it needs no extra privilege: the account
from [`src/script/database/`](./src/script/database/README.md) already may create schemas and
tables in its own database, which the migrations need anyway. On PostgreSQL, MySQL and SQL Server
the snapshot is part of the database, so replicas and your own database backups carry it too.

If the snapshot cannot be taken (no disk space, no permission), Ninlil leaves the database
unchanged and refuses to start with a message that says why. Fix the cause, or set
`NINLIL_STORAGE_MIGRATION_SAFETY_TAKE_SNAPSHOT=false` to migrate without a snapshot (Ninlil then
logs a warning). A SQLite database held in memory has no file to copy and gets no snapshot.

**Settings**

| Variable | Default | Meaning |
|---|---|---|
| `NINLIL_STORAGE_MIGRATION_SAFETY_TAKE_SNAPSHOT` | `true` | Take a snapshot before migrating a database that holds data. |
| `NINLIL_STORAGE_MIGRATION_SAFETY_SNAPSHOTS_TO_KEEP` | `3` | Finished snapshots kept after a successful migration. The oldest go first. At least 1. |
| `NINLIL_STORAGE_MIGRATION_SAFETY_RETRY_FAILED_MIGRATION` | `false` | Try again on the database as it is after a failure that may have left it half migrated. |
| `NINLIL_STORAGE_MIGRATION_SAFETY_RESTORE_SNAPSHOT` | empty | The id of a snapshot to restore at the next start. |

**Restoring a snapshot**

Ninlil restores snapshots itself, on every relational engine, through one setting. The restore
drops the Ninlil tables, rebuilds the layout the snapshot had and copies the rows back, so
**everything written after the snapshot is lost**. Choose the case that fits.

*Case A: go back to the previous Ninlil version* (the new version fails or misbehaves).

1. Stop Ninlil.
2. Find the snapshot id: it is in the log line `Took migration snapshot` of the upgrade start and
   in the refusal message, or use the listing in the table above. Take the newest one.
3. Install the previous Ninlil version. It must be a version that has the
   `NINLIL_STORAGE_MIGRATION_SAFETY_*` settings; if it is older than that, restore a database
   backup you took before the upgrade instead.
4. Set `NINLIL_STORAGE_MIGRATION_SAFETY_RESTORE_SNAPSHOT=<id>` (or start with the command-line
   argument `--Storage:MigrationSafety:RestoreSnapshot=<id>`) and start Ninlil.
5. Read the log. Success looks like `Snapshot <id> is restored. Remove the setting ...`.
6. Remove the setting and restart. If you forget, the next start logs a warning that the snapshot
   was already restored and does not restore it again.

*Case B: stay on the new version and try the upgrade again* (you fixed the cause of a failure).
Follow the same steps without step 3. After the restore, the same start applies the pending
migrations again, with a new snapshot.

A restore is not atomic, but you can repeat it: if it is interrupted, start again with the same
setting. A snapshot made by one engine cannot be restored into another.

*SQLite by hand.* The refusal message for SQLite also gives a manual way: stop Ninlil, copy the
snapshot file over the database file, delete the `-wal` and `-shm` files next to the database if
they exist, delete the snapshot's `.json` manifest, and start Ninlil.

*Deleting a snapshot by hand.* Prefer letting Ninlil prune snapshots after the next successful
migration. To free the space sooner, delete the SQLite files, or on PostgreSQL run
`DROP SCHEMA ninlil_snapshot_<id> CASCADE;`, on SQL Server drop the schema's tables and then
the schema, and on MySQL drop every table that starts with `nsnap_<id>__`.

#### When the start is refused (exit code 78)

Whenever Ninlil must not run on the database it finds, or the storage settings contradict each
other, it logs a critical entry that begins with `Startup refused:`, explains what to do, and exits with
**exit code 78**. The causes are:

| Message mentions | Meaning | What to do |
|---|---|---|
| The database was upgraded by a newer Ninlil version | A newer version applied migrations this one does not know. | Start the newer version, or restore a snapshot or backup from before that upgrade (Case A above). |
| Applying migrations ... failed and was rolled back | PostgreSQL, SQL Server and SQLite undo a failed migration completely. The database is unchanged. | Fix the cause named in the message and start again, or start the previous version. |
| Applying migrations ... failed and was not rolled back, or An earlier attempt ... failed | MySQL cannot undo schema changes, so a failure can leave the database half migrated. | Restore the snapshot the message names (Case A or B). Only if you know the half-migrated state is safe, set `NINLIL_STORAGE_MIGRATION_SAFETY_RETRY_FAILED_MIGRATION=true`; that reuses the snapshot. |
| The snapshot before the migration could not be taken | Disk space or permissions. | Fix them, or set `NINLIL_STORAGE_MIGRATION_SAFETY_TAKE_SNAPSHOT=false`. |
| There is no snapshot ... to restore | The restore setting names an id that does not exist. | Correct or remove the setting; the message lists the ids that exist. |
| The MySQL server ... has read_only set | The connection string reaches a replica. | Point it at the writable primary. |
| A setting under `Storage:` is wrong | A storage cluster or replica setting is out of range or contradicts the connection string. | Fix the setting the message names. |
| MongoDB: unknown schema step, or a backfill failed | See [MongoDB](#mongodb). | |

To see the message: `sudo journalctl -u ninlil -e` (Linux packages), `docker logs ninlil`, or the
console output of `dotnet run`.

The systemd units that ship with Ninlil (the Deb, RPM and Arch packages and
[`src/script/systemd/ninlil-dns.service`](./src/script/systemd/ninlil-dns.service)) set
`RestartPreventExitStatus=78`, so systemd stops instead of retrying a start that cannot succeed.
`systemctl status ninlil` then shows `status=78`. After you fix the cause, run
`sudo systemctl restart ninlil`. If you write your own unit, add the same line. Docker restart
policies such as `unless-stopped` (as in the example `docker-compose.yml`) and the Windows
service recovery actions restart the program whatever its exit code. The refusal then repeats on
every try and does not change the database further, so read the log and fix the cause.

#### MongoDB

MongoDB has no schema migrations and no snapshots. Ninlil keeps a list of the one-off data
changes it has made in the `NinlilDnsSchemaSteps` collection and, like the relational engines,
refuses to start (exit code 78) on a database that records a step it does not know, which means
a newer Ninlil version has used it. If a data change fails, the start is refused with a message
that says it is safe to retry.

Because there is no snapshot, **take a backup before upgrading**: `mongodump --uri "<connection
string>"`, and optionally a backup file from **Backup & Restore** in the admin console (that
backup leaves out the query log and other history, see
[Switching the storage engine](#switching-the-storage-engine)).

**Run MongoDB as a replica set.** Saving several documents at once (a zone with its records, a user
with its roles, a restore) is only all-or-nothing on a replica set. A single-node replica set is
enough. On a standalone server Ninlil logs this warning at every start: *MongoDB is a standalone
server, so a save that writes several documents is not atomic; run a replica set (a single-node
one is enough) for all-or-nothing saves.* To turn a standalone `mongod` into a single-node replica
set:

1. Stop `mongod` and start it again with `--replSet rs0` (or `replication.replSetName: rs0` in
   its configuration file).
2. Run this once in `mongosh`, with a host name that the machine running Ninlil can reach:
   `rs.initiate({ _id: "rs0", members: [{ _id: 0, host: "mongo1:27017" }] })`
3. Put the replica set name in Ninlil's connection string, for example
   `mongodb://ninlil:<password>@mongo1:27017/ninlil?replicaSet=rs0`. Ninlil then also asks the
   server to acknowledge writes from a majority with the journal (`w=majority`, `journal=true`)
   unless the string already says otherwise.
4. Restart Ninlil. The warning is gone.

Two operations are not atomic even on a replica set, because they can be larger than MongoDB's
transaction limits: refreshing a filter list (a failed refresh is simply repeated by the next one)
and restoring a backup file.

A MongoDB server that holds Ninlil's indexes keeps about 150 files open per database (one for each
collection and each index). The usual default limit of 1024 open files is too low. Raise it well
above that: `ulimit -n 64000` before starting `mongod`, `LimitNOFILE=64000` in its systemd unit,
or `docker run --ulimit nofile=64000:64000 ...` for the container. Without it, `mongod` can abort
with `Too many open files`.

### Read replicas and database clusters

For a large installation you can add a **read replica**: a second database server that holds a
copy of the primary and answers some of Ninlil's reads. Nothing changes until you configure one.
All writes, startup work and everything that must see the newest data always use the primary.
Read replicas work with PostgreSQL, MySQL, SQL Server and MongoDB, not with SQLite.

Ninlil does not set up replication. You create the replica with your database's own tools. Ninlil
only needs to know where to read.

**What goes to a replica, and what never does**

| Goes to an eligible replica | Always uses the primary |
|---|---|
| The admin console's list, detail and statistics pages (zones, records, users, roles, clients, DHCP, filtering, forwarders, webhooks, the query log search and so on) | Startup, migrations, snapshots and restores |
| The in-memory DNS caches, when a refresh is only due to age (see below) | Every write, and every read that is followed by a write |
| The cluster, forwarder and webhook health checks, and the query counts a cluster peer asks for | Sign-in, sessions, two-factor, API-token checks and the rest of `/api/auth` |
| | DHCP packet handling |
| | Backup export |
| | The zone, signed-zone, access-rule and TSIG key lookups of the DNS server |

**Set up a replica (any engine)**

1. Create the replica with the database's own replication so that it holds the same database
   (name, tables and accounts) as the primary. Check that it is read-only.
2. Make sure the Ninlil account exists on the replica. PostgreSQL, MySQL and MongoDB copy
   accounts with the data. A SQL Server login belongs to the server, not to the database, so
   create it on every replica instance with the same SID as on the primary. Add the extra grants
   in the per-engine section below.
3. Set the variables for your engine (examples below). Put the replica's connection string in
   `NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING` (for MongoDB use
   `NINLIL_STORAGE_MONGODB_READ_PREFERENCE` instead).
4. Restart Ninlil. A setting that contradicts another setting or the connection string stops the
   start with exit code 78 and a message that names both
   (see [When the start is refused](#when-the-start-is-refused-exit-code-78)).
5. Find this line in the log. It never contains a user name, password or database name:

   ```text
   Storage: PostgreSql, primary db1:5432, read replica r1:5432, r2:5433; a user who saved reads the primary for 10 s, a replica more than 5 s behind gets no reads.
   ```

6. Open **Settings**, then **Storage**. The **Primary and read replica** card shows the replica as
   *Eligible* once its first check passes (within `NINLIL_STORAGE_READ_REPLICA_LAG_CHECK_INTERVAL_SECONDS`,
   5 seconds by default), together with its lag and how many database scopes went to each side.
   The same data is in `GET /api/storage` (the `primary` and `readReplica` objects).

**PostgreSQL**

Use a streaming-replication hot standby (or several). List every host that can be the primary in
the primary string, and every standby in the read string:

```bash
NINLIL_STORAGE_ENGINE=PostgreSql
NINLIL_STORAGE_CONNECTION_STRING="Host=db1.example,db2.example;Database=ninlil;Username=ninlil;Password=change-me"
NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING="Host=standby1.example,standby2.example;Database=ninlil;Username=ninlil;Password=change-me"
```

- A primary string with several hosts gets `Target Session Attributes=read-write` from Ninlil, so
  after a failover the next new connection finds the new primary.
- A read string with several hosts gets `Target Session Attributes=prefer-standby` and
  `Load Balance Hosts=true`: reads spread over the standbys and fall back to the primary when no
  standby is up. A read string with one host is used as written.
- The account needs no extra grant.
- The optional variables `NINLIL_STORAGE_POSTGRESQL_*` change these defaults.

**MySQL**

Put the cluster's router or proxy in front of the servers, so that the primary string reaches the
one writable server and the read string reaches the replicas (the ports below are the defaults of
MySQL Router):

```bash
NINLIL_STORAGE_ENGINE=MySql
NINLIL_STORAGE_CONNECTION_STRING="Server=router.example;Port=6446;Database=ninlil;User=ninlil;Password=change-me"
NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING="Server=router.example;Port=6447;Database=ninlil;User=ninlil;Password=change-me"
```

- The primary must be writable (`read_only=0`). Ninlil checks this at every start and refuses to
  start on a read-only server; the **Storage** health component reports Unhealthy if the primary
  becomes read-only later.
- A primary string with several hosts is only accepted in `FailOver` mode, and only behind an
  external failover (InnoDB Cluster, Orchestrator) that leaves exactly one host writable. The
  driver cannot tell a writable server from a read-only one.
- A read string with several hosts uses `RoundRobin` unless `NINLIL_STORAGE_MYSQL_REPLICA_LOAD_BALANCE`
  says otherwise.
- **Extra grant:** Ninlil measures the lag with `SHOW REPLICA STATUS`, which needs the
  `REPLICATION CLIENT` privilege. The account from `src/script/database/mysql/init.sql` does not
  have it, so run this once as an administrator on the primary (account changes replicate; if
  yours do not, run it on each replica):

  ```sql
  GRANT REPLICATION CLIENT ON *.* TO 'ninlil'@'%';
  ```

  Without it the replica's lag is unknown and the replica is never used.
- If replication is stopped on a replica, its lag is unknown and it gets no reads.

**SQL Server**

Use an Always On availability group with a readable secondary and read-only routing. Both strings
name the listener; Ninlil adds `ApplicationIntent=ReadOnly` to the read string, which makes the
listener route it to a secondary:

```bash
NINLIL_STORAGE_ENGINE=SqlServer
NINLIL_STORAGE_CONNECTION_STRING="Server=listener.example;Database=ninlil;User Id=ninlil;Password=Change-Me-1!;Encrypt=True"
NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING="Server=listener.example;Database=ninlil;User Id=ninlil;Password=Change-Me-1!;Encrypt=True"
NINLIL_STORAGE_SQLSERVER_MULTI_SUBNET_FAILOVER=true   # only for a listener that spans subnets
```

- **Extra grant:** Ninlil reads the secondary's lag from the primary with
  `sys.dm_hadr_database_replica_states`, which needs `VIEW SERVER STATE`. The login from
  `src/script/database/sqlserver/init.sql` does not have it, so run this once, connected as an
  administrator, on every instance that can become the primary:

  ```sql
  GRANT VIEW SERVER STATE TO [ninlil];
  ```

  Without it the lag is unknown, the Storage page says so, and the replica is never used.
- Database mirroring (`Failover Partner`) is not supported.

**MongoDB**

Use a replica set. List the members in the connection string. Set the read preference for the
work that tolerates lag; work that needs the newest data always reads the primary:

```bash
NINLIL_STORAGE_ENGINE=MongoDb
NINLIL_STORAGE_CONNECTION_STRING="mongodb://ninlil:change-me@m1.example:27017,m2.example:27017,m3.example:27017/ninlil?replicaSet=rs0"
NINLIL_STORAGE_MONGODB_READ_PREFERENCE=SecondaryPreferred
```

- Ninlil adds `w=majority` and `journal=true` to the string of a replica set unless it already
  says something else (`NINLIL_STORAGE_MONGODB_WRITE_CONCERN` and `..._JOURNAL` change them).
- `NINLIL_STORAGE_MONGODB_MAX_STALENESS_SECONDS` is optional; when set it must be at least 90
  (the driver's minimum). Ninlil's own lag limit applies as well.
- A standalone server has no secondaries, so a read preference other than `Primary` has no effect
  there and the replica is never used.
- **Extra grant:** Ninlil reads the members' optimes with `replSetGetStatus`. If authentication is
  on, the account needs the `clusterMonitor` role:

  ```javascript
  db.getSiblingDB("ninlil").grantRolesToUser("ninlil", [{ role: "clusterMonitor", db: "admin" }])
  ```

  Without it the lag is unknown and the replica is never used. The optimes have a resolution of
  one second, so a current secondary can read as 1 second behind.

**When a replica gets reads**

Ninlil checks every replica every `NINLIL_STORAGE_READ_REPLICA_LAG_CHECK_INTERVAL_SECONDS` (5).
A replica is **eligible** only when its lag is known and no more than
`NINLIL_STORAGE_READ_REPLICA_MAX_LAG_SECONDS` (5), and it has the same database layout as the
primary. Otherwise every read goes to the primary, Ninlil logs why (once per change), and the
**storage** component of `/status` and the Storage page show *Degraded*. Degraded means the
replica is unused and everything still works. A replica starts as not eligible until its first
check passes. After an upgrade that migrates the primary, the replica is held back until
replication brings its layout up to date. If a replica stays unused for more than 5 minutes, the
log gets a warning. A check that fails or times out counts as an unknown lag.
Health checks that read a replica can be a few seconds out of date.

With several hosts in the read string, one check measures whichever host the driver picked, once
per interval.

**Seeing your own changes**

After you save something, your reads go to the primary for
`NINLIL_STORAGE_READ_REPLICA_STICKY_AFTER_WRITE_SECONDS` (10). Your sign-in session and your API
tokens count as the same user. Other users may read a replica that is up to the lag limit behind.
After the window, a replica serves you only if it was within the lag limit at its last check.
Because the lag limit must be lower than the window, you see your own change as long as the last
check is no older than the window minus the lag limit (5 seconds with the defaults). Keep
`NINLIL_STORAGE_READ_REPLICA_LAG_CHECK_INTERVAL_SECONDS` at or below that difference. This is a
bound, not a guarantee.

Ninlil refuses to start when the lag limit is not below the sticky window or is 60 or more,
because 60 seconds is the shortest refresh interval of the in-memory caches (a refresh from a
replica that is that far behind could bring back data older than the cache already holds).

**DNS caches**

The DNS server keeps its working data in memory. The first load and every reload that follows a
change made on this node read the **primary**, so a change you make shows up in DNS answers at
once. A reload that is only due to age (60 seconds for the client directory, filter configuration,
rewrites, client schedules and forwarder zones, 5 minutes for the entries of a filter list, 30
seconds for static address mappings) reads an eligible replica. A change made on **another**
cluster node therefore reaches this node after at most the refresh interval plus the lag limit. The
zone, signed-zone and access-rule caches have no timer and always read the primary.

**Other limits and warnings**

- *Storage switch.* Ninlil refuses to schedule a switch while `NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING`
  or any setting of the engine in use (`NINLIL_STORAGE_POSTGRESQL_*`, `..._MYSQL_*`,
  `..._SQLSERVER_*`, `..._MONGODB_*`) is set. Remove them, switch, then configure replicas for
  the new engine. A switch that was saved before such a setting appeared is kept, with a
  warning, and not applied.
- *Same server.* If the read string names the same hosts (with ports) and database as the primary,
  Ninlil starts and logs a warning that the split gains nothing.
- *Settings of another engine* are ignored with a warning.
- *Snapshots.* The snapshots described in
  [Upgrades and migration snapshots](#upgrades-and-migration-snapshots) are part of the database,
  so replicas hold them too and need the disk space. A restore runs on the primary and the replicas
  follow.

### Behaviour worth knowing

- **SQL statements are logged at Debug level.** At the default level the log shows none. To see
  every statement, add this to `appsettings.json` (or pass
  `--Logging:LogLevel:Microsoft.EntityFrameworkCore.Database.Command=Debug` on the command line):

  ```json
  { "Logging": { "LogLevel": { "Microsoft.EntityFrameworkCore.Database.Command": "Debug" } } }
  ```

  Failed statements are always logged as errors.
- **SQLite runs in WAL mode.** Next to `ninlil-dns.sqlite3` you will see `ninlil-dns.sqlite3-wal`
  and `ninlil-dns.sqlite3-shm` while Ninlil runs. Do not copy only the main file of a running
  installation: use **Backup & Restore**, or stop Ninlil first and copy all three files. Ninlil
  sets `synchronous = NORMAL`, which keeps the database consistent after a crash; a power cut can
  lose the last second of writes. It also runs `PRAGMA optimize` at every start and after the
  query log cleanup deleted rows, so queries on large logs choose good indexes.
- **A DHCP relay sends its replies out the client's link.** The DHCPv4 relay sends replies to
  clients from its `giaddr` address, bound (on Linux) to the interface that owns it, so a
  broadcast reply does not leave on the interface of the default route. The DHCPv6 relay gives a
  client's link-local address the scope of the interface that owns its link-address. When no
  interface holds `giaddr` (DHCPv4) or the link-address (DHCPv6), the relay logs one Warning at
  start and falls back to the routing table's choice. The automated tests cover this on the
  loopback interface only. A relay with two real links is checked by hand with
  `src/script/development/dhcp-relay-two-link-test.sh /path/to/Ninlil.Web` (as root; it needs
  `tcpdump`, a DHCPv4 and a DHCPv6 client, and asks you to create one DHCPv4 and one DHCPv6 scope
  in the server instance). CI does not run the script, so run it after a change to the relay sockets.
- **Filter data is loaded when Ninlil starts.** A background service loads the clients, the filter
  configuration and the entries of every list in use, so the first DNS query does not wait for
  them. It does not delay readiness. It does nothing while filtering is off at start.
- **Forwarder zone names ignore case and a trailing dot.** `Corp.Example`, `corp.example` and
  `corp.example.` all match the same conditional forwarder zone.
- **In a Ninlil cluster, a change made on one node reaches the others' DNS answers after a delay.**
  Each node keeps its own database and caches. On the node where you make a change, the answer
  changes at once. On the other nodes, filter and client settings, rewrites, client schedules and
  forwarder zones show up after at most 60 seconds, and the entries of a filter list after at most
  5 minutes. With a [read replica](#read-replicas-and-database-clusters), add the replica's lag
  limit (`NINLIL_STORAGE_READ_REPLICA_MAX_LAG_SECONDS`, 5 seconds by default).

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

### Single sign-on and e-mail secrets

The SSO client secret, the SMTP password, the reverse-proxy shared secret and the event broker
passwords can come from two places. Each is stored encrypted
with this server's Data Protection key ring when you save it in the admin console, or it can be
supplied in plaintext from outside the application. The plaintext source wins.

| Secret | Plaintext variable | Admin console page |
|---|---|---|
| SSO client secret | `NINLIL_IDENTITY_SSO_CLIENT_SECRET` | Single sign-on |
| SMTP password | `NINLIL_EMAIL_PASSWORD` | SMTP |
| Reverse-proxy shared secret | `NINLIL_DNS_SERVER_REVERSE_PROXY_SHARED_SECRET` | Reverse proxy |
| RabbitMQ password | `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_PASSWORD` | Event broker |
| ActiveMQ password | `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_PASSWORD` | Event broker |

**Supply a secret from the environment** (containers, secret stores):

1. Set the variable for the Ninlil process, for example `NINLIL_IDENTITY_SSO_CLIENT_SECRET=<secret>`
   in your compose file or systemd unit.
2. Restart Ninlil. SSO settings are read once at startup.
3. Open the settings page. It shows that the secret comes from the environment and overrides
   anything saved there. A secret you save on the page has no effect until you unset the variable.

A plaintext secret typed into the configuration file is encrypted at startup and the plaintext is
removed. A secret from the environment is never written to disk.

The reverse-proxy shared secret and the event broker passwords are also replicated in a cluster.
The node you save one on sends it to
the other nodes over the encrypted, signed cluster channel, and each node encrypts it with its
own key ring before it writes its file. It never goes through the event broker.

**If the key ring is lost.** The Data Protection key ring lives in the data directory. If it is
recreated, or the configuration file is copied to another host without it, a stored secret can no
longer be decrypted. The same happens if you put a plaintext value into a
`..._ENCRYPTED` variable. Ninlil keeps running and logs an Error at startup (SSO) or when a mail
is sent (SMTP). The settings page shows that the saved secret cannot be decrypted. SSO sign-in
then fails at the identity provider and sends the user back to the sign-in page with an error.
An unreadable reverse-proxy shared secret fails closed: Ninlil logs an Error at startup and trusts
no reverse proxy, so every request is treated as coming from its own connection address, until you
enter the secret again and restart. An unreadable broker password becomes an empty password: the
broker refuses the login, Ninlil logs an Error at startup and keeps running, and the event broker
stays disconnected until you enter the password again and restart.

To recover:

1. Open the SSO, SMTP, Reverse proxy or Event broker settings page, or set the plaintext variable above.
2. Enter the secret again and save (or restart with the variable set).
3. For SSO, the reverse proxy and the event broker, restart Ninlil so the new secret is read.

An SMTP user name needs a password: Ninlil rejects a save that would leave a user name without
one, and logs a warning at startup if the configuration already has that state. Clear the user
name for a mail server that accepts anonymous connections.

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

The image holds nothing but a prebuilt, self-contained `ninlil-linux-<arch>` executable, so
build those first (see [Publishing the binaries](#publishing-the-binaries)), then point the
[`Dockerfile`](./Dockerfile)'s `binaries` build context at them. It only copies files, so a
two-architecture image needs no CPU emulation:

```bash
.forgejo/scripts/publish-binary.sh linux-amd64 dist
.forgejo/scripts/publish-binary.sh linux-arm64 dist
docker buildx build --build-context binaries=dist -t ninlil .
docker buildx build --build-context binaries=dist \
  --platform linux/amd64,linux/arm64 -t <registry>/ninlil --push .
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

`OpenApiSnapshotTests` in `Ninlil.Web.Tests` fails whenever the snapshot no longer matches the
controllers. To regenerate it without a running backend, run this from the repository root, then
`npm run generate:api` here:

```bash
NINLIL_UPDATE_OPENAPI_SNAPSHOT=true dotnet test src/dotnet/tests/Ninlil.Web.Tests \
  --filter FullyQualifiedName~OpenApiSnapshotTests
```

### Standard verification loop

Before considering a change done, both sides of the codebase have the same shape of gate:

1. `dotnet build Ninlil.slnx` → fix errors → `dotnet csharpier format .` →
   `dotnet test Ninlil.slnx` (must stay green) → re-run any relevant scratch smoke test for
   anything touching runtime wiring (DI composition, migrations, event delivery, real
   sockets/TLS/HTTP) — a passing build is not evidence anything works at runtime.
2. For Angular: `npm run lint` / `npm run format:check` / `npm run build` / `npm test`, all clean.

The pipeline runs exactly these checks, each in a throwaway container of the matching toolchain
(see [The pipeline](#the-pipeline)). The pipeline also lints its own scripts and workflows and
runs their self-tests; run the same check locally, before pushing a change under `.forgejo/`,
`docker/` or `src/script/`, with `./.forgejo/scripts/in-container.sh lint ./.forgejo/scripts/lint.sh`.
A few .NET tests start their own database
containers through Testcontainers, so the .NET suite needs Docker running; they carry the
`RequiresDocker` test category, which the pull-request build excludes (see
[The pipeline](#the-pipeline)). Every test listener,
Karma's included, takes a port the operating system reports as free, never a fixed or well-known
one, and neither suite needs root, so both can run on a machine that already hosts DNS, DHCP,
web, or database services.

### Publishing the binaries

[`.forgejo/scripts/publish-binary.sh`](./.forgejo/scripts/publish-binary.sh) builds one
self-contained, single-file executable — the same command the pipeline runs — with the admin
console embedded, so build the console into `Ninlil.Web/wwwroot` first:

```bash
.forgejo/scripts/build-admin-console.sh               # npm ci + npm run build, then into wwwroot
.forgejo/scripts/publish-binary.sh linux-amd64 dist   # or linux-arm64, macos-amd64, macos-arm64, windows-amd64
```

[`.forgejo/scripts/pack-packages.sh`](./.forgejo/scripts/pack-packages.sh) builds the NuGet
packages the same way: `.forgejo/scripts/pack-packages.sh 'Ninlil.*' packages`.

Each script uses whatever Node.js or .NET SDK is installed. To use the pipeline's own toolchains
instead, needing nothing but Docker, prefix it with `.forgejo/scripts/in-container.sh node` or
`.forgejo/scripts/in-container.sh dotnet`.

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
`archlinux` Forgejo runners, which need only Docker with buildx, `git`, `curl`, and `jq`. Every
build and test step runs in a throwaway container of one of the toolchains in
[`docker/ci.Dockerfile`](./docker/ci.Dockerfile) (.NET SDK; Node.js with Java and Chromium;
`makepkg`; shellcheck and actionlint; nFPM) with just the checkout mounted, through
[`.forgejo/scripts/in-container.sh`](./.forgejo/scripts/in-container.sh). One job, the Windows
smoke test, runs on a Windows runner labelled `windows-latest` instead (the project's runner
`lofnheim`; if it registers only under that name, change the job's `runs-on`). It runs
[`windows-smoke.ps1`](./.forgejo/scripts/windows-smoke.ps1), which starts the Windows executable
and checks `/status`, the admin console and one DNS query. Locally, `pwsh
.forgejo/scripts/windows-smoke.ps1 -ExecutablePath dist/ninlil-linux-amd64` runs the same checks
against a Linux binary. The Arch Linux toolchain
exists for amd64 only, so an arm64 runner runs it under emulation, which must be registered for
containers and at every boot: `qemu-user-static-binfmt`, or
`docker run --privileged --rm tonistiigi/binfmt --install amd64` from a boot-time unit.

| Workflow | Runs on | Does |
|---|---|---|
| `build.yml` | Every push to a non-release branch; pull requests into `develop`/`main` | Tests, then (pushes only) builds and publishes a pre-release |
| `release.yml` | Every push to a `v<yyyy>.<mm>.<dd>.<increment>` branch | Tests, then builds and publishes a release |
| `pipeline.yml` | Called by both of the above | The shared jobs: version; lint with the script self-tests; .NET and admin console tests; then, in parallel, one job per binary and one per NuGet package family; the Arch Linux packages, the install-tested Deb and RPM packages and the multi-architecture image from the Linux binaries; the Windows smoke test; and finally publish |
| `cleanup.yml` | Hourly | Removes pre-release releases, tags, container tags, and NuGet versions older than 24 hours |
| `bump-pins.yml` | Mondays, or by hand | Refreshes the image digests and the checkout commit (below) and opens a pull request into `develop` (by hand, any branch) |

A build publishes nothing until lint and every test have passed, and its release stays an invisible draft
until every binary (and the Windows smoke test), both Arch Linux packages, the Deb and RPM packages (after they
installed and started in Debian, Ubuntu, Rocky Linux and Fedora containers), the multi-architecture image, and every NuGet
package have been uploaded. The pipeline needs a `NINLIL_CI_TOKEN` secret (a Forgejo access token
with repository and package write access) and a `NINLIL_CI_USER` variable (that token's user name)
on the repository.

**Pull requests and the Docker socket.** A pull-request build (`verify`) runs code nobody has
reviewed yet, so its test job gets no Docker socket (it is root-equivalent on the runner host) and
runs with `--filter "Category!=RequiresDocker"`, which skips the PostgreSQL query-log tests. A
same-repo branch is pushed before its pull request, and that push build runs them with the socket.
Mark any new test that starts a container with `[Trait("Category", "RequiresDocker")]`.

**Fork pull requests (set before the repository goes public).** The runners run steps on the host
as a user in the Docker group, and a pull request from a fork runs its own copy of every workflow,
so the rule above cannot stop a malicious fork by itself. Before going public, enable the Forgejo
setting that makes workflow runs from fork pull requests wait for a maintainer's approval (or
disable Actions for forks), and read the diff of `.forgejo/` and every script it runs before
approving a run.

**Pins.** Every base image is `name:tag@sha256:<multi-architecture index digest>` (the
[`Dockerfile`](./Dockerfile), [`docker/ci.Dockerfile`](./docker/ci.Dockerfile),
[`docker-compose.yml`](./docker-compose.yml) and the PostgreSQL test host), and every
`actions/checkout` is a commit with its tag in a comment. The tag is for readers; the digest or
commit decides what runs. `bump-pins.yml` runs
[`.forgejo/scripts/bump-pins.sh`](./.forgejo/scripts/bump-pins.sh) weekly and
[`open-bump-pr.sh`](./.forgejo/scripts/open-bump-pr.sh) opens a pull request when something moved,
so a bump goes through the normal build before it is merged. To bump by hand, run
`.forgejo/scripts/bump-pins.sh --dry-run` to see what would change, then without the flag.

**Native libraries.** The container image's `libmsquic` (DNS-over-QUIC) and `libnuma1` come from
[`docker/native-libraries.lock`](./docker/native-libraries.lock): per architecture, the exact
package version, file and SHA-256. The image build downloads only those files and fails when a
sum differs, so the reviewed lock, not the package server, is the trust anchor. The same weekly
bump rewrites the lock with
[`refresh-native-lock.sh`](./.forgejo/scripts/refresh-native-lock.sh), which picks the highest
Debian version from Ubuntu `noble`, `noble-updates` and `noble-security` (and Microsoft's
`noble` repository for `libmsquic`). The Microsoft repository package that the CI toolchain image
installs is pinned by SHA-256 in `docker/ci.Dockerfile` and bumped the same way.

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

   cd ../../../..
   src/script/development/file-scoped-namespaces.sh src/dotnet/src/Ninlil.Dns.Data.EntityFrameworkCore.Migrations.*/Migrations
   ```

   `dotnet ef` writes block-scoped namespaces (`namespace X { }`); the last command converts the
   new files to the file-scoped form (`namespace X;`) the code base uses, which the lint job checks.

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
src/archlinux/          The ninlil, ninlil-git-stable, and ninlil-git-next Arch Linux packages
src/linux-packages/     The Deb and RPM package definition (nFPM) and its install scripts
src/script/             Operational scripts: database helpers and a systemd unit
```

The reasoning behind a specific design choice lives with the code: every class and public member
carries a doc comment explaining *why* it is shaped the way it is.

## License

MIT — see [`LICENSE`](./LICENSE). See [`NOTICE.md`](./NOTICE.md) for the clean-room statement and
third-party dependency license table.
