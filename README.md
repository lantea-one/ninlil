# Ninlil

Ninlil is a from-scratch, clean-room, **MIT-licensed** DNS, DHCPv4, DHCPv6 and Router
Advertisement server suite, built on **.NET 10**, with a full REST API (OpenAPI, Swagger, ReDoc)
and an **Angular** admin console for day-to-day operation. It runs as one host process: the DNS
engine, the DHCP engine, the REST API, a real-time SignalR event feed and the admin console are all
served from one program, with no separate services to wire together.

Ninlil is an independent, clean-room implementation that brings together, in one project,
functionality that is more often split across three categories of tool:

- a full-featured, standards-based authoritative and recursive DNS server;
- a network-wide ad and tracker blocker with per-client filtering and parental controls;
- a lightweight DNS and DHCP forwarder with address management.

It is one option among the tools in these categories. It is designed and built against public
IETF RFCs and IANA registries (and, for the few protocols without an RFC, their own published
specifications), never by reading or porting any other project's source code. See
[`NOTICE.md`](./NOTICE.md) for the full clean-room statement and third-party dependency license
table, and [`LICENSE`](./LICENSE) for the MIT license text.

## Contents

- [What's in the box](#whats-in-the-box)
  - [DNS server](#dns-server)
  - [DHCP, DHCPv6 & Router Advertisement](#dhcp-dhcpv6--router-advertisement)
  - [Admin API & console](#admin-api--console)
- [Quick start](#quick-start)
- [Installation](#installation)
  - [Download a binary](#download-a-binary)
  - [Windows](#windows)
  - [Arch Linux](#arch-linux)
  - [Debian, Ubuntu, Fedora, RHEL and Rocky Linux](#debian-ubuntu-fedora-rhel-and-rocky-linux)
  - [Container image](#container-image)
  - [Build and run from source](#build-and-run-from-source)
- [First run](#first-run)
  - [Ports](#ports)
  - [The data directory](#the-data-directory)
- [Configuration](#configuration)
  - [Environment variables](#environment-variables)
  - [Enabling additional transports](#enabling-additional-transports)
- [Guides](#guides)
  - [Zones and records](#zones-and-records)
  - [DNSSEC](#dnssec)
  - [Secondary zones and transfers](#secondary-zones-and-transfers)
  - [Cloud DNS sync](#cloud-dns-sync)
  - [Certificates (ACME)](#certificates-acme)
  - [Forwarding and recursion](#forwarding-and-recursion)
  - [Filtering, rewrites and parental controls](#filtering-rewrites-and-parental-controls)
  - [Encrypted transports and DNS64](#encrypted-transports-and-dns64)
  - [DHCP, DHCPv6 and Router Advertisement](#dhcp-dhcpv6-and-router-advertisement)
  - [TFTP and PXE](#tftp-and-pxe)
  - [Clustering, events and webhooks](#clustering-events-and-webhooks)
  - [Security](#security)
  - [Storage](#storage)
  - [Backup and restore](#backup-and-restore)
  - [Self-update](#self-update)
- [Using the admin console](#using-the-admin-console)
- [REST API](#rest-api)
- [Running with Docker](#running-with-docker)
  - [Run it](#run-it)
  - [DHCP and Router Advertisement](#dhcp-and-router-advertisement)
  - [A fuller example: `docker-compose.yml`](#a-fuller-example-docker-composeyml)
  - [Building the image yourself](#building-the-image-yourself)
- [Releases and versioning](#releases-and-versioning)
  - [Using the libraries from NuGet](#using-the-libraries-from-nuget)
  - [Cutting a release](#cutting-a-release)
  - [The pipeline](#the-pipeline)
- [Contributing](#contributing)
  - [Development](#development)
  - [Branches](#branches)
  - [Conventions](#conventions)
  - [Backend: adding a new entity end-to-end](#backend-adding-a-new-entity-end-to-end)
  - [Frontend: adding a feature page](#frontend-adding-a-feature-page)
  - [Project layout](#project-layout)
- [License](#license)

## What's in the box

### DNS server

- **Authoritative DNS**: primary, secondary, stub, forwarder, and RFC 9432 catalog zones; RFC 1996
  NOTIFY; RFC 2136 dynamic update; AXFR/IXFR zone transfers (including DNSSEC-signed content and
  RFC 8945 §5.3.1 multi-message TSIG chaining) over plain TCP, TLS, or QUIC.
- **Recursive & validating resolution**: a full iterative walk (RFC 1034 §5.3.3) with RFC 9156 QNAME
  minimization, RFC 8767 serve-stale, prefetch, EDNS Client Subnet, and DNS64.
- **DNSSEC**: RSA/ECDSA/EdDSA signing and validation (the SHA-1, DSA, MD5 and GOST algorithms
  are validated but never signed with, per RFC 8624), NSEC/NSEC3 (with RFC 5155 opt-out),
  automated proactive re-signing, and a full RFC 6781 automated key-rollover state machine
  (Pre-Publish for ZSKs, Double-Signature for KSKs, with automatic DS cutover when the parent zone
  is also hosted locally). RFC 5011 automated root trust-anchor rollover on the recursive side: the first check runs a
  minute after start, later ones follow the RFC's refresh rule (at most the configured interval),
  and a root key counts as revoked only if it signed its own revocation.
  Validation of recursive and forwarded answers, and the Insecure domains list that keeps private zones
  working, are in [Forwarding and recursion](#forwarding-and-recursion). DNS64 is in
  [Encrypted transports and DNS64](#encrypted-transports-and-dns64).
- **Every major DNS transport**: plain UDP/TCP (on by default — this is a DNS server, after all),
  DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC, and DNSCrypt v2. Every transport beyond plain DNS is
  off by default; see [Enabling additional transports](#enabling-additional-transports). Each one,
  and the web listener, can be limited to chosen IP addresses, to keep it off a WAN interface or
  share a host with another DNS service.
- **General upstream forwarding** with health-checked failover, independent of the recursive
  resolver, supporting DNSCrypt `sdns://` stamps, and a TTL-honouring answer cache
  (RFC 2308 negative caching, serve-stale) that operators can inspect and flush from the console.
- **TSIG (RFC 8945)** signed transfers and dynamic updates with HMAC-SHA1, -SHA256 (the default),
  -SHA384 or -SHA512 keys. A key is not server-wide: each zone lists the keys allowed to update it
  and the keys allowed to transfer it. See [Zones and records](#zones-and-records) and
  [Secondary zones and transfers](#secondary-zones-and-transfers). Also an **ACME (RFC 8555)
  DNS-01 client** for automated certificate issuance against zones this server hosts itself, with
  optional automated CAA record and PTR/reverse-zone management (including RFC 2317 classless
  delegation). See [Certificates (ACME)](#certificates-acme).
- **Cloud DNS zone sync**: Cloudflare, AWS Route 53, and DigitalOcean, kept in sync alongside
  classic AXFR/IXFR zones, covering A/AAAA/CNAME/MX/NS/TXT/SRV/CAA records. A `CloudPrimary` zone
  also accepts RFC 2136 dynamic updates. See [Cloud DNS sync](#cloud-dns-sync).
- **Filtering and parental controls**: block/allow-list filtering with subscribed external
  blocklists (hosts-file, plain-domain or adblock `||domain^`/`@@` syntax), CNAME-cloaking
  protection, a curated "blocked services" catalog, Safe Search enforcement, per-client and
  per-client-group policy, time-of-day schedules, and a one-click time-boxed pause — all resolved
  against a persistent per-device client identity (an operator-defined static address mapping,
  then DHCP lease data, then the host's ARP and neighbor table, tried in that order). See
  [Filtering, rewrites and parental controls](#filtering-rewrites-and-parental-controls).
- **DNS rewrites** (lightweight domain-to-address/CNAME overrides), general **rate limiting**, and
  **RFC 7873 DNS Cookies** for abuse hardening.
- **A persistent, searchable query log** with configurable privacy levels and a per-client activity
  view.
- **Outbound subscribable webhooks** for zone changes, cache evictions, DNSSEC re-signing, ACME
  renewal outcomes, cloud sync failures, and trust-anchor rollovers, with persisted delivery retry
  that survives a restart. See [Clustering, events and webhooks](#clustering-events-and-webhooks).
- **Multi-node clustering**: membership, heartbeat, catalog-zone auto-sync, operational config
  sync, and identity/RBAC federation across nodes, with an aggregate cluster dashboard. See
  [Clustering, events and webhooks](#clustering-events-and-webhooks).
- **Configuration presets** (`Open` / `Baseline` / `Protective` / `Family` / `FIPS`) that apply a
  curated bundle of hardening settings and blocklist subscriptions in one call.
- **Full-system, passphrase-encrypted backup and restore** (see [Backup and restore](#backup-and-restore)),
  and a self-update notifier that only checks a release feed you host (see [Self-update](#self-update)).

### DHCP, DHCPv6 & Router Advertisement

- **A dual-stack DHCP service** in the same process as DNS. DHCPv4 answers DISCOVER, REQUEST,
  DECLINE, RELEASE and INFORM, with static reservations, standard options and tag-based options
  (matching the Vendor Class Identifier, the User Class and the relay's Circuit ID or Remote ID).
  DHCPv6 answers SOLICIT, REQUEST, RENEW, REBIND, RELEASE, DECLINE, CONFIRM and
  INFORMATION-REQUEST with stateful addresses (IA_NA) and prefix delegation (IA_PD) side by side,
  and with optional Rapid Commit.
- **A relay-agent mode** for each protocol, which replaces the local server for that protocol
  (RFC 1542 and RFC 2131 §4.1 with the RFC 3046 option for DHCPv4, RFC 8415 §9 and §19
  RELAY-FORW/RELAY-REPL for DHCPv6).
- **Router Advertisement** (RFC 4861) for SLAAC, with the RFC 8106 DNS servers and search domains,
  RFC 4191 routes, and different content per interface.
- **A read-only TFTP server** (RFC 1350) for PXE boot files. A scope's boot file name and
  next-server address tell the computers what to fetch.
- **Automatic DNS registration**: a granted lease writes an A or AAAA record for the device's host
  name (for DHCPv6, the one in the Client FQDN option) in the zone the scope names, and removes it
  when the lease is released, declined or expires. A PTR record follows when the zone manages PTR
  records (see [Zones and records](#zones-and-records)).
- Step by step: [DHCP, DHCPv6 and Router Advertisement](#dhcp-dhcpv6-and-router-advertisement) and
  [TFTP and PXE](#tftp-and-pxe).

### Admin API & console

- **A complete REST API** (more than 80 controllers) covering full CRUD for every DNS/DHCP resource, every
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

## Quick start

This walk-through starts Ninlil on unprivileged ports, signs in, creates one zone with one record
and asks the server for that record. It changes nothing outside the folder you run it in.

#### Before you start

1. A Linux x86-64 host. On another platform, install Ninlil as described in
   [Installation](#installation) and use the same ports and commands from step 3 on.
2. `curl` and `dig` (your distribution's DNS utilities package provides `dig`). Check both:

   ```bash
   curl --version
   dig -v
   ```

   Each prints a version line. A `command not found` message means the tool is missing.
3. Three free TCP/UDP ports. This walk-through uses `18080` (web, HTTP), `18443` (web, HTTPS) and
   `15300` (DNS). Check one of them; the command prints only a header line when the port is free:

   ```bash
   ss -ltnu 'sport = :15300'
   ```

   ```text
   Netid State  Recv-Q Send-Q Local Address:Port Peer Address:Port
   ```

#### Steps

1. Download, check and unpack the binary: follow steps 1 to 3 of
   [Download a binary](#download-a-binary). Stay in the folder that holds `ninlil-linux-amd64`.
2. Start Ninlil. The password is the bootstrap administrator's password (at least 12 characters);
   the variables move the ports off the privileged defaults:

   ```bash
   NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD='Change-Me-Now1!' \
   NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
   NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
   NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15300 \
   ./ninlil-linux-amd64
   ```

   Leave it running and open a second terminal for the next steps. To use the container image
   instead, run it as in [Running with Docker](#running-with-docker) with the same variable passed
   as `-e` and the ports published as `-p 18080:80 -p 18443:443 -p 15300:53/udp -p 15300:53/tcp`.
3. Check that it is healthy:

   ```bash
   curl http://127.0.0.1:18080/status
   ```

   ```text
   {"status":"Healthy","components":null}
   ```

4. Sign in. `-k` accepts the self-signed certificate Ninlil generated:

   ```bash
   curl -k https://127.0.0.1:18443/api/auth/login -H 'Content-Type: application/json' -d '{"userName":"admin","password":"Change-Me-Now1!"}'
   ```

   ```text
   {"requiresTwoFactor":false,"mfaToken":null,"accessToken":"eyJhbGciOi...
   ```

   Copy the long `accessToken` value. The next commands call it `<token>`.
5. Create a primary zone. The answer starts with the zone's `id`; copy it. The next commands call
   it `<zone-id>`:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"example.test","type":"Primary"}'
   ```

   ```text
   {"id":"<zone-id>","name":"example.test","type":"Primary", ...
   ```

6. Add the zone's SOA record. A zone's first record must be its SOA record:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"example.test","type":"SOA","timeToLiveSeconds":3600,"data":{"primaryNameServer":"ns1.example.test","responsiblePersonMailbox":"admin.example.test","serial":1,"refreshSeconds":3600,"retrySeconds":900,"expireSeconds":1209600,"minimumTtlSeconds":3600}}'
   ```

   ```text
   {"id":"...","zoneId":"<zone-id>","name":"example.test","type":"SOA", ...
   ```

7. Add an A record:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"www.example.test","type":"A","timeToLiveSeconds":3600,"data":{"address":"192.0.2.10"}}'
   ```

   ```text
   {"id":"...","zoneId":"<zone-id>","name":"www.example.test","type":"A", ...
   ```

#### Check that it worked

Ask the server for the record:

```bash
dig @127.0.0.1 -p 15300 www.example.test A +noall +answer
```

```text
www.example.test.	3600	IN	A	192.0.2.10
```

Then open `https://localhost:18443/` in a browser (accept the certificate warning, which the
self-signed certificate causes), sign in as `admin` with the password from step 2 and open
**Zones**: `example.test` is listed. Press Ctrl+C in the first terminal to stop Ninlil. To start
over, delete the `data` folder next to the executable.

#### When something goes wrong

- *Step 7 or 6 answers `A zone's very first record must be its own SOA record.`* Run step 6 first.
- *The `dig` answer has status `NXDOMAIN`.* The record is missing or has another name: list the
  zone's records with `GET /api/zones/<zone-id>/records` and compare.
- *Ninlil does not start.* Read the log lines in the first terminal. The web port being in use
  is the usual cause: choose another port number. A DNS port that is in use does not stop Ninlil;
  see "Encrypted transports and DNS64" for what the log and `GET /status` show.

To go on from here, read [Installation](#installation) for a permanent setup and the
[Guides](#guides) for each feature.

## Installation

Every release, and every build of every branch, is published from the project's Forgejo pipeline
(see [Releases and versioning](#releases-and-versioning)). Pick whichever fits:

| You want | Use |
|---|---|
| To try Ninlil on Linux or macOS in minutes | [Download a binary](#download-a-binary) |
| Ninlil as a Windows service | [Windows](#windows) |
| An Arch Linux package | [Arch Linux](#arch-linux) |
| A service on Debian, Ubuntu, Fedora, RHEL, Rocky Linux or AlmaLinux | [Debian, Ubuntu, Fedora, RHEL and Rocky Linux](#debian-ubuntu-fedora-rhel-and-rocky-linux) |
| A container | [Container image](#container-image) |
| To change the code | [Build and run from source](#build-and-run-from-source) |

### Download a binary

Each [release](https://git.lantea.one/Lantea.One/ninlil/releases) carries a self-contained,
single-file executable per platform. No .NET installation is needed: the runtime, every native
library and the admin console are inside the one file.

| Platform | File |
|---|---|
| Linux x86-64 | `ninlil-linux-amd64` |
| Linux ARM64 | `ninlil-linux-arm64` |
| macOS Intel | `ninlil-macos-amd64` |
| macOS Apple Silicon | `ninlil-macos-arm64` |
| Windows x86-64 | `ninlil-windows-amd64.exe` (see [Windows](#windows)) |

#### Before you start

1. Choose a folder the user that runs Ninlil can write to, for example `/opt/ninlil`. Ninlil keeps
   all of its state in a `data/` directory next to the executable, so `/usr/local/bin` does not
   work for a user without write access there.
2. Check that port 53 is free. The command prints only a header line when it is:

   ```bash
   ss -lntu 'sport = :53'
   ```

   If it lists a listener, another program uses port 53 and Ninlil cannot bind it. Stop that
   program or choose another Ninlil port (see [Ports](#ports)). On a Linux desktop the program is
   usually the `systemd-resolved` stub listener.
3. Remember that ports 53, 80 and 443 are privileged on Linux and macOS: run Ninlil as root, or
   choose other ports (see [Ports](#ports)).

#### Steps

1. Download the executable and the checksum list. Replace `<version>` with the release's version:

   ```bash
   curl -LO https://git.lantea.one/Lantea.One/ninlil/releases/download/<version>/ninlil-linux-amd64
   curl -LO https://git.lantea.one/Lantea.One/ninlil/releases/download/<version>/SHA256SUMS
   ```

2. Check the download. The command must print `OK` for your file:

   ```bash
   sha256sum --check --ignore-missing SHA256SUMS
   ```

   ```text
   ninlil-linux-amd64: OK
   ```

3. Make the file executable:

   ```bash
   chmod +x ninlil-linux-amd64
   ```

4. Start it. `sudo` allows the privileged default ports; for a trial without root, set other
   ports as shown in [Ports](#ports):

   ```bash
   sudo ./ninlil-linux-amd64
   ```

#### Check that it worked

In a second terminal (use your own HTTP port if you changed it):

```bash
curl http://localhost/status
```

```text
{"status":"Healthy","components":null}
```

The first-start log lines and the bootstrap administrator password are described in
[First run](#first-run).

#### Behaviour worth knowing

- On the first start, the native libraries bundled in the file are extracted once to `~/.net/`
  (override with `DOTNET_BUNDLE_EXTRACT_BASE_DIR`).
- A systemd unit to start from lives in [`src/script/systemd/`](./src/script/systemd/).
- **macOS** binaries are ad-hoc signed, not notarized: remove the download quarantine flag once with
  `xattr -d com.apple.quarantine ninlil-macos-arm64` before the first run. On macOS the DNS server,
  REST API and admin console work fully; the DHCP, Router Advertisement and passive device-presence
  features rely on Linux kernel interfaces and are unavailable. DNS-over-QUIC needs `libmsquic`
  2.4.20 or later: install it with `brew install libmsquic` and start Ninlil with
  `DYLD_FALLBACK_LIBRARY_PATH=$(brew --prefix)/lib` set, as
  [Microsoft's QUIC documentation](https://learn.microsoft.com/en-us/dotnet/fundamentals/networking/quic/quic-overview)
  describes.

### Windows

The Windows executable is a plain console program that also runs as a Windows service. It is
**unsigned**, so Windows SmartScreen warns the first time you start it. Verify the download against
`SHA256SUMS` (in PowerShell: `(Get-FileHash .\ninlil-windows-amd64.exe).Hash` must equal the
`ninlil-windows-amd64.exe` line there, ignoring case) before you accept the warning. The DNS server,
REST API, and admin console work fully; the DHCP, Router Advertisement, and passive device-presence
features rely on Linux kernel interfaces and are unavailable, as on macOS.

#### Before you start

1. A Windows x86-64 machine and an administrator PowerShell.
2. Free ports: the defaults use 53 (UDP and TCP), 80 and 443; or override the ports, see
   [Ports](#ports).

#### Steps

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

Changing the service account later only needs the new account to have modify rights on the `data`
folder. How the keys in it are stored is described in [Security](#security).

### Arch Linux

Every release carries installable `ninlil` packages for `x86_64` and `aarch64`.

#### Steps

1. Install the package and start the service. Replace `<tag>` and `<pkgver>` with the release's
   tag and version:

   ```bash
   sudo pacman -U https://git.lantea.one/Lantea.One/ninlil/releases/download/<tag>/ninlil-<pkgver>-1-x86_64.pkg.tar.zst
   sudo systemctl enable --now ninlil
   ```

[`src/archlinux/`](./src/archlinux/) also has the AUR PKGBUILDs that build from source:
`ninlil-git-stable` (the newest release branch) and `ninlil-git-next` (the latest `develop`
commit). All three install a `ninlil` systemd service running as an unprivileged user with its
state in `/var/lib/ninlil`.

DNS-over-QUIC needs `libmsquic` 2.4.20 or later. The package lists it as an optional dependency
(`msquic`) and does not install it.

#### Check that it worked

```bash
curl http://localhost/status
```

```text
{"status":"Healthy","components":null}
```

The bootstrap administrator's password is in `journalctl -u ninlil`.

### Debian, Ubuntu, Fedora, RHEL and Rocky Linux

Every release carries a `.deb` (Debian, Ubuntu) and an `.rpm` (Fedora, RHEL, Rocky Linux,
AlmaLinux) for `amd64`/`x86_64` and `arm64`/`aarch64`. They install the same layout as the Arch
Linux package: the server in `/usr/lib/ninlil/`, a `ninlil` systemd service that runs as an
unprivileged `ninlil` user, all state in `/var/lib/ninlil`, and the settings in
`/etc/ninlil/ninlil.env`. Installing never starts the service; you do that in step 4.

Each package is install-tested on every build, on `amd64`, on Debian 12, Ubuntu 22.04, Ubuntu 24.04,
Rocky Linux 9 and Fedora 41. The `arm64` packages are built from the same files but not run by the
pipeline. The package manager installs the libraries the server needs (the C++ runtime, OpenSSL and
ICU) from your distribution. DNS-over-QUIC needs `libmsquic` 2.4.20 or later: the package
suggests it and does not install it, because an older release aborts the whole process when a
QUIC peer advertises the minimum UDP payload size over IPv4. See
[Microsoft's package repository](https://learn.microsoft.com/en-us/linux/packages) if you want it.

#### Steps

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

#### Check that it worked

```bash
sudo systemctl is-active ninlil
curl http://localhost/status
```

```text
active
{"status":"Healthy","components":null}
```

#### Day-to-day

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

### Container image

A hardened, multi-architecture (`linux/amd64`, `linux/arm64`) image is published to the Forgejo
container registry. Its tags, the command to run it and how to run DHCP from it are in
[Running with Docker](#running-with-docker).

### Build and run from source

#### Before you start

You need these tools. Check each with the command in the last column; the first one must print a
`10.x` version.

| Tool | Why | Notes | Check |
|---|---|---|---|
| [.NET 10 SDK](https://dotnet.microsoft.com/download) | Builds and runs everything under `src/dotnet/` | Version pinned by [`global.json`](./global.json) | `dotnet --version` |
| [Node.js](https://nodejs.org/) + npm | Builds the Angular admin console | Only needed to build the admin console; the DNS/DHCP engines and REST API build without it | `node --version` |
| A Java runtime (JRE/JDK) | `@openapitools/openapi-generator-cli` (used to regenerate the Angular app's typed API client from the backend's own OpenAPI spec) wraps the Java-based OpenAPI Generator tool | Only needed when the Angular build actually runs — see [Frontend](#frontend-srcangular) below | `java -version` |

You do **not** need PostgreSQL, MySQL, SQL Server, or MongoDB to get started — the default storage
engine is SQLite, and the database file is created and migrated automatically on first run.

#### Steps

1. Clone the repository:

   ```bash
   git clone https://git.lantea.one/Lantea.One/ninlil.git
   cd ninlil
   ```

2. Build and run it. The ports are moved off the privileged defaults (see [Ports](#ports)):

   ```bash
   NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
   NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
   NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15300 \
   dotnet run --project src/dotnet/src/Ninlil.Web
   ```

#### Check that it worked

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

#### Behaviour worth knowing

A single `dotnet build`/`dotnet run`/`dotnet publish` of `Ninlil.Web` automatically
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

#### Check that it worked

The data directory (see [The data directory](#the-data-directory)) holds these files. Your list
can hold more:

```text
cluster-node-id.txt
jwt-signing-key.txt
key-<guid>.xml
ninlil-dns.sqlite3
protocol-tls-certificate.pfx
recursive-trust-anchors.json
```

The log holds one warning line with the administrator account. A generated password follows
`Password:` in that line; if you set `NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD`, the line
says so and prints no password:

```text
warn: Ninlil.Web.Startup.AdministratorBootstrapper[0]
      No user account existed yet, so a bootstrap administrator account was created using the configured Identity:BootstrapAdministratorPassword. Username: admin -- sign in and change this password, or create a real account and delete this one, as soon as possible.
```

The browser warns about the self-signed certificate on the HTTPS port. Accept it for a first look.

### Ports

| Port | Purpose | Default state |
|---|---|---|
| `80` / `443` (`DnsServer:Web:HttpPort`/`HttpsPort`) | REST API, SignalR hub and the Angular admin console, on one Kestrel listener | Always on |
| `443` (the web HTTPS port, path `/dns-query`) | DNS-over-HTTPS (`DnsServer:Protocol:DnsOverHttps:Enable`) | Off by default |
| `53` UDP/TCP (`DnsServer:Protocol:Plain:Port`) | Plain DNS | On by default (the one transport that is) |
| `853` (`DnsServer:Protocol:DnsOverTls:Port`) | DNS-over-TLS | Off by default |
| `853` UDP (`DnsServer:Protocol:DnsOverQuic:Port`) | DNS-over-QUIC | Off by default (needs `libmsquic` 2.4.20 or later on the host; the container image ships it) |
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

### The data directory

Ninlil keeps its files in one folder, the **data directory**. Where it is depends on how you
installed Ninlil:

| Installed as | Data directory |
|---|---|
| Linux package (Deb, RPM, Arch) | `/var/lib/ninlil` |
| Docker image | `/app/data` (the volume you mount) |
| Windows service | the `data` folder next to the executable, such as `C:\Ninlil\data` |
| A downloaded binary or `dotnet run` | the `data` folder next to the executable or the built program |

On Linux and macOS the folder is readable by its owner only (mode 0700). Back it up with
[Backup and restore](#backup-and-restore) or by copying all of it while Ninlil is stopped.

## Configuration

Ninlil reads its settings from three sources. A later source overrides an earlier one:

1. `appsettings.json` in the content root (the folder Ninlil starts in; the packages and the container
   image set it to the data directory).
2. `NINLIL_*` environment variables.
3. Command-line arguments, such as `--Storage:MigrationSafety:RestoreSnapshot=<id>`.

The admin console's settings pages write your changes to `appsettings.json` and show a note that
says whether the change applies at once or after a restart. Environment variables are read once, at
start, so restart Ninlil after you change one. A value set by an environment variable overrides
the same value saved on a page.

### Environment variables

Every environment variable this application reads follows one `NINLIL_`-prefixed,
single-underscore naming scheme. **See [`ENVIRONMENT.md`](./ENVIRONMENT.md) for the reference of
every supported variable**, organized by subsystem. The reference is written by hand, and a test
fails when a variable has no row. The table below covers only the handful you are most likely to
set for a first deployment.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_STORAGE_ENGINE` | Storage backend: `Sqlite`, `PostgreSql`, `MySql`, `SqlServer`, `MongoDb` | `Sqlite` |
| `NINLIL_STORAGE_CONNECTION_STRING` | Connection string for the selected engine | *(none — SQLite auto-generates a local file)* |
| `NINLIL_DNS_SERVER_WEB_HTTP_PORT` / `..._HTTPS_PORT` | The REST API/admin-console ports | `80` / `443` |
| `NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT` | The plain DNS port | `53` |
| `NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD` | Set the bootstrap admin password explicitly instead of accepting a random one | *(random, printed once)* |
| `NINLIL_JWT_SIGNING_KEY` | The JWT signing key. Ninlil generates one at the first start; when to set it yourself is in its [row](./ENVIRONMENT.md#identity-authentication--sso) | *(generated; see the row)* |
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

## Guides

One guide per feature. Each says what the feature does and links the settings that control it.

### Zones and records

#### What it does

A zone is one part of the DNS name space that this server answers for. Ninlil stores a zone as a
list of records. The zone's type says where its records come from:

| Type | Where the records come from |
|---|---|
| `Primary` | You write them. This is the usual type. |
| `Secondary` | A copy of a zone on another server, kept current by zone transfer. See [Secondary zones and transfers](#secondary-zones-and-transfers). |
| `Stub` | A copy of only the zone's NS records, to learn which servers answer for the zone. |
| `Forwarder` | None. Ninlil sends every query for names under the zone to the servers you list. See [Forwarding and recursion](#forwarding-and-recursion). |
| `SecondaryForwarder` | A copy, by zone transfer, of a forwarding setup that another server holds. |
| `Catalog` | A list of other zones that peers create on their own (an RFC 9432 catalog zone). |
| `SecondaryCatalog` | A copy of a catalog zone from another server. |
| `CloudPrimary` | You write them, and Ninlil also pushes them to a cloud DNS provider. See [Cloud DNS sync](#cloud-dns-sync). |
| `CloudSecondary` | Pulled from a cloud DNS provider. See [Cloud DNS sync](#cloud-dns-sync). |

A zone you create has no records, not even an SOA record. The first record you add must be the
zone's SOA record. After that, every accepted change to the zone's records advances the SOA serial
by one, so you do not edit the serial yourself. The REST API manages zones under `/api/zones` and their
records under `/api/zones/<zone-id>/records`. The console does the same under **Zones**.

#### Before you start

1. A running Ninlil and a signed-in API token. [Quick start](#quick-start) steps 2 to 4 start the
   server and sign in. A token is valid for 15 minutes by default
   ([`NINLIL_JWT_ACCESS_TOKEN_LIFETIME`](./ENVIRONMENT.md#identity-authentication--sso)). A request
   with an expired token answers `401` with no body: sign in again.
2. The commands in this guide and the next four use the Quick start values: the API on
   `https://127.0.0.1:18443` and DNS on port `15300`. Replace them if you chose other ports. `-k`
   accepts Ninlil's self-signed certificate. Two placeholders stand for values you copy from an
   answer: `<token>` is the `accessToken` from the sign-in answer and `<zone-id>` is a zone's `id`.
3. A user that holds the `zones:read` and `zones:write` permissions. The bootstrap administrator
   holds every permission.
4. The Quick start zone `example.test`, with its SOA record and the `www` A record. Check that it
   exists and copy its `id`:

   ```bash
   curl -k 'https://127.0.0.1:18443/api/zones?skip=0&take=100' -H 'Authorization: Bearer <token>'
   ```

   ```text
   {"items":[{"id":"<zone-id>","name":"example.test","type":"Primary", ...
   ```

   A page holds 50 zones unless you set `take` (500 at most). With more zones than one page, raise
   `skip` to reach the next page.

#### Steps

**Add and change records.**

1. Add an MX record, a TXT record and a CNAME record. Each record's `data` object has the fields
   of its type (`preference` and `exchange` for MX, a `values` list for TXT, a `target` for CNAME).
   The console form shows the fields for the type you pick, and `/swagger` lists them for each
   type.

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"example.test","type":"MX","timeToLiveSeconds":300,"data":{"preference":10,"exchange":"mail.example.test"}}'
   ```

   ```text
   {"id":"<mx-record-id>","zoneId":"<zone-id>","name":"example.test","type":"MX", ...
   ```

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"example.test","type":"TXT","timeToLiveSeconds":300,"data":{"values":["v=spf1 -all"]}}'
   ```

   ```text
   {"id":"<txt-record-id>","zoneId":"<zone-id>","name":"example.test","type":"TXT", ...
   ```

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"web.example.test","type":"CNAME","timeToLiveSeconds":300,"data":{"target":"www.example.test"}}'
   ```

   ```text
   {"id":"...","zoneId":"<zone-id>","name":"web.example.test","type":"CNAME", ...
   ```

   Each answer starts with the new record's `id`. Copy the MX record's `id` as `<mx-record-id>` and
   the TXT record's `id` as `<txt-record-id>`. In the console: open **Zones**, press **Manage
   records** on the zone's row, press **Add record**, fill in the form and press **Save**.
2. To find the `id` of a record you did not just add, list the zone's records. Each item holds its
   `id`:

   ```bash
   curl -k 'https://127.0.0.1:18443/api/zones/<zone-id>/records?skip=0&take=100' -H 'Authorization: Bearer <token>'
   ```

   ```text
   {"items":[{"id":"...","zoneId":"<zone-id>","name":"example.test","type":"SOA", ...
   ```

3. Change the preference of the MX record. A `PATCH` changes only the fields you send; `data` is
   replaced as a whole, so send every field of the type:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id>/records/<mx-record-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"data":{"preference":20,"exchange":"mail.example.test"}}'
   ```

   An answer of `204` with no body means the change is saved.
4. Take the TXT record out of service without deleting it, and then delete it:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id>/records/<txt-record-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"isDisabled":true}'
   curl -k -X DELETE https://127.0.0.1:18443/api/zones/<zone-id>/records/<txt-record-id> -H 'Authorization: Bearer <token>'
   ```

   Both answer `204`.

**Accept dynamic updates (optional).** A client can change a zone's records with an RFC 2136
dynamic update. Ninlil accepts an update only for a `Primary` or `CloudPrimary` zone that opts in,
and only when the update is signed with a TSIG key that the zone lists.

1. Create a TSIG key. Leave out `secret` and Ninlil generates one. The answer shows the secret
   once; copy it. The next steps call it `<secret>`:

   ```bash
   curl -k https://127.0.0.1:18443/api/tsig-keys -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"update-key"}'
   ```

   ```text
   {"key":{"id":"...","name":"update-key","algorithmName":"hmac-sha256", ...},"secret":"<secret>"}
   ```

   In the console: **TSIG Keys**, **New key**. The console needs the `tsigkeys:write` permission.
2. Turn updates on for the zone and list the key. An empty list accepts no key:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"dynamicUpdateEnabled":true,"updateTsigKeyNames":["update-key"]}'
   ```

   In the console: edit the zone, open **Dynamic updates & transfers**, tick **Accept RFC 2136
   dynamic updates** and tick the key under **Keys allowed to send dynamic updates**.
3. Send an update with `nsupdate`, a command-line client that your distribution's DNS utilities
   package provides. First write the update to a file:

   ```bash
   printf 'server 127.0.0.1 15300\nzone example.test\nupdate add dyn.example.test. 300 A 192.0.2.99\nsend\n' > update.txt
   ```

   Then send it, signed with the key:

   ```bash
   nsupdate -y 'hmac-sha256:update-key:<secret>' update.txt
   ```

   `nsupdate` prints nothing when the update succeeds.

**Keep PTR records in step (optional).** Ninlil can write a PTR record for every A and AAAA record
of a zone into the matching reverse zone.

1. Create the reverse zone and its SOA record. The reverse zone for `192.0.2.0/24` is
   `2.0.192.in-addr.arpa`. The first answer starts with the new zone's `id`; copy it as
   `<reverse-zone-id>`:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"2.0.192.in-addr.arpa","type":"Primary"}'
   ```

   ```text
   {"id":"<reverse-zone-id>","name":"2.0.192.in-addr.arpa","type":"Primary", ...
   ```

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<reverse-zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"2.0.192.in-addr.arpa","type":"SOA","timeToLiveSeconds":3600,"data":{"primaryNameServer":"ns1.example.test","responsiblePersonMailbox":"admin.example.test","serial":1,"refreshSeconds":3600,"retrySeconds":900,"expireSeconds":1209600,"minimumTtlSeconds":3600}}'
   ```

2. Turn PTR management on for `example.test`. The change starts a pass in the background, which
   writes the PTR records within a few seconds:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"autoManagePtrRecords":true}'
   ```

   The answer is `204`. Wait a few seconds before the check below.

#### Check that it worked

1. Ask for the records you added:

   ```bash
   dig @127.0.0.1 -p 15300 example.test MX +noall +answer
   dig @127.0.0.1 -p 15300 web.example.test A +noall +answer
   ```

   ```text
   example.test.		300	IN	MX	20 mail.example.test.
   web.example.test.	300	IN	CNAME	www.example.test.
   www.example.test.	3600	IN	A	192.0.2.10
   ```

   The CNAME answer also carries the A record it points at.
2. Read the SOA serial. It is higher than before your changes (your values differ):

   ```bash
   dig @127.0.0.1 -p 15300 example.test SOA +noall +answer
   ```

   ```text
   example.test.		3600	IN	SOA	ns1.example.test. admin.example.test. 8 3600 900 1209600 3600
   ```

3. After the dynamic update:

   ```bash
   dig @127.0.0.1 -p 15300 dyn.example.test A +noall +answer
   ```

   ```text
   dyn.example.test.	300	IN	A	192.0.2.99
   ```

4. After the PTR pass:

   ```bash
   dig @127.0.0.1 -p 15300 -x 192.0.2.10 +noall +answer
   ```

   ```text
   10.2.0.192.in-addr.arpa. 3600	IN	PTR	www.example.test.
   ```

#### Behaviour worth knowing

- A disabled record is kept but never answered: its name answers `NXDOMAIN` when nothing else
  lives there. A disabled zone answers `REFUSED`.
- A record marked **Exclude from sync** (`excludeFromSync`) is local-only. Zone transfers and
  cloud pushes never send it, and an incoming transfer or sync never overwrites or deletes it.
- Ninlil sends a NOTIFY to the addresses in a zone's `notifyAddresses` after every change. See
  [Secondary zones and transfers](#secondary-zones-and-transfers).
- A TSIG key name is stored and shown in lowercase. DNS names compare without case
  (RFC 4343 §3), and the MAC covers the key name in lowercase canonical wire format (RFC 8945
  §4.3.3, RFC 4034 §6.2), so one spelling must do the signing. At start, Ninlil lowercases a key
  that was saved with capitals. When two keys differ only in case, it leaves both as they are and
  writes a warning that names them to the log.
- A TSIG key is not server-wide. Each zone lists the keys allowed to update it
  (`updateTsigKeyNames`) and the keys allowed to transfer it (`transferTsigKeyNames`). An empty list
  accepts no key. A request signed with a valid key that the zone does not list gets a signed
  `REFUSED`.
- The keys that clustering creates for its peers carry the label `isClusterKey`. They can be
  disabled but not renamed or deleted, they never sign or verify zone transfers or updates, and
  they go away when the peer is removed.
- Ninlil verifies a TSIG MAC that the sender truncated (for example HMAC-SHA1-96 or
  HMAC-SHA256-128) on its leading octets. A MAC longer than the hash, or shorter than the larger
  of 10 octets and half the hash length, gets `FORMERR` (RFC 8945 §5.2.2.1). An HMAC-SHA1 MAC of
  10 or 11 octets passes that rule but gets `BADTRUNC` (TSIG error 22), because Ninlil accepts no
  MAC shorter than 12 octets.
- A dynamic update answers `NOTAUTH` when the zone section names a class other than `IN` or a
  zone that Ninlil does not host. It answers `REFUSED` for an unsigned update, for a key the zone
  does not list, for a zone type that is not `Primary` or `CloudPrimary` and for a zone that has
  not opted in. A zone with no SOA record answers `SERVFAIL`.
- PTR management is off unless `autoManagePtrRecords` is `true` on the zone or the server-wide
  default is on. It works for `Primary` and `CloudPrimary` zones only. `ptrIpv4PrefixLength` picks
  the size of each reverse zone: 24 (the default) means one reverse zone per third octet, and 25 to
  30 use RFC 2317 classless delegation. `ptrIpv6PrefixLength` must be a multiple of 4 from 16 to
  124 (default 64). A value outside these ranges answers `400`. Every change to the zone's A and
  AAAA records starts a pass again. `POST /api/zones/<zone-id>/reconcile-ptr` runs one pass on
  demand and answers with the counts of PTR records added and removed.

#### Settings

- [Authoritative DNS engine](./ENVIRONMENT.md#authoritative-dns-engine): the PTR defaults
  (`NINLIL_DNS_SERVER_AUTHORITATIVE_PTR_SYNC_DEFAULT_ENABLED` and the two prefix defaults) and the
  NOTIFY settings.
- [TSIG](./ENVIRONMENT.md#tsig): `NINLIL_DNS_SERVER_TSIG_DEFAULT_FUDGE_SECONDS`, the clock
  difference a signed message may have (`300` seconds).
- In the console: **Presets & Settings**, **Zone Management**, **PTR Sync** and **TSIG**.

#### When something goes wrong

- *`401` with no body.* The token expired. Sign in again.
- *`400` "A zone's very first record must be its own SOA record."* Add the SOA record first (the
  [Quick start](#quick-start) shows it).
- *`nsupdate` prints `update failed: REFUSED`.* Check, in this order: the zone has
  `dynamicUpdateEnabled` set to `true`; the key name in `updateTsigKeyNames` is the one you pass to
  `nsupdate -y`; the secret is the one the key creation answered.
- *`nsupdate` prints `update failed: NOTAUTH`.* The zone name in the update file is not a zone
  that Ninlil hosts, or the update names a class other than `IN`.

### DNSSEC

#### What it does

Ninlil signs the zones it originates (`Primary` and `CloudPrimary`) and validates answers from other
servers on the recursive and forwarding paths. This guide covers signing. Validation is described
under [DNS server](#dns-server) in "What's in the box".

A signed zone carries three kinds of data besides its own records: a DNSKEY record set that
publishes the zone's public keys, RRSIG records that sign each record set, and a chain of NSEC or
NSEC3 records that proves which names do not exist. A zone uses two roles of key:

- The **Zone Signing Key (ZSK)** signs the zone's ordinary record sets.
- The **Key Signing Key (KSK)** signs the DNSKEY record set. The parent zone refers to the KSK with
  a **DS record**, the fingerprint of the KSK, which is how a validator finds your keys from
  outside the zone.

Signing is per zone. Ticking **Sign this zone with DNSSEC** (`isDnssecSigned`) marks a zone, and
the zone is signed only while it holds a key that can sign. Ninlil never generates keys by itself:
you create them, and Ninlil signs the zone again at once. It generates the key pair on the server
and never returns the private key.

#### Before you start

1. A running Ninlil, a signed-in API token and the Quick start zone `example.test`, as in
   [Zones and records](#zones-and-records) "Before you start". The placeholders `<token>` and
   `<zone-id>` mean the same here.
2. A user that holds the `zones:read` and `zones:write` permissions. The bootstrap administrator
   holds every permission.
3. `dig`. Check it with `dig -v`.
4. To publish a DS record for a zone that is not a child of a zone Ninlil hosts: a login to the
   registrar or to the parent zone's DNS provider.

#### Steps

**Sign a zone.**

1. Mark the zone as signed. A zone you create uses NSEC3 with 0 extra iterations and no salt. Change those
   fields in the same call only if you have a reason ("Denial of existence" under "Behaviour worth
   knowing" below gives the rules):

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"isDnssecSigned":true}'
   ```

   The answer is `204` with no body. In the console: edit the zone, open **DNSSEC signing**, tick
   **Sign this zone with DNSSEC** and press **Save**.
2. Read the zone's `dnssecServedUnsigned` field. It is `true` because the zone has no key yet:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' | grep -o '"dnssecServedUnsigned":[a-z]*'
   ```

   ```text
   "dnssecServedUnsigned":true
   ```

   The server log holds one warning: `Zone 'example.test' (<zone-id>) is marked signed but has no
   key that can sign; it is served unsigned until a key with a permitted algorithm is added. If a
   parent zone published a DS record for it, replace or remove that DS.`
3. Create the KSK. The answer holds the key's `id`, its `lifecycleState` (`Active`) and its DS
   record:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"role":"Ksk","algorithm":"ECDSAP256SHA256"}'
   ```

   ```text
   {"id":"<key-id>","zoneId":"<zone-id>","role":"KSK","algorithm":"ECDSAP256SHA256", ... "lifecycleState":"Active", ... "dsRecord":"example.test. IN DS 27562 13 2 CF3287E3...","dsDigest":"CF3287E3..."}
   ```

4. Create the ZSK:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"role":"Zsk","algorithm":"ECDSAP256SHA256"}'
   ```

   In the console, steps 3 and 4 are **Zones**, **Manage DNSSEC keys**, **Generate key**. The
   algorithms Ninlil signs with are `RSASHA256`, `RSASHA512`, `ECDSAP256SHA256`,
   `ECDSAP384SHA384`, `ED25519` and `ED448`. For an RSA key, `keySizeBits` is `0` (2048 bits) or a
   multiple of 64 from 2048 to 4096.

**Publish the DS record at the parent.**

5. Read the KSK's DS record. A ZSK has none, so its `dsRecord` is `null`:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys -H 'Authorization: Bearer <token>' | grep -o '"dsRecord":[^,]*'
   ```

   ```text
   "dsRecord":null
   "dsRecord":"example.test. IN DS 27562 13 2 CF3287E34FEDDF18A6732808880B7372E6B88C1D7964EFFBE77E12C405B44683"
   ```

   The order of the keys in the list can differ.

   In the console, the key list has a **DS and DNSKEY records** button on each KSK.
6. Enter the key tag, algorithm, digest type and digest at your registrar or at the parent zone's
   provider. The key list and the API also give the digest alone (`dsDigest`). Until the parent
   publishes the DS record, validators treat your zone as unsigned.

**Publish the DS record from a parent that Ninlil hosts (optional).** When Ninlil hosts both the
parent zone and a signed child zone, the parent publishes the child's DS record itself. This
example uses the parent `example.test` and the child `child.example.test`. The child needs its own
zone, SOA record and keys: create them as in the steps above and in
[Zones and records](#zones-and-records), then continue here. The child's answer starts with the
child zone's `id`; copy it as `<child-zone-id>`.

7. Add the delegation to the parent: an NS record at the child's name.

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"child.example.test","type":"NS","timeToLiveSeconds":300,"data":{"nameServer":"ns.child.example.test"}}'
   ```

8. The parent adds the DS record the next time it is signed. A key added to the child does not start
   that, so change the parent in any way, for example its comment:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"comments":"signed child"}'
   ```

**Roll a key (optional).** A rollover replaces a key without breaking validation. Ninlil follows
the two schemes of RFC 6781: Pre-Publish for a ZSK (§4.1.1.1) and Double-Signature for a KSK
(§4.1.2).

9. Start the rollover of an `Active` key. `<key-id>` is the `id` of the key to replace. The
   answer describes the new key:

   ```bash
   curl -k -X POST https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys/<key-id>/roll -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{}'
   ```

   ```text
   {"id":"<new-key-id>","zoneId":"<zone-id>","role":"ZSK","algorithm":"ECDSAP256SHA256", ... "lifecycleState":"Published", ...
   ```

   A KSK's new key starts in `DoubleSignature`. Add `"algorithm":"ED25519"` to the body to change
   the algorithm in the same rollover. In the console, the key list has a **Start a rollover**
   button.
10. For a KSK whose parent zone Ninlil does not host, publish the new key's DS record at the parent
    (steps 5 and 6, using the new key), and then tell Ninlil. `<new-key-id>` is the id from step 9:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys/<new-key-id>/confirm-parent-ds-updated -H 'Authorization: Bearer <token>'
    ```

    The answer is `204`. In the console, press **Confirm parent DS updated** on the new key. Ninlil
    cannot look at a registrar, so the rollover waits for this call.
11. Watch the states. Each pass of the rollover service moves a key when its wait is over:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys -H 'Authorization: Bearer <token>' | grep -o -E '"role":"[A-Z]*"|"lifecycleState":"[A-Za-z]*"'
    ```

    ```text
    "role":"ZSK"
    "lifecycleState":"Active"
    "role":"ZSK"
    "lifecycleState":"Published"
    "role":"KSK"
    "lifecycleState":"Active"
    ```

    This is the list after a ZSK rollover began. Each `role` line is followed by the
    `lifecycleState` of the same key, and the order of the keys can differ.

#### Check that it worked

1. Ask for a record with the DO bit set. The answer carries an RRSIG record (your values differ):

   ```bash
   dig @127.0.0.1 -p 15300 www.example.test A +dnssec +noall +answer
   ```

   ```text
   www.example.test.	3600	IN	A	192.0.2.10
   www.example.test.	3600	IN	RRSIG	A 13 3 3600 20261129184349 20261030174349 2348 example.test. Afr5olY+...
   ```

2. The zone publishes its keys: flag `257` marks a KSK and flag `256` a ZSK. With one KSK and one
   ZSK, as after step 4, you see two:

   ```bash
   dig @127.0.0.1 -p 15300 example.test DNSKEY +noall +answer
   ```

   ```text
   example.test.		3600	IN	DNSKEY	257 3 13 YN6opwRdsRMlb2HZK6oXYVoyVw08lQQrXVQmX/coF+y/OB2Kvnard4Ua 6zOTEstLGQDPnTwuqyoF3156+54UPA==
   example.test.		3600	IN	DNSKEY	256 3 13 XiDCOmy95sbyViNUFF3Q4Nd+A0GBwQWM/R2izc/q2q3tR4SqWfENT2eY WpjtS7nuFyR5Y+W0ZlaP6Z5dK/pwUg==
   ```

3. `dnssecServedUnsigned` is `false` (the command of step 2):

   ```text
   "dnssecServedUnsigned":false
   ```

4. With a parent that Ninlil hosts, the parent answers the DS question:

   ```bash
   dig @127.0.0.1 -p 15300 child.example.test DS +noall +answer
   ```

   ```text
   child.example.test.	3600	IN	DS	64665 13 2 D14342D5DFD3EEBD229C004DB0C1CE3DC340B93D366D61739FC1B870 A8A906BB
   child.example.test.	3600	IN	RRSIG	DS 13 3 3600 20261129184433 20261030174433 2348 example.test. 7dzq37pK...
   ```

#### Behaviour worth knowing

- **No key, no signatures.** A zone marked signed that has no key that can sign is served unsigned,
  with one warning in the log. Signatures from keys you removed are never served, because they
  would expire into a zone that validators reject. The zone is signed again as soon as it gets a
  usable key. This covers a zone with no key, with only `Removed` keys, and with only keys whose
  algorithm Ninlil never signs with.
- **`dnssecServedUnsigned`.** A zone response carries this field. It is `true` for a `Primary` or
  `CloudPrimary` zone that is enabled and marked signed while this node serves it unsigned. It is
  always `false` for other zone types. Each node reads its own in-memory signing state, so each
  node of a cluster reports for itself, and just after a key change the field can lag the new
  signing by moments. The console shows a warning icon on the zone list and a banner on the key
  list.
- **Algorithms Ninlil never signs with.** RSAMD5, DSA, RSASHA1, DSA-NSEC3-SHA1,
  RSASHA1-NSEC3-SHA1 and ECC-GOST: RFC 8624 §3.1 marks each as `MUST NOT` or `NOT RECOMMENDED`
  for signing. Creating such a key, or setting one as the zone's `dnssecAlgorithm`, answers `400`.
  Ninlil still validates zones that other servers sign with these algorithms. A key like this that
  already exists is not published and signs nothing. When it was the zone's only signing key, the
  zone is served unsigned until a key with a permitted algorithm exists. A parent zone at another
  operator that still publishes a DS record for that key must replace or remove the DS record.
- **Which algorithms may be created.** The policy lists the algorithms a new key may use. An empty
  list allows every algorithm Ninlil signs with. Change it in the console under **Presets &
  Settings**, **Zone Management**, **DNSSEC Policy**, or through `PATCH /api/dnssec-policy-settings`.
  It limits only new keys.
- **Delegations.** At a delegation point (a name below the apex with its own NS records), Ninlil
  signs only the DS record set. The NS records are the child's data and stay unsigned (RFC 4035
  §2.2), and so do names below the delegation, which includes glue. Glue stays in the zone and in
  transfers. A DS record at a delegation is served and signed. It holds a DS record for each
  `Active` KSK of a signed child zone that Ninlil hosts, plus the DS records you stored there. A
  KSK in `DoubleSignature` has no generated DS record: the parent swaps the DS record when the
  rollover promotes the new key.
- **Denial of existence.** `denialOfExistenceMode` is `NSEC3` or `NSEC`. NSEC3 hides the names in
  the zone, at the cost of hashing work. A zone you create uses NSEC3 with 0 extra iterations and no salt,
  which is what RFC 9276 §3.1 requires ("an iterations count of 0 MUST be used") and advises
  ("Operators SHOULD NOT use a salt"). The API accepts at most 150 iterations
  (`nsec3Iterations`) and answers `400` above that. Ninlil's own validator treats a zone above 150
  as insecure, and any validator may treat a count above 0 as insecure (RFC 9276 §3.2).
- **Key sizes.** Only RSA keys use `keySizeBits`: `0` for 2048 bits, or a multiple of 64 from 2048
  to 4096. The multiple of 64 comes from Windows, which creates RSA keys only in steps of 64 bits.
  Other algorithms ignore the field.
- **Signature timing.** Signatures are valid for 30 days and are backdated by one hour. Ninlil signs
  the zone again when a record or key changes, and a background pass re-signs a zone 5 days before
  its earliest signature expires. That pass runs every 6 hours.
- **The two roles.** The ZSK signs ordinary record sets and the KSK signs the DNSKEY record set.
  With no signing ZSK the KSK signs everything, and with no signing KSK the ZSKs sign the DNSKEY
  record set.
- **How long a rollover takes.** The wait times follow RFC 6781. The rollover service checks every
  15 minutes and adds a propagation delay of one hour to each wait (the time for a change to reach
  all your servers):

  | Key | Step | Moves on after |
  |---|---|---|
  | ZSK | `Published` to `Active` (the old ZSK becomes `Retired`) | the DNSKEY TTL plus the propagation delay |
  | ZSK | `Retired` to `Removed` | the largest TTL of an enabled record in the zone plus the propagation delay |
  | KSK | `DoubleSignature` (new key) to `Active` (the old key stays in `DoubleSignature`) | the DNSKEY TTL plus the propagation delay, and the parent must be able to publish the DS record: at once when Ninlil hosts the parent, otherwise after you confirm it |
  | KSK | `DoubleSignature` (old key) to `Removed` | the DS TTL plus the propagation delay |

  The DNSKEY TTL is the TTL of the zone's SOA record (3600 seconds without one). For a parent that
  Ninlil hosts, the DS TTL is the longer of 3600 seconds and the TTL of a DS record you stored at
  the child's name. For any other parent, Ninlil assumes one day, because it cannot read a
  registrar.
- **A parent hosted here, at any distance.** To find a parent that Ninlil hosts, Ninlil asks which
  zone answers the child's DS question, however many labels lie between them. The zone counts when
  it is a signed `Primary` or `CloudPrimary` zone and the child's name is the topmost zone cut
  below it. Ninlil then re-signs that parent when the child's new KSK takes over.
- **Forcing a key state.** `PUT` or `PATCH` on a key with `isRetired` sets the state by hand:
  `true` moves the key to `Removed` at once and `false` moves it to `Active`, whatever state it
  was in, which also cancels a rollover. A `PUT` that leaves out `isRetired` sets it to `false`, which moves the key to `Active`.
  Prefer a rollover for a key that is in use.
- **Zone transfers and secondaries.** A secondary zone holds no keys. It serves the DNSSEC records
  it received by zone transfer, once the secondary zone itself is marked signed (see
  [Secondary zones and transfers](#secondary-zones-and-transfers)).

#### Settings

- [Authoritative DNS engine](./ENVIRONMENT.md#authoritative-dns-engine): the signature timing,
  `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_SIGNING_SIGNATURE_VALIDITY_PERIOD` (`30.00:00:00`),
  `..._REFRESH_BEFORE_EXPIRATION` (`5.00:00:00`), `..._INCEPTION_OFFSET` (`01:00:00`) and
  `..._ZONE_SIGNING_POLL_INTERVAL` (`06:00:00`), and the rollover timing,
  `NINLIL_DNS_SERVER_AUTHORITATIVE_DNSSEC_KEY_ROLLOVER_PROPAGATION_DELAY` (`01:00:00`),
  `..._EXTERNAL_PARENT_DS_TTL` (`1.00:00:00`) and `..._DNSSEC_KEY_ROLLOVER_POLL_INTERVAL`
  (`00:15:00`).
- [DNSSEC, ACME & CAA](./ENVIRONMENT.md#dnssec-acme--caa): the list of algorithms a new key may
  use has no variable; set it in the console or through the API.
- In the console: **Presets & Settings**, **Zone Management**, **Zone Signing**, **DNSSEC Key
  Rollover** and **DNSSEC Policy**.
- [Recursive resolver](./ENVIRONMENT.md#recursive-resolver): the validation settings.

#### When something goes wrong

- *The answer has no RRSIG although the zone is marked signed.* Read `dnssecServedUnsigned`. When it
  is `true`, create a key (steps 3 and 4) or turn signing off.
- *`400` "DNSSEC algorithm ... is prohibited for signing".* Choose one of the six algorithms in
  step 4.
- *`400` "RSA key size must be a multiple of 64 between 2048 and 4096 bits; 0 means 2048."* Use a
  permitted size.
- *`400` "'151' is not a valid NSEC3 iteration count".* Use 0 to 150, and 0 unless you need more.
- *`409` "A Zsk rollover is already in progress".* A zone has one rollover per role at a time. Wait
  for the running one, or cancel it by setting the incoming key's `isRetired` to `true`.
- *A KSK stays in `DoubleSignature` for days.* The parent is not hosted here and nobody confirmed
  the DS record. Publish the DS record and call `confirm-parent-ds-updated` (step 10).
- *Validators call the zone bogus after a change at the registrar.* The parent's DS record does
  not match a published KSK. Compare the registrar's DS with the `dsRecord` of the `Active` KSK.
- *The parent hosted here shows no DS record for the child.* Change the parent in any way (step 8)
  and ask again. The child must be signed, and the delegation NS record must sit at the child's
  name.
- *The zone stops being re-signed and the log shows `Failed to sign zone`.* A record with empty
  data may sit in the zone. See [Zones and records](#zones-and-records) "When something goes
  wrong".

### Secondary zones and transfers

#### What it does

A secondary zone is a copy of a zone that another server (the primary) holds. Ninlil keeps the copy
current by zone transfer: AXFR (RFC 5936) copies the whole zone and IXFR (RFC 1995) copies only
the changes since the serial the secondary holds. A transfer travels over plain TCP, over TLS
(RFC 9103) or over QUIC. A TSIG key (RFC 8945) signs it. The primary tells the secondary about a
change with a NOTIFY message (RFC 1996), so the copy follows a change within seconds.

Ninlil plays both roles. As a primary it serves AXFR and IXFR for its `Primary` zones. As a
secondary it keeps `Secondary`, `SecondaryCatalog`, `SecondaryForwarder` and `Stub` zones. A stub
zone copies only the NS records (and glue) of the zone it names.

#### Before you start

1. A primary server that allows zone transfers with a TSIG key. This guide uses a Ninlil primary:
   the Quick start server with the zone `example.test` ([Zones and records](#zones-and-records)
   "Before you start" explains `<token>` and `<zone-id>`).
2. A second Ninlil as the secondary. To rehearse on one machine, copy the executable into a new
   empty folder (each Ninlil keeps its data in a `data` folder next to its executable) and start
   it there on other ports. The transfer client reaches the primary on port 53 unless you set
   another port, so the secondary also gets the primary's DNS port:

   ```bash
   NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD='Change-Me-Now1!' \
   NINLIL_DNS_SERVER_WEB_HTTP_PORT=18081 \
   NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18444 \
   NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15301 \
   NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_PRIMARY_PORT=15300 \
   ./ninlil-linux-amd64
   ```

   Stop the first Ninlil (Ctrl+C) and start it again with one more variable, which sends NOTIFY
   to the secondary's DNS port instead of port 53. It keeps its data, because the `data` folder
   stays where it is:

   ```bash
   NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD='Change-Me-Now1!' \
   NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
   NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
   NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15300 \
   NINLIL_DNS_SERVER_AUTHORITATIVE_NOTIFY_SECONDARY_PORT=15301 \
   ./ninlil-linux-amd64
   ```

   With two machines, skip the port variables: both servers use port 53, and the addresses below
   are the machines' own. Sign in to the secondary as in the Quick start (step 4, with port
   `18444`); its token is `<secondary-token>` below, and the primary's is `<token>`.
3. `dig`, to read the copy and to start a transfer by hand.
4. The clocks of the two servers differ by less than 300 seconds. A TSIG signature carries the
   time it was made and a fudge (300 seconds by default), and a clock difference above the fudge
   fails verification.

#### Steps

**On the primary.**

1. Create a TSIG key. Leave out `secret` and Ninlil generates one. The answer shows the secret
   once. Copy it; the next steps call it `<secret>`:

   ```bash
   curl -k https://127.0.0.1:18443/api/tsig-keys -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"xfer-key"}'
   ```

   ```text
   {"key":{"id":"...","name":"xfer-key","algorithmName":"hmac-sha256", ...},"secret":"<secret>"}
   ```

2. Allow that key to transfer the zone, and list the secondary for NOTIFY. `notifyAddresses`
   holds the secondary's IP address (without a port):

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"transferTsigKeyNames":["xfer-key"],"notifyAddresses":["127.0.0.1"]}'
   ```

   The answer is `204`. In the console: edit the zone, open **Dynamic updates & transfers**, type
   the address under **Notify addresses** and tick the key under **Keys allowed to transfer this
   zone (AXFR and IXFR)**.

**On the secondary.**

3. Create the same key with the same name, the same secret and the same algorithm (`hmac-sha256`
   is the default on both):

   ```bash
   curl -k https://127.0.0.1:18444/api/tsig-keys -H 'Authorization: Bearer <secondary-token>' -H 'Content-Type: application/json' -d '{"name":"xfer-key","secret":"<secret>"}'
   ```

4. Create the secondary zone. `primaryServerAddresses` lists the primaries in the order to try
   them. `tsigKeyName` is the key that signs the requests. `transferTransport` is `Plain`
   (the default), `Tls` or `Quic`:

   ```bash
   curl -k https://127.0.0.1:18444/api/zones -H 'Authorization: Bearer <secondary-token>' -H 'Content-Type: application/json' -d '{"name":"example.test","type":"Secondary","primaryServerAddresses":["127.0.0.1"],"tsigKeyName":"xfer-key"}'
   ```

   ```text
   {"id":"<secondary-zone-id>","name":"example.test","type":"Secondary", ...
   ```

   In the console: **Zones**, **New zone**, type **Secondary**, then fill **Transfer source** and
   the **Security** section's **TSIG key**.
5. Wait for the first transfer. The secondary looks for zones that are due every 15 seconds
   (`NINLIL_DNS_SERVER_AUTHORITATIVE_SECONDARY_ZONE_REFRESH_POLL_INTERVAL`), so wait up to half a
   minute.

#### Check that it worked

1. The secondary answers from the copy:

   ```bash
   dig @127.0.0.1 -p 15301 www.example.test A +noall +answer
   ```

   ```text
   www.example.test.	300	IN	A	192.0.2.10
   ```

2. The primary and the secondary report the same SOA serial (your values differ):

   ```bash
   dig @127.0.0.1 -p 15300 example.test SOA +short
   dig @127.0.0.1 -p 15301 example.test SOA +short
   ```

   ```text
   ns1.example.test. admin.example.test. 8 3600 900 1209600 3600
   ns1.example.test. admin.example.test. 8 3600 900 1209600 3600
   ```

3. A change on the primary reaches the secondary within seconds, through NOTIFY:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"notify.example.test","type":"A","timeToLiveSeconds":300,"data":{"address":"192.0.2.77"}}'
   dig @127.0.0.1 -p 15301 notify.example.test A +noall +answer
   ```

   ```text
   notify.example.test.	300	IN	A	192.0.2.77
   ```

4. Start a transfer by hand to see what a secondary receives. `dig` signs the request with
   `-y`:

   ```bash
   dig @127.0.0.1 -p 15300 example.test AXFR -y 'hmac-sha256:xfer-key:<secret>' +noall +answer
   ```

   ```text
   example.test.		3600	IN	SOA	ns1.example.test. admin.example.test. 12 3600 900 1209600 3600
   dyn.example.test.	300	IN	A	192.0.2.99
   web.example.test.	300	IN	CNAME	www.example.test.
   www.example.test.	3600	IN	A	192.0.2.10
   ...
   example.test.		3600	IN	SOA	ns1.example.test. admin.example.test. 12 3600 900 1209600 3600
   ```

   The first and the last record are the zone's SOA record (your records and serial differ).
5. Without the key, or with a key that the zone does not list, the primary refuses:

   ```bash
   dig @127.0.0.1 -p 15300 example.test AXFR +noall +comments
   ```

   ```text
   ;; Got answer:
   ;; ->>HEADER<<- opcode: QUERY, status: REFUSED, id: 18170
   ;; flags: qr; QUERY: 1, ANSWER: 0, AUTHORITY: 0, ADDITIONAL: 0

   ; Transfer failed.
   ```

#### Behaviour worth knowing

- **What the primary requires.** A transfer must arrive over TCP, TLS or QUIC (never UDP), must
  carry a TSIG signature that verifies, and must name the zone's apex. The key must be one of the
  zone's `transferTsigKeyNames`; an empty list accepts no key. Anything else gets `REFUSED`, and
  the refusal is signed when the key verified. A secondary that has expired (see below) answers a
  transfer with a signed `SERVFAIL`, so it does not feed a copy of old data to another secondary.
- **How a secondary refreshes.** The secondary waits the SOA Refresh time of its copy, asks the
  primary for the SOA serial over UDP and pulls a transfer when the primary's serial is ahead
  (RFC 1982 serial arithmetic). A NOTIFY from a configured primary address makes it check at
  once. With several primaries it tries them in order. A zone with no copy yet asks for AXFR; a
  zone with a copy asks for IXFR, and the primary falls back to a full transfer when its change
  history does not reach that serial.
- **Whole transfers only.** A transfer ends with the zone's closing SOA record (RFC 5936 §2.2,
  RFC 1995 §4). A transfer that stops before it, for example when the primary goes down, is not
  applied: the log says `The primary closed the connection before the closing SOA (RFC 5936 §2.2);
  the transfer is incomplete and was not applied.`
- **Retry.** When every primary fails, the secondary asks again after the SOA Retry time of its
  copy (RFC 1034 §4.3.5). A zone without a copy, and a stub zone, retry after 30 seconds
  (`NINLIL_DNS_SERVER_AUTHORITATIVE_SECONDARY_ZONE_REFRESH_DEFAULT_RETRY_INTERVAL`).
- **Expire.** A refresh succeeds when a primary shows that the copy is current or a transfer is
  applied. When no refresh succeeds for the SOA Expire time, the secondary answers `SERVFAIL` for
  the zone, and to transfers of it, until the next refresh succeeds. Ninlil stores the time of the
  last success per zone on each node, so a restart does not give an expired zone a fresh
  interval. A copy that exists but has no stored time (the first start, a restore, a switch of
  storage engine, a zone enabled again) starts a fresh interval at that moment. An Expire of `0`
  means the zone never expires; Ninlil logs a warning at load. Stub zones do not expire.
- **TSIG.** Every secondary request is signed with the zone's `tsigKeyName`. The primary signs
  each message of the answer in a chain (RFC 8945 §5.3.1). A signed SOA answer that fails
  verification is logged and ignored. Truncated MACs and the other TSIG rules are in
  [Zones and records](#zones-and-records) "Behaviour worth knowing".
- **Plain, TLS and QUIC.** The serial check always uses plain UDP on the primary's DNS port
  (`..._ZONE_TRANSFER_CLIENT_PRIMARY_PORT`, `53`), because the serial is not secret. The transfer
  then uses `transferTransport`: plain TCP on the same port, TLS on port `853`
  (`..._PRIMARY_TLS_PORT`) or QUIC on port `853` (`..._PRIMARY_QUIC_PORT`). A TLS or QUIC transfer
  needs the primary's DNS-over-TLS or DNS-over-QUIC transport turned on
  ([Enabling additional transports](#enabling-additional-transports)). The secondary checks the
  primary's certificate against the operating system's trust store. A Ninlil primary with its
  self-signed certificate fails that check. Either give the primary a certificate that the
  secondary trusts (for example one from [Certificates (ACME)](#certificates-acme)) or set
  `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_VALIDATE_SERVER_CERTIFICATE=false`. With
  the check off, only TSIG protects the content, and the connection can be intercepted.
- **Signed zones.** A transfer of a signed zone carries the DNSKEY, RRSIG, NSEC or NSEC3,
  NSEC3PARAM and generated DS records. The secondary holds no keys and signs nothing. To serve the
  DNSSEC records it received, the secondary zone itself must be marked signed:

  ```bash
  curl -k -X PATCH https://127.0.0.1:18444/api/zones/<secondary-zone-id> -H 'Authorization: Bearer <secondary-token>' -H 'Content-Type: application/json' -d '{"isDnssecSigned":true}'
  ```

  Without that flag, the secondary answers without RRSIG records even to a client that sets the DO
  bit. The console zone form shows the **DNSSEC signing** section for `Primary` and
  `CloudPrimary` zones only, so set the flag through the API.
- **Local-only records.** A record marked **Exclude from sync** is left out of every transfer. On
  a secondary, an incoming transfer never deletes such a record.
- **NOTIFY.** A secondary accepts a NOTIFY only for the apex of one of its secondary zones and only
  from one of that zone's primary addresses. The primary sends it by UDP to every address in
  `notifyAddresses`, up to 5 times, 3 seconds apart, and waits 3 seconds for the acknowledgement
  (`..._NOTIFY_MAX_ATTEMPTS`, `..._NOTIFY_RETRY_DELAY`, `..._NOTIFY_RESPONSE_TIMEOUT`). It sends
  to port 53 unless `..._NOTIFY_SECONDARY_PORT` says otherwise.

#### Settings

- [Authoritative DNS engine](./ENVIRONMENT.md#authoritative-dns-engine): the transfer settings
  (`NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_RECORDS_PER_MESSAGE`, `100`), the transfer client
  settings (ports, timeouts and the certificate check, all starting with
  `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_`), the NOTIFY settings and the two
  secondary refresh settings.
- [TSIG](./ENVIRONMENT.md#tsig): `NINLIL_DNS_SERVER_TSIG_DEFAULT_FUDGE_SECONDS` (`300`).
- In the console: **Presets & Settings**, **Zone Management**, **NOTIFY**, **Zone Transfer**,
  **Zone Transfer Client**, **Secondary Zone Refresh** and **TSIG**.

#### When something goes wrong

- *`dig ... AXFR` answers `REFUSED`.* The request was unsigned, signed with a key that the zone does
  not list in `transferTsigKeyNames`, or sent over UDP. Check the zone's list and the key name.
- *The secondary log says `The SOA answer from <address> for zone <zone>. was rejected: it failed
  TSIG verification with key xfer-key: BadVersionOrBadSignature.` and then `No usable SOA answer
  from primary <address> for zone '<zone>' (no reply, or the reply was rejected).`* The two keys
  differ. The name, the secret and the algorithm must be identical on both servers. A key's secret
  cannot be changed: delete the key and create it again with the same secret.
- *The same message without a TSIG line before it.* The primary does not answer on the port the
  secondary uses. Check the primary's address and port, and that the primary is running.
- *A change on the primary reaches the secondary only after the Refresh time.* The primary's NOTIFY
  did not arrive or was refused. The secondary accepts a NOTIFY only from an address in the zone's
  `primaryServerAddresses`, and the primary sends it only to addresses in `notifyAddresses`.
- *`Pulling zone '<zone>' from <address> failed` followed by a certificate error.* The TLS or QUIC
  certificate check failed. See "Plain, TLS and QUIC" above.
- *`Secondary zone '<zone>' expired: no primary confirmed it for 00:01:00; it answers SERVFAIL
  until a refresh succeeds.`* No primary has confirmed the copy for the SOA Expire time. Restore the
  link to a primary. The log then says `Secondary zone '<zone>' was refreshed and answers again.`
- *The secondary has the records but no RRSIG records.* Mark the secondary zone as signed (the
  command under "Signed zones").

### Cloud DNS sync

#### What it does

Ninlil keeps a zone in step with a zone at a cloud DNS provider: Cloudflare, AWS Route 53 or
DigitalOcean. The zone type picks the direction:

- A `CloudPrimary` zone is the source of truth. Ninlil serves its records and pushes them to the
  provider.
- A `CloudSecondary` zone is a read-only copy. Ninlil pulls the provider's records and serves them.

The sync matches records by content, because the providers number their records themselves. It
handles the record types A, AAAA, CNAME, MX, NS, TXT, SRV and CAA. A credential set holds the
secrets for one provider account. Several zones can share it.

#### Before you start

1. A running Ninlil, a signed-in API token and the placeholders `<token>` and `<zone-id>`, as in
   [Zones and records](#zones-and-records) "Before you start".
2. A user that holds the `cloudprovidercredentials:write` and `zones:write` permissions.
3. At the provider, a zone that already exists (Ninlil does not create it) and the zone's
   identifier. What counts as the identifier depends on the provider:

   | Provider (`providerType`) | `credentials` fields | `cloudProviderZoneId` |
   |---|---|---|
   | `Cloudflare` | `ApiToken` | the zone ID |
   | `Route53` | `AccessKeyId` and `SecretAccessKey` | the hosted zone ID |
   | `DigitalOcean` | `ApiToken` | the domain name |

4. Credentials that can list, create and delete DNS records in that zone. For a `CloudSecondary`
   zone, listing is enough.
5. The connectors have not been tried against a live provider account. Their tests use stand-in
   servers that follow each provider's documented API. Try a new zone with test records first.

#### Steps

**Push a zone to a provider (`CloudPrimary`).**

1. Create a credential set. The answer shows the secrets once. The next steps call the set's `id`
   `<credential-id>`:

   ```bash
   curl -k https://127.0.0.1:18443/api/cloud-provider-credentials -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"my-cloudflare","providerType":"Cloudflare","credentials":{"ApiToken":"<provider-api-token>"}}'
   ```

   ```text
   {"credential":{"id":"<credential-id>","name":"my-cloudflare","providerType":"Cloudflare", ...},"secrets":{"ApiToken":"..."}}
   ```

   `<provider-api-token>` is the token that you create at the provider. In the console:
   **Cloud Credentials**, **New credential set**.
2. Create the zone. `cloudSyncInterval` is how often Ninlil syncs (`hh:mm:ss`; the default is five
   minutes):

   ```bash
   curl -k https://127.0.0.1:18443/api/zones -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"example.com","type":"CloudPrimary","cloudProviderCredentialId":"<credential-id>","cloudProviderZoneId":"<provider-zone-id>","cloudSyncInterval":"00:05:00"}'
   ```

   ```text
   {"id":"<zone-id>","name":"example.com","type":"CloudPrimary", ...
   ```

   In the console: **Zones**, **New zone**, type **CloudPrimary**, then pick the credential under
   **Cloud provider credential** and type the provider's zone ID under **Cloud provider's own zone
   ID**.
3. Add the zone's SOA record, then its other records, as in [Zones and
   records](#zones-and-records). The SOA record stays local: the provider keeps its own.
4. Wait for the next sync. A zone you just created syncs within about half a minute (Ninlil looks for due
   zones every 30 seconds). Records that you add later wait for the zone's next interval.

**Copy a provider's zone (`CloudSecondary`).**

5. Create the zone with the type `CloudSecondary` and the same two provider fields as step 2.
6. Add an SOA record. Ninlil never copies the provider's SOA, so the zone needs its own SOA record
   before the first copy. When the provider answers and the zone has no record yet, the log says
   `seed it with its own SOA record`:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"example.com","type":"SOA","timeToLiveSeconds":3600,"data":{"primaryNameServer":"ns1.example.com","responsiblePersonMailbox":"admin.example.com","serial":1,"refreshSeconds":3600,"retrySeconds":900,"expireSeconds":1209600,"minimumTtlSeconds":3600}}'
   ```

7. Wait for the next sync. The zone's records then match the provider's.

**Accept dynamic updates on a `CloudPrimary` zone (optional).** Follow "Accept dynamic updates"
in [Zones and records](#zones-and-records). The zone opts in, the update is signed with a TSIG key
and the zone lists the key, as for a `Primary` zone.

#### Check that it worked

Not run: the checks below need a provider account.

1. In the provider's dashboard, or with the provider's own API, the zone shows the records that you
   added to Ninlil (`CloudPrimary`), apart from the SOA and the apex NS records.
2. Ask one of the provider's name servers for a record that you added:

   ```bash
   dig @<provider-name-server> www.example.com A +noall +answer
   ```

3. For a `CloudSecondary` zone, ask Ninlil for a record that exists only at the provider:

   ```bash
   dig @127.0.0.1 -p 15300 <provider-only-name> A +noall +answer
   ```

#### Behaviour worth knowing

- **What a push does.** A push creates the supported local records that the provider lacks. It
  deletes the supported records at the provider that the zone does not have. A record that you
  add directly at the provider is therefore removed by the next push.
- **What the sync leaves alone.** The sync never creates or deletes the SOA record or the apex NS
  records, on either side, and it ignores the provider's own. It never touches a record marked
  **Exclude from sync**. Records of a type outside the eight above are served locally and never
  pushed.
- **Route 53 sets.** A Route 53 record set holds all values of one name and type under one TTL
  (RFC 2181 §5.2). Ninlil syncs every value of a set. It never lists or changes alias sets and
  routing-policy sets (weighted, latency, failover, geolocation, multi-value answer): a create or
  delete for the name and type of such a set does nothing and logs a warning.
- **When a sync runs.** The interval is a field of each zone (`cloudSyncInterval`). Ninlil keeps the
  time of each zone's last sync in memory only, so after a restart every zone syncs once at once.
  An edit through the REST API or the console waits for the next interval. A dynamic update, and
  the ACME challenge record, start a push right away.
- **Dynamic updates on a `CloudPrimary` zone.** The zone accepts RFC 2136 updates with the rules of
  a `Primary` zone: the zone opts in, the update is TSIG-signed and the key is listed on the zone.
  Ninlil stores an accepted update and answers it at once, then pushes the zone within moments. If
  the push fails, the zone's regular sync retries it and a `CloudDnsZoneSyncFailed` event is
  raised. An update that adds a record of a type the providers cannot hold (anything but A, AAAA,
  CNAME, MX, NS, TXT, SRV and CAA) is refused as a whole, so a `CloudPrimary` reverse zone cannot
  take PTR updates. Deletions are allowed. The provider keeps its own SOA and apex NS records, so
  an update to them changes only what Ninlil serves. An update to a zone with no SOA record gets
  `SERVFAIL` until you add one.
- **One failure does not stop the rest.** A zone that fails to sync gets a log warning and a
  `CloudDnsZoneSyncFailed` event; the other zones go on. A webhook can deliver the event (see
  [Clustering, events and webhooks](#clustering-events-and-webhooks)).
- **Credentials.** Secrets are encrypted at rest and shown only when you create a set. A `PUT`
  replaces the secrets with the values you send. A `PATCH` without `credentials` changes only the
  name, the comments or the disabled flag. A zone whose credential set is disabled or missing is
  skipped with a warning.

#### Settings

There are no `NINLIL_*` variables for cloud sync. The interval, the credential and the provider
zone ID are fields of each zone. The ACME settings that use cloud zones are in
[Certificates (ACME)](#certificates-acme).

#### When something goes wrong

- *The log shows `Cloud DNS sync failed for zone '<zone>'.` followed by an HTTP error.* Read the
  last line of the error. A made-up zone ID gives `404 (Not Found)`. Check the credential, its
  permissions and the provider zone ID. The sync tries again at the next interval.
- *`Zone '<zone>' is cloud-synced but has no credential or provider zone ID configured; skipping.`*
  Set `cloudProviderCredentialId` and `cloudProviderZoneId` on the zone.
- *`Zone '<zone>' references a missing or disabled cloud DNS provider credential; skipping.`* Enable
  the credential set or pick another.
- *`Zone '<zone>' is a new CloudSecondary zone with no local records yet`.* Add the zone's SOA
  record (step 6).
- *Records at the provider keep disappearing.* The zone is a `CloudPrimary` zone, and Ninlil is the
  source of truth. Add the record to the zone in Ninlil, or mark the zone `CloudSecondary` if the
  provider is the source.

### Certificates (ACME)

#### What it does

Ninlil has an ACME (RFC 8555) client that gets and renews TLS certificates from a certificate
authority (CA) such as Let's Encrypt. It proves control of a domain with the DNS-01 challenge: it
publishes a TXT record at `_acme-challenge.<domain>` in a zone that Ninlil hosts (RFC 8555 §8.4),
waits until the zone's servers serve it, and asks the CA to check. The CA then issues the
certificate, which Ninlil stores as a PKCS#12 file (`.pfx`).

#### Before you start

1. A running Ninlil, a signed-in API token and the placeholders `<token>` and `<zone-id>`, as in
   [Zones and records](#zones-and-records) "Before you start".
2. A user that holds `acmesettings:read` and `acmesettings:write` to change the settings, and
   `acmesettings:manage` to issue a certificate on demand.
3. A public domain whose zone Ninlil hosts as `Primary` or `CloudPrimary`, and whose name servers
   at the registrar answer for that zone. The CA asks the public DNS, so the TXT record must be
   visible there. Any other zone type is refused, because Ninlil is not the source of truth for
   it.
4. A decision about the CA. The default is the Let's Encrypt production directory, which limits
   how often you may request certificates. Use the staging directory
   `https://acme-staging-v02.api.letsencrypt.org/directory` until the setup works.
5. Optional: an e-mail address for the CA to reach you. Ninlil registers every address that you
   list as a `mailto:` contact (RFC 8555 §7.3). With none, the account has no contact.

#### Steps

1. Change the ACME settings. `domains` lists every name a certificate is wanted for, one
   certificate per entry; a leading `*.` asks for a wildcard certificate. Replace `example.com` with
   your domain:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/acme-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"directoryUri":"https://acme-staging-v02.api.letsencrypt.org/directory","domains":["www.example.com","*.example.com"],"contactEmails":["admin@example.com"],"enable":true}'
   ```

   ```text
   {"note":"Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required."}
   ```

   The change shows in a read within a few seconds. In the console: **Presets & Settings**,
   **Integrations & Delivery**, **ACME / Certificates**; the page turns **Save** on only when you
   change something.
2. List the domains and their certificates:

   ```bash
   curl -k https://127.0.0.1:18443/api/acme-settings/certificates -H 'Authorization: Bearer <token>'
   ```

   ```text
   [{"domain":"www.example.com","hasCertificate":false,"issuedAtUtc":null,"expiresAtUtc":null,"isDueForRenewal":true,"lastFailureAtUtc":null,"lastFailureMessage":null}, ...]
   ```

3. Ask for the first certificate now instead of waiting for the next check (Ninlil checks every 12
   hours). The call returns the domain's entry; a failure is not an HTTP error, it shows in the
   entry:

   ```bash
   curl -k -X POST https://127.0.0.1:18443/api/acme-settings/certificates/www.example.com/issue -H 'Authorization: Bearer <token>'
   ```

   ```text
   {"domain":"www.example.com","hasCertificate":true,"issuedAtUtc":"...","expiresAtUtc":"...","isDueForRenewal":false,"lastFailureAtUtc":null,"lastFailureMessage":null}
   ```

   In the console, the page lists the domains with an **Issue/renew now** button. For a wildcard
   entry, put the name in the path as it is and quote the URL, so that the shell does not expand
   the `*`: `'https://127.0.0.1:18443/api/acme-settings/certificates/*.example.com/issue'`.
4. Point the TLS listeners at the certificate file. Ninlil writes each certificate as
   `<domain>.pfx` in `data/acme-certificates` (next to the executable), with an empty password. The
   name is lowercase, and a wildcard's `*.` becomes `_wildcard.`, so `*.example.com` gives
   `_wildcard.example.com.pfx`. Set the path for the web listener (the console, the API and
   DNS-over-HTTPS) and restart Ninlil once. Start it as in the Quick start, with the one extra
   variable; replace `/path/to` with the folder that holds the executable:

   ```bash
   NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD='Change-Me-Now1!' \
   NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
   NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
   NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15300 \
   NINLIL_DNS_SERVER_WEB_CERTIFICATE_PFX_PATH='/path/to/data/acme-certificates/www.example.com.pfx' \
   ./ninlil-linux-amd64
   ```

   DNS-over-TLS and DNS-over-QUIC take their own `..._CERTIFICATE_PFX_PATH` variables, listed under
   [DNS transports](./ENVIRONMENT.md#dns-transports).

#### Check that it worked

Not run: issuing a certificate needs a public domain and a reachable CA.

1. The domain's entry shows `"hasCertificate":true` and an `expiresAtUtc` in the future (the
   command of step 2).
2. The file exists. Run the command in the folder that holds the executable:

   ```bash
   ls data/acme-certificates
   ```

   ```text
   www.example.com.pfx
   ```

3. After the restart in step 4, the web listener presents the certificate:

   ```bash
   openssl s_client -connect 127.0.0.1:18443 -servername www.example.com </dev/null 2>/dev/null | openssl x509 -noout -subject -issuer -enddate
   ```

#### Behaviour worth knowing

- **Where the challenge record goes.** The record has a 60-second TTL. It reaches the zone's
  secondaries by NOTIFY and a transfer, and a `CloudPrimary` zone is pushed to its provider at
  once. Ninlil does not call a provider's API for the challenge itself: the record is an
  ordinary record of the zone, and the zone's normal push sends it. Ninlil removes the record
  after the check.
- **Waiting for the servers.** Before it asks the CA to check, Ninlil waits until its own zone and
  every server that may answer for the zone serve the record: the addresses in the zone's
  `notifyAddresses` and the addresses of the zone's apex NS hosts. The wait is two minutes at most,
  with a probe every 5 seconds. On a timeout the log names the servers that never served the
  record, and Ninlil asks the CA to check anyway, because a server that Ninlil cannot reach may
  still answer the CA.
- **Wildcards.** A wildcard such as `*.example.com` is validated at its base name, so its
  challenge record is `_acme-challenge.example.com` (RFC 8555 §7.1.3, §8.4).
- **Domain names.** An entry is a host name of letters, digits and hyphens with at least two
  labels, in `xn--` form for an internationalized name, optionally with a leading `*.`. Anything
  else answers `400`.
- **Renewal.** A background pass runs every 12 hours (`..._ACME_POLL_INTERVAL`). It requests a
  certificate for each domain that has none, or whose certificate expires within 30 days
  (`..._ACME_RENEW_BEFORE_EXPIRATION`). A failed attempt is recorded in `lastFailureMessage` and
  retried at the next pass. Webhooks can deliver `AcmeCertificateRenewalSucceeded` and
  `AcmeCertificateRenewalFailed` (see [Clustering, events and webhooks](#clustering-events-and-webhooks)).
- **Renewed certificates reach the listeners.** Each TLS listener checks the last-write time of its
  certificate file every 5 minutes (`..._CERTIFICATE_RELOAD_POLL_INTERVAL`) and loads a replaced
  file without a restart. A listener picks up renewals only while its path points at the ACME
  file. Pointing it at the file for the first time needs one restart.
- **The ACME account.** Ninlil keeps the account key and the state of each domain in
  `data/acme-account.json`, encrypted. A change to its path (`..._ACME_ACCOUNT_STATE_FILE_PATH`)
  needs a restart; every other ACME setting applies without one. A change of the contact list
  reaches the CA at the next issuance.
- **CAA records.** CAA records tell a CA whether it may issue for a name (RFC 8659). Ninlil can
  keep one in each `Primary` or `CloudPrimary` zone. It writes the `issue` tag with the CA's
  domain, and only when an issuer domain is set. It never changes a CAA record that you added
  yourself. The issuer domain comes from the zone (`caaIssuerDomain`) or from the server-wide
  default (`NINLIL_DNS_SERVER_ACME_CAA_AUTO_MANAGEMENT_DEFAULT_ISSUER_DOMAIN`, empty). Management
  is on by default (`NINLIL_DNS_SERVER_ACME_CAA_AUTO_MANAGEMENT_DEFAULT_ENABLED`), and it writes
  nothing until an issuer domain exists. Setting the issuer domain of a zone starts a pass in the
  background; wait a few seconds, then ask for the record:

  ```bash
  curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"caaIssuerDomain":"letsencrypt.org"}'
  dig @127.0.0.1 -p 15300 example.test CAA +noall +answer
  ```

  ```text
  example.test.		3600	IN	CAA	0 issue "letsencrypt.org"
  ```

  `POST /api/zones/<zone-id>/reconcile-caa` runs one pass on demand and answers with the counts of
  CAA records added and removed.

#### Settings

- [DNSSEC, ACME & CAA](./ENVIRONMENT.md#dnssec-acme--caa): every `NINLIL_DNS_SERVER_ACME_*`
  variable. The list-valued settings `domains` and `contactEmails` have no variable; set them in
  the console or through the API.
- [DNS transports](./ENVIRONMENT.md#dns-transports) and
  [Web host](./ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding): the certificate paths and
  the reload interval of each listener.

#### When something goes wrong

- *The entry shows `lastFailureMessage`.* Read it. With a made-up `.test` name the CA answers
  `urn:ietf:params:acme:error:rejectedIdentifier: Invalid identifiers requested :: Cannot issue for
  "www.example.test": Domain name does not end with a valid public suffix (TLD)`. Use a domain
  that the CA can issue for.
- *The log says the domain is not a zone this server hosts.* Add the zone to Ninlil first. The domain
  must lie inside a `Primary` or `CloudPrimary` zone.
- *The log names servers that never served the challenge record.* A secondary or a provider did not
  receive the record in two minutes. Check the zone's `notifyAddresses`, the secondary's transfer
  and the cloud sync. The CA may still succeed if its server sees the record.
- *`404` from the issue call.* The domain is not in the `domains` list. Look at the list, and
  remember that a change shows after a few seconds.
- *The entry shows `urn:ietf:params:acme:error:invalidContact`.* The CA refused a contact address,
  for example one at a domain that has no valid public suffix. Fix `contactEmails`; the CA sees the
  change at the next issuance.
- *The CA rate limit refuses the request.* You used the production CA too often. Use the staging
  directory while you test.

### Forwarding and recursion

#### What it does

Ninlil can find the answer to a query for a name it does not host in two ways:

- **Forwarding.** Ninlil sends the query to one or more **forwarders**, which are DNS servers you
  name, and relays the answer. A forwarder can be a public resolver or a resolver of your own.
- **Recursion.** Ninlil asks the servers of the DNS tree itself, starting at the root servers and
  following the delegations down to the server that holds the answer.

Both are **recursive service**: Ninlil does the work of finding an answer for the client. Both are off
by default, because a server that answers anyone can be misused in an attack. A query reaches them
in this order:

1. A name inside a zone that Ninlil hosts is answered from the zone.
2. Otherwise Ninlil tries forwarding, if it is on.
3. If no forwarder answered, Ninlil tries recursion, if it is on.

A client gets recursive service when its query has the RD (recursion desired) bit set, which every
stub resolver sets, and the client is **permitted recursion**. A client is permitted when
[`NINLIL_DNS_SERVER_RECURSIVE_ENABLE_RECURSION`](./ENVIRONMENT.md#recursive-resolver) is `true`, or
when an access rule gives its address the `RecursionAccess` capability. Ninlil answers any other
client's query for a name it does not host with `REFUSED`.

There are two kinds of forwarder:

- **Global forwarders** are a list. Ninlil tries them in order for every name.
- **Conditional forwarder zones** are zones of the type `Forwarder`. A zone sends the names at and
  below its own name to its own list of servers instead of the global list. Use one to send a
  private domain, such as `corp.example`, to the servers that know it. The closest enabled zone
  above the queried name wins.

Ninlil validates DNSSEC for recursive and forwarded answers. See "Behaviour worth knowing" below.

#### Before you start

1. A running Ninlil and a signed-in API token. [Quick start](#quick-start) steps 2 to 4 start the
   server and sign in. The commands use the Quick start values: the API on
   `https://127.0.0.1:18443` and DNS on port `15300`. `<token>` is the `accessToken` from the
   sign-in answer, as in [Zones and records](#zones-and-records) "Before you start".
2. A user that holds the `recursivesettings:write`, `forwardingsettings:write`, `forwarders:write`,
   `zones:write` and `accesscontrol:write` permissions. The bootstrap administrator holds every
   permission.
3. `dig` and an upstream server that Ninlil can reach on UDP and TCP port 53. This guide uses the
   public resolver Quad9 (`9.9.9.9` and `149.112.112.112`). Check that it answers from this host:

   ```bash
   dig @9.9.9.9 example.com A +short
   ```

   ```text
   104.20.23.154
   172.66.147.243
   ```

   (Your addresses differ.) No output and a `timed out` message mean a firewall blocks the path.
   Fix that first.
4. A DNS name that you can look up from a stub client. The checks use `example.com`.

#### Steps

**Allow recursive service.** Choose one of the two ways.

1. To serve every client, turn recursion on. The answer says the change takes effect at once. The
   configuration reload can take about a second, so wait a few seconds before the first query:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/recursive-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enableRecursion":true}'
   ```

   ```text
   {"note":"Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required."}
   ```

   In the console: **Presets & Settings**, **Recursive Resolver**, tick **Recurse for a query this
   server does not itself host** and save.
2. To serve only some clients, leave recursion off and add an access rule for them instead. The
   `cidr` is an address (one host) or a network such as `192.168.1.0/24`:

   ```bash
   curl -k https://127.0.0.1:18443/api/access-control-rules -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"Local network","cidr":"127.0.0.1","capabilities":["RecursionAccess"]}'
   ```

   ```text
   {"id":"...","name":"Local network","cidr":"127.0.0.1","capabilities":["RecursionAccess"],"isDisabled":false, ...
   ```

   In the console: **Access Control Rules**, **New rule**. The Quick start `dig` commands come from
   `127.0.0.1`, so this rule serves them.

**Forward to global forwarders.**

3. Turn forwarding on:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/forwarding-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
   ```

   ```text
   {"note":"Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required."}
   ```

   In the console: **Presets & Settings**, **Forwarding**.
4. Add a forwarder. `definitionString` says where to send queries; the table after this step lists
   the forms. `sortOrder` puts the forwarder in line: lower values are tried first.

   ```bash
   curl -k https://127.0.0.1:18443/api/forwarders -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"quad9","definitionString":"9.9.9.9","sortOrder":0}'
   ```

   ```text
   {"id":"...","name":"quad9","definitionString":"9.9.9.9","protocol":"Plain","sortOrder":0,"isDisabled":false, ...
   ```

   Add a second one so a failure of the first has a fallback:

   ```bash
   curl -k https://127.0.0.1:18443/api/forwarders -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"quad9-secondary","definitionString":"149.112.112.112","sortOrder":1}'
   ```

   In the console: **Forwarders**, **New forwarder**. The form offers well-known public resolvers
   in a list, grouped by provider, and a field for a definition you type.

   | `definitionString` | Meaning |
   |---|---|
   | `9.9.9.9` or `9.9.9.9:53` | Plain DNS over UDP, with TCP when an answer is truncated. `udp://` and `tcp://` in front mean the same. |
   | `[2620:fe::fe]:53` | Plain DNS over IPv6. A port after an IPv6 address needs brackets. Without a port, `2620:fe::fe` also works. |
   | `tls://dns.quad9.net` | DNS-over-TLS, port 853 unless you add `:<port>`. The host name is also the name Ninlil expects in the server's certificate. |
   | `https://dns.quad9.net/dns-query` | DNS-over-HTTPS. |
   | `quic://dns.example.net` | DNS-over-QUIC, port 853 unless you add `:<port>`. Needs `libmsquic` 2.4.20 or later on this host. |
   | `sdns://...` | A DNSCrypt stamp. See [Encrypted transports and DNS64](#encrypted-transports-and-dns64). |

   Ninlil checks the syntax when you save, and never looks a host name up at that point. A
   definition it cannot parse answers `400` with the reason, for example `an IPv6 address with a
   port must be written as [address]:port`.

**Forward one domain to its own servers (conditional forwarder zone).**

5. Create a zone of the type `Forwarder`. This example sends `corp.example` and every name below it
   to `192.0.2.53`. Use the address of a server you run:

   ```bash
   curl -k https://127.0.0.1:18443/api/zones -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"corp.example","type":"Forwarder","forwarderAddresses":["192.0.2.53"]}'
   ```

   ```text
   {"id":"<forwarder-zone-id>","name":"corp.example","type":"Forwarder", ... "forwarderAddresses":["192.0.2.53"], ... "validateDnssec":false, ...
   ```

   Copy the `id` as `<forwarder-zone-id>`. The addresses use the same forms as the table above. In
   the console: **Zones**, **New zone**, type **Forwarder**.
6. The zone has *Validate answers with DNSSEC* (`validateDnssec`) off, which is right for a private
   domain. Turn it on only for a domain that is signed under the public root:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/zones/<forwarder-zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"validateDnssec":true}'
   ```

**Let a private domain pass DNSSEC validation (Insecure domains).**

7. DNSSEC validation is on. A private domain has no chain of trust from the public root, so the
   public DNS proves it does not exist and validation fails. Put the domain on the Insecure domains
   list. A name at or below an entry skips validation and is answered without the AD bit. Saving a
   list replaces the built-in one:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/recursive-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"insecureDomains":["corp.example","home.arpa","internal"]}'
   ```

   To go back to the built-in list, send `{"insecureDomains":null}`. The console has the list on
   **Presets & Settings**, **Recursive Resolver**, with a **Restore defaults** button.

**Look at the caches and flush them.**

8. List this node's DNS caches, with sizes and hit counts:

   ```bash
   curl -k https://127.0.0.1:18443/api/dns-cache -H 'Authorization: Bearer <token>'
   ```

   ```text
   [{"name":"dnssec","entryCount":3,"maxEntries":10000,"hits":0,"misses":3},{"name":"forwarded","entryCount":6, ...},{"name":"recursive", ...}]
   ```

9. Forget one name in every cache, or empty every cache. Both need the `recursivesettings:write`
   permission. Neither touches another cluster node:

   ```bash
   curl -k -X DELETE https://127.0.0.1:18443/api/dns-cache/entries/example.com -H 'Authorization: Bearer <token>'
   curl -k -X DELETE https://127.0.0.1:18443/api/dns-cache -H 'Authorization: Bearer <token>'
   ```

   ```text
   {"removedEntryCount":0}
   {"removedEntryCount":27}
   ```

   (Your counts differ. The answer is the number of cache entries removed.)

#### Check that it worked

1. Ask Ninlil for a name it does not host:

   ```bash
   dig @127.0.0.1 -p 15300 example.com A +noall +comments +answer
   ```

   ```text
   ;; ->>HEADER<<- opcode: QUERY, status: NOERROR, id: 52613
   ;; flags: qr rd ra ad; QUERY: 1, ANSWER: 2, AUTHORITY: 0, ADDITIONAL: 1
   example.com.		235	IN	A	172.66.147.243
   example.com.		235	IN	A	104.20.23.154
   ```

   (Trimmed to the lines that matter. Your numbers and addresses differ.) `status: NOERROR` and an
   answer mean Ninlil found the name.
   The flag `ra` (recursion available) shows the client is served. `ad` (authentic data) shows
   Ninlil validated the answer with DNSSEC; a name in an unsigned zone has no `ad`.
2. Ask again after a few seconds. The TTL counts down, because Ninlil answers from its cache.
3. If you added a conditional forwarder zone, ask for a name below it. A name that the zone's
   servers know gets their answer, without `ad` while `validateDnssec` is off.
4. A client that is not permitted recursion gets `status: REFUSED`. To see that, turn
   `enableRecursion` off, remove the access rules and ask again.

#### Behaviour worth knowing

- **Who is forwarded for.** Forwarding needs RD and a client that is permitted recursion. A query
  without RD is answered from local data only.
- **Forwarders are tried in order.** Ninlil sends the query to the first forwarder and waits for
  the per-forwarder timeout (3 seconds by default). A forwarder has **failed** when it does not
  answer in time, or answers with any code except `NOERROR` and `NXDOMAIN` (for example
  `SERVFAIL`, `REFUSED` or `FORMERR`). Then Ninlil asks the next forwarder. `NOERROR` and
  `NXDOMAIN` are answers and end the search. When every forwarder has failed, Ninlil serves an
  expired cached answer if serve-stale allows it, and otherwise asks the recursive resolver,
  which is allowed for this client by the same rule.
- **The failover strategy.** The default, `HealthCheckedRoundRobin`, takes turns among the healthy
  forwarders so the load spreads, and leaves out a forwarder that failed 3 queries or probes in a
  row. One success makes it healthy again, and a probe runs every 30 seconds. When none is
  healthy Ninlil tries all of them, so forwarding is never locked out. `TryInOrder` always starts
  with the first forwarder.
- **Host names resolve in the background.** A forwarder given as a host name, such as
  `tls://dns.quad9.net`, is looked up by the operating system's resolver when Ninlil starts and
  again about every 5 minutes. A query never waits for the lookup. While a name has no known address,
  that forwarder is skipped and the next one is tried. A failed lookup keeps the last good address.
  On a server whose own system resolver is Ninlil, the lookup can succeed only through recursion or
  another forwarder. Give the forwarders as addresses, or keep recursion on.
- **A conditional forwarder zone replaces the global list.** The addresses of the closest enabled
  zone are the only forwarders for a name below it. Zone names ignore case and a trailing dot:
  `Corp.Example`, `corp.example` and `corp.example.` are the same zone. A zone of the type
  `SecondaryForwarder` gets its list of addresses from a primary by zone transfer. Its
  `validateDnssec` switch is not transferred: set it on each node.
- **Forwarded answers are validated.** While [DNSSEC validation](./ENVIRONMENT.md#recursive-resolver)
  is on (the default), Ninlil asks the forwarder with the DO and CD bits set, checks the answer
  itself and builds the reply for each client. The AD bit is set only for an answer that Ninlil
  validated and only for a client that set DO or AD; an AD bit from the forwarder is ignored. A
  client without DO gets no RRSIG, NSEC or NSEC3 records. The DS and DNSKEY records of the chain of
  trust are asked of the same forwarders. A forwarded answer never stays in the cache longer than
  its shortest signature lifetime.
- **Recursive answers are validated the same way.** The validator builds the chain of trust from
  the root trust anchor and keeps the zones it has proven in the `dnssec` cache, which the cache
  list shows and the flush calls empty. A recursive answer carries the AD bit only when it
  validated and the client set DO or AD. A client that set DO also gets the RRSIG records and, in
  the authority section, the NSEC or NSEC3 records that prove a wildcard answer. A cached answer
  never outlives its signatures. A zone that fails validation is logged as a Warning, at most
  once per zone per hour.
- **A bogus forwarded answer counts as a failure.** The forwarder is treated as failed and the
  next one is tried. A client that set CD gets the data without AD, and Ninlil does not cache it.
  When no forwarder gives a valid answer, Ninlil falls back to recursion (if allowed), which
  asks the public DNS. A private name behind an unsigned conditional forwarder zone therefore gets
  the public DNS's answer, `NXDOMAIN`, when validation is on and the name is not on the Insecure
  domains list. The log shows `returned a Bogus (DNSSEC-invalid) answer`.
- **The Insecure domains list.** It starts with the built-in list: `home.arpa` (RFC 8375),
  `internal` and the locally served reverse zones of RFC 6303 and RFC 7793 (the RFC 1918 ranges,
  `127.in-addr.arpa`, `254.169.in-addr.arpa`, the documentation ranges and the shared address
  space, and the `ip6.arpa` zones for `::`, `::1`, `fd00::/8`, `fe80::/10` and `2001:db8::/32`).
  It does not include `resolver.arpa` and `service.arpa`. A change applies to the next query, and the
  forwarded cache is flushed, because it remembers whether an answer was validated. Only the
  queried name counts, so a CNAME from a public name into a listed domain is still validated.
  In the configuration file, the list (`DnsServer:Recursive:InsecureDomains`) needs
  `NINLIL_DNS_SERVER_RECURSIVE_USE_CUSTOM_INSECURE_DOMAINS=true`. Without it Ninlil ignores the
  list, uses the built-in one and logs a Warning at start.
- **Identical queries in flight share one upstream query.** While the response cache is on (the
  default), the first query for a name, type and set of flags that is not cached goes upstream, and
  an identical query that arrives meanwhile waits for it. The waiting query is answered from the
  cache, or tries upstream on its own when the cache has nothing after the first query ends. This
  keeps the number of identical outstanding queries per upstream server at one. RFC 5452 §5
  explains the risk of more: each identical outstanding query raises the chance that an attacker's
  spoofed reply matches.
- **What the cache shares between clients.** A cached answer is reused for a query with the same
  name, type, class, DO, CD and RD bits, EDNS use and forwarder set. With EDNS Client Subnet on,
  the subnet sent upstream is part of that key, so only clients that disclose the same subnet
  share an answer. Filtering, rewrites and schedules still apply to each client's reply.
- **Recursion** walks from the root down, uses QNAME minimization (RFC 9156) so each server learns
  only what it needs, serves an expired answer when the servers are unreachable (RFC 8767), and
  refreshes a popular answer shortly before it expires (prefetch). It keeps the root trust anchors
  up to date by itself (RFC 5011), saved in `data/recursive-trust-anchors.json`. A network that
  blocks DNSSEC data needs `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_DNSSEC_VALIDATION=false`, because with
  validation on a failed DS or DNSKEY lookup makes the answer bogus and the client gets `SERVFAIL`.

#### Settings

- [Recursive resolver](./ENVIRONMENT.md#recursive-resolver): recursion, validation, serve-stale,
  prefetch, limits and trust anchors. The Insecure domains list itself has no variable.
- [Forwarding](./ENVIRONMENT.md#forwarding): the switch, the failover strategy, the health-check
  interval, the timeout and the cache.
- [Query pipeline & cross-cutting DNS behavior](./ENVIRONMENT.md#query-pipeline--cross-cutting-dns-behavior):
  EDNS Client Subnet and the outbound source-port pool.

#### When something goes wrong

- *Every query answers `REFUSED`.* The client is not permitted recursion. Turn on
  `enableRecursion` or add an access rule for the client's address, and wait a few seconds.
- *`dig` says `recursion requested but not available`.* The same cause: the reply carries no `ra`
  flag.
- *The answer is `SERVFAIL`.* Validation failed or no server answered. Read the Ninlil log for
  `Bogus` or `Forwarder` lines. If a private zone is the cause, add it to the Insecure domains
  list. If your network blocks DNSSEC data, turn validation off.
- *A host-name forwarder never answers.* The lookup of its name failed. The log shows one Warning
  after two failures in a row. Use an address, or make sure the system resolver can find the name.
- *A forwarder you added seems ignored.* Another forwarder earlier in the order, or a conditional zone,
  answers first. List the forwarders with `GET /api/forwarders` and look at `sortOrder`.
- *A cached answer is stale after you changed upstream data.* Forget the name with the commands in
  step 9.

### Filtering, rewrites and parental controls

#### What it does

Three features change what a client gets for a query. All three apply to every transport.

- **Filtering** blocks or allows names. You collect names in **filter lists**. A list is a block
  list or an allow list. A list holds domain names, or addresses (see "Behaviour worth knowing").
  Lists come from you (**local** lists) or from a web address that Ninlil fetches on a schedule
  (**subscribed** lists). A list does nothing until you assign it to a **client group** or to a
  single **client**. The one exception is a blocked service switched on for everyone (step 14).
- **Rewrites** answer a name with an address or another name that you choose, instead of the real
  answer. Use one for a name on your network or to send a search engine to its safe-search address.
- **Parental controls** are per-client and per-group pauses and time-of-day schedules, the blocked
  services catalog and Safe Search. They sit on top of lists and rewrites.

A **client** is a device Ninlil knows by a MAC address (IPv4) or a DHCP unique identifier (IPv6), not
by an address that can change. Ninlil finds the client behind a query's source address, in this
order: a static address mapping you wrote, then the lease table of Ninlil's own DHCP server, then
the host's ARP and neighbor table. A **client group** is a set of clients and networks that share
lists, schedules and rewrites.

#### Before you start

1. A running Ninlil and a signed-in API token, with the Quick start values
   ([Forwarding and recursion](#forwarding-and-recursion) "Before you start" step 1).
2. A user that holds the `filteringsettings:write`, `filterlists:write`, `clientgroups:write`,
   `clients:write`, `rewritesettings:write` and `dnsrewrites:write` permissions. The bootstrap
   administrator holds every permission.
3. Recursive service for your test client, so that names outside your zones resolve. Follow "Allow
   recursive service" in [Forwarding and recursion](#forwarding-and-recursion), steps 1 to 4.
4. `dig`. Every check asks `dig @127.0.0.1 -p 15300`.

#### Steps

**Block names with a list.**

1. Turn filtering on. It is off by default:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/filtering-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
   ```

   ```text
   {"note":"Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required."}
   ```

   In the console: **Presets & Settings**, **Filtering**.
2. Create a client group and make it the **default group**. A client that matches no other group
   gets the lists of the default group. Copy the answer's `id` as `<group-id>`:

   ```bash
   curl -k https://127.0.0.1:18443/api/client-groups -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"Everyone","isDefault":true}'
   ```

   ```text
   {"id":"<group-id>","name":"Everyone","isDefault":true,"isDisabled":false, ...
   ```

   Only one group is the default. Saving a second default group takes the flag from the first. In
   the console: **Client Groups**, **New client group**.
3. Create a local block list. Copy the `id` as `<list-id>`:

   ```bash
   curl -k https://127.0.0.1:18443/api/filter-lists -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"My blocks","kind":"Domain","action":"Block","source":"Local"}'
   ```

   ```text
   {"id":"<list-id>","name":"My blocks","kind":"Domain","action":"Block","source":"Local", ...
   ```

4. Add a name to the list. A plain name blocks the name and every name below it:

   ```bash
   curl -k https://127.0.0.1:18443/api/filter-lists/<list-id>/entries -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"value":"ads.example.org"}'
   ```

   ```text
   {"id":"...","listId":"<list-id>","value":"ads.example.org","comment":null,"kind":"Domain", ...
   ```

   Two other entry kinds exist, set with `"kind"`: `Wildcard`, a glob where `*` stands for any
   characters and `?` for one (`"value":"ads*.example.org"`), and `Regex`, a .NET regular
   expression. Both match the whole queried name. Try a pattern before you save it:

   ```bash
   curl -k https://127.0.0.1:18443/api/filter-pattern-preview -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"kind":"Wildcard","value":"ads*.example.org","sampleDomains":["ads1.example.org","www.example.org"]}'
   ```

   ```text
   {"isValid":true,"error":null,"matches":[{"domain":"ads1.example.org","isMatch":true},{"domain":"www.example.org","isMatch":false}]}
   ```

5. Assign the list to the group:

   ```bash
   curl -k https://127.0.0.1:18443/api/client-groups/<group-id>/list-assignments -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"listId":"<list-id>"}'
   ```

   ```text
   {"id":"...","clientGroupId":"<group-id>","listId":"<list-id>", ...
   ```

   In the console: **Filter Lists**, **New filter list**, then add entries on the list. Open
   **Client Groups** and use the group's list assignments.

**Subscribe to a list that someone else maintains.**

6. A subscribed list needs the web address of a list file. Ninlil reads three formats: a hosts file
   (`0.0.0.0 tracker.example.org`), one domain per line, and the ad-blocking syntax
   (`||adblock.example.org^`, with `@@||allowed.example.org^` as an exception that cancels
   matches inside the same list). To try it without a third party, serve a small file from your own
   machine. Run these in a second terminal and leave the server running:

   ```bash
   mkdir -p lists
   printf '# a test list\n0.0.0.0 tracker.example.org\n||adblock.example.org^\n@@||allowed.adblock.example.org^\n' > lists/list.txt
   python3 -m http.server 18999 --bind 127.0.0.1 --directory lists
   ```

7. Create the subscribed list and assign it. `refreshInterval` is `hh:mm:ss` and at least one hour;
   leave it out for 24 hours. Copy the `id` as `<subscribed-list-id>`:

   ```bash
   curl -k https://127.0.0.1:18443/api/filter-lists -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"My subscription","kind":"Domain","action":"Block","source":"Subscribed","subscriptionUrl":"http://127.0.0.1:18999/list.txt","refreshInterval":"01:00:00"}'
   curl -k https://127.0.0.1:18443/api/client-groups/<group-id>/list-assignments -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"listId":"<subscribed-list-id>"}'
   ```

   Ninlil looks for due lists once a minute, so the first fetch happens within about a minute. A
   shorter interval answers `400`: `'refreshInterval' must be at least 01:00:00, or null for the
   default of 24 hours.` The entries of a subscribed list cannot be edited by hand (`409`).

**Allow a name that a block list catches, and decide who wins.**

8. Create a local allow list (`"action":"Allow"`), add the name and assign it to the group, with the
   same three calls as steps 3 to 5. When a name is in a block list and an allow list for the same
   client, the setting `precedence` decides. The default is `AllowWins`. To let the block list win:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/filtering-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"precedence":"BlockWins"}'
   ```

**Choose what a blocked client sees.**

9. By default a blocked name answers `NXDOMAIN`. To answer with an address that goes nowhere
   instead (`0.0.0.0` for A, `::` for AAAA, with a TTL of 60 seconds), use sinkhole mode:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/filtering-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"blockResponseMode":"SinkholeAddress"}'
   ```

   To use other addresses, set `sinkholeAddressV4`, `sinkholeAddressV6` and
   `sinkholeTimeToLiveSeconds` in the same call.

**Give one group or one device its own rules.**

10. Add a second group and put networks in it. A `cidr` is a network (`192.168.1.0/24`) or one
    address (`192.168.1.77`). A `clientId` is a label that a DNS-over-HTTPS client puts in the URL,
    `https://<host>/dns-query/<client-id>`; the match ignores case:

    ```bash
    curl -k https://127.0.0.1:18443/api/client-groups -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"Kids"}'
    curl -k https://127.0.0.1:18443/api/client-groups/<kids-group-id>/networks -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"cidr":"192.168.1.0/24"}'
    curl -k https://127.0.0.1:18443/api/client-groups/<kids-group-id>/networks -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"clientId":"kids-tablet"}'
    ```

    Copy the first answer's `id` as `<kids-group-id>`. A row sets either `cidr` or `clientId`,
    never both. Assign lists to this group as in step 5. A client that matches this group gets
    its lists, not those of the default group.
11. Register a device as a client, so that you can pause it, give it a schedule or assign lists to
    it alone. First tell Ninlil which MAC address belongs to the device's address, unless Ninlil's
    DHCP server or the ARP table already knows it:

    ```bash
    curl -k https://127.0.0.1:18443/api/client-static-address-mappings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"cidr":"127.0.0.1","clientIdentifier":"aa:bb:cc:00:00:01","protocolVersion":"IPv4"}'
    curl -k https://127.0.0.1:18443/api/clients -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"clientIdentifier":"aa:bb:cc:00:00:01","protocolVersion":"IPv4","displayName":"Test laptop"}'
    ```

    The second answer starts with the client's `id`. Copy it as `<client-id>`. This example maps
    `127.0.0.1`, the address of your own test queries. In the console: **Clients**,
    **Static Address Mappings**.
12. Pause the client for 10 minutes. A pause blocks all of the client's queries, whatever lists,
    schedules and rewrites say. Calling it again replaces the pause:

    ```bash
    curl -k https://127.0.0.1:18443/api/clients/<client-id>/pause -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"durationMinutes":10}'
    curl -k -X POST https://127.0.0.1:18443/api/clients/<client-id>/resume -H 'Authorization: Bearer <token>'
    ```

    A group has the same two calls, `/api/client-groups/<group-id>/pause` and `/resume`. A group
    pause blocks the registered clients that belong to the group.
13. Add a time-of-day schedule. The window blocks queries from the start time to the end time in
    the named time zone, and an end time before the start time crosses midnight. `daysOfWeek` is a
    comma-separated list of day names:

    ```bash
    curl -k https://127.0.0.1:18443/api/clients/<client-id>/schedules -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"daysOfWeek":"Monday, Tuesday, Wednesday, Thursday, Sunday","startTime":"21:00:00","endTime":"07:00:00","timeZoneId":"Europe/Berlin"}'
    ```

    A group has the same call under `/api/client-groups/<group-id>/schedules`.

**Block a service or force Safe Search.**

14. The blocked services catalog turns a named service on or off with one call. Enabling it creates
    a subscribed list from a public list file. A call without `clientGroupId` or `clientId` blocks
    the service for every client, including one that matches no group. A call with one of them
    blocks it for that group or client only. The two add up: a group or client cannot unblock a
    service that is blocked for everyone, except through an allow list for the same name (the
    `precedence` setting of step 8 decides).

    ```bash
    curl -k https://127.0.0.1:18443/api/blocked-services -H 'Authorization: Bearer <token>'
    curl -k -X POST "https://127.0.0.1:18443/api/blocked-services/tiktok" -H 'Authorization: Bearer <token>'
    curl -k -X POST "https://127.0.0.1:18443/api/blocked-services/tiktok?clientGroupId=<group-id>" -H 'Authorization: Bearer <token>'
    ```

    The first call lists the catalog keys. The second blocks TikTok for every client, the third for
    one group. `DELETE` with the same query turns the same toggle off. Turning the toggle for
    everyone off keeps the block of any group or client that still has one. In the console:
    **Presets & Settings**, **Blocked Services**.
15. Safe Search makes a search engine's own safe-search address the answer for its search host
    names. Enabling an entry turns rewrites on if they were off:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/safe-search/google -H 'Authorization: Bearer <token>'
    ```

    Add `?clientGroupId=<group-id>` or `?clientId=<client-id>` to limit it to one group or one
    client. In the console: **Presets & Settings**, **Safe Search**.

**Answer a name with an address you choose (rewrite).**

16. Turn rewrites on, then add one. A target that is an IP address answers as an A or AAAA record. A
    target that is a name answers as a CNAME:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/rewrite-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    curl -k https://127.0.0.1:18443/api/dns-rewrites -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"domain":"nas.home.test","target":"192.168.1.20"}'
    curl -k https://127.0.0.1:18443/api/dns-rewrites -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"domain":"alias.home.test","target":"example.com"}'
    curl -k https://127.0.0.1:18443/api/dns-rewrites -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"domain":"*.dev.home.test","target":"192.168.1.30"}'
    ```

    ```text
    {"id":"...","domain":"nas.home.test","target":"192.168.1.20","targetKind":"A","isDisabled":false, ...
    ```

    In the console: **DNS Rewrites**, **New rewrite**.

#### Check that it worked

1. Ask for the blocked name from step 4:

   ```bash
   dig @127.0.0.1 -p 15300 ads.example.org A +noall +comments +answer
   ```

   In the default mode (before step 9) the answer is `NXDOMAIN`. After step 9 (sinkhole mode) it
   is an address that goes nowhere. These are the trimmed lines:

   ```text
   ;; ->>HEADER<<- opcode: QUERY, status: NXDOMAIN, id: 7919
   ```

   ```text
   ;; ->>HEADER<<- opcode: QUERY, status: NOERROR, id: 39615
   ads.example.org.	60	IN	A	0.0.0.0
   ```

   A name that is on no list resolves as usual. Choose a name in a domain that you do not host: the
   Quick start zone `example.test` answers every name below it itself.
2. Ask for the subscribed list's names after the first fetch (a minute or so):

   ```bash
   dig @127.0.0.1 -p 15300 tracker.example.org A +noall +comments +answer
   dig @127.0.0.1 -p 15300 allowed.adblock.example.org A +noall +comments +answer
   ```

   In sinkhole mode the first answers `0.0.0.0`. The second is not blocked, because the list's `@@`
   exception cancels the block. The Ninlil log shows how the fetch went:

   ```text
   Refreshed block/allow list 'My subscription' (4 entries, 1 of them exceptions; 0 unsupported rules skipped) from 'http://127.0.0.1:18999/list.txt'.
   ```

3. Ask for the rewrites from step 16:

   ```bash
   dig @127.0.0.1 -p 15300 nas.home.test A +noall +answer
   dig @127.0.0.1 -p 15300 alias.home.test A +noall +answer
   dig @127.0.0.1 -p 15300 x.dev.home.test A +noall +answer
   ```

   ```text
   nas.home.test.		300	IN	A	192.168.1.20
   alias.home.test.	300	IN	CNAME	example.com.
   example.com.		5	IN	A	172.66.147.243
   x.dev.home.test.	300	IN	A	192.168.1.30
   ```

   (The `example.com` addresses and TTL differ.) The `alias.home.test` answer holds the CNAME and the
   addresses of its target.
4. For a DNS-over-HTTPS client in the Kids group (step 10), a name on the Kids lists is blocked
   on `/dns-query/kids-tablet` and not on `/dns-query`.
5. Ask the API for the lists that apply to a group: `GET
   /api/client-groups/<group-id>/list-assignments`.

#### Behaviour worth knowing

- **Filter data is loaded when Ninlil starts.** A background service loads the clients, the filter
  configuration and the entries of every list in use, so the first DNS query does not wait for
  them. It does not delay readiness. It does nothing while filtering is off at start.
- **Which lists apply to a client.** A client gets the lists of every non-default group it matches,
  all together. It matches a group by a network row that contains its address, by a ClientID row,
  or because the client is registered into that group. The default group applies only when no
  other group matches. The lists assigned to the registered client itself are added on top,
  whether or not it matched a group. A list that applies to every client, which a blocked
  service switched on without a target is, is added for every client.
- **Entry kinds.** A plain domain entry blocks the name and every name below it, so
  `example.com` also blocks `www.example.com`. A rewrite is different and matches one exact name.
  A wildcard or regex match that takes longer than 250 milliseconds counts as no match and is
  logged, so a bad pattern cannot stall queries.
- **List kinds.** `Domain` lists match the queried name. `ResponseIp` lists hold addresses or
  networks and match the A and AAAA addresses in the answer. `ClientSource` lists hold addresses
  or networks and match the client's source address; a `Block` match denies the client and gets
  no reply. Wildcard and regex entries belong to `Domain` lists only.
- **CNAME cloaking.** With `blockCnameCloaking` on (the default), an answer is also blocked when a
  CNAME target in it is on one of the client's block lists. This catches a tracker hidden behind a
  subdomain of a site. A name on an allow list is never blocked for where it points.
- **Subscribed lists.** A list without an interval refreshes every 24 hours. The minimum is one
  hour, and a stored smaller value counts as one hour. A fetch that fails keeps the old entries and
  the old fetch time. The next try waits 1, 2, 4 ... minutes after each failure in a row, at most
  one hour. A download over 128 MiB is refused, and the entries of a refresh replace the old
  ones as a whole. Refreshing runs only while filtering is on.
- **What a delete removes.** Deleting a filter list removes its entries and every assignment of it
  to a group or a client. Deleting a client group removes its networks, list assignments,
  schedules and scoped rewrites, and the clients in it stay and lose the group. Deleting a client
  removes its addresses, schedules, list assignments and scoped rewrites. DHCP leases and DNS
  records are not touched.
- **Rewrites match one name.** `nas.home.test` does not match `sub.nas.home.test`. `*` in a
  rewrite domain needs at least a label there: `*.dev.home.test` does not match `dev.home.test`.
  The record types differ by target. An A rewrite answers only A queries, an AAAA rewrite only
  AAAA queries, and a CNAME rewrite answers A, AAAA and CNAME queries. Any other type is
  answered as if no rewrite existed, so a name that exists only through a rewrite can answer
  `NXDOMAIN` to a query of another type. The TTL of every rewrite answer is
  `NINLIL_DNS_SERVER_REWRITES_TIME_TO_LIVE_SECONDS` (300 by default). Of two rewrites for the same
  name, an exact one beats a wildcard, then a client's beats a group's, which beats a global one.
- **A CNAME rewrite returns the target's addresses** when the client may use forwarding or
  recursion. Ninlil looks the target up once, as a normal query, and adds its records to the
  answer. A client that is not permitted recursion gets the CNAME alone, and so does a CNAME query.
- **What replicates in a cluster.** Filter lists, blocked service lists, global rewrites and the
  global Safe Search rewrites replicate to the other nodes, and so does the setting that makes a
  blocked service apply to every client. The assignment of a list to a group
  or client, and a rewrite scoped to a client or group, stay on the node where you made them,
  because clients and groups exist only there. Turning rewrites on through Safe Search turns them on
  for every node. See also [Clustering, events and webhooks](#clustering-events-and-webhooks).
- **Filtering applies to a cached answer.** The forwarded and recursive caches hold the real
  answer, and Ninlil filters and rewrites it per client on each reply.

#### Settings

- [Filtering, rewrites, rate limiting & query log](./ENVIRONMENT.md#filtering-rewrites-rate-limiting--query-log):
  the switches, precedence, the sinkhole addresses and the rewrite TTL.
- The client groups, lists, assignments, schedules, networks and rewrites themselves are data,
  not settings. They have no variable.

#### When something goes wrong

- *A list does not block.* A list does nothing until it is assigned. Check the assignment with
  `GET /api/client-groups/<group-id>/list-assignments`. Check that filtering is on and that the
  client matches the group you assigned it to. A client that matches a non-default group does
  not get the default group's lists.
- *A subscribed list shows no `lastFetchedAtUtc`.* Wait a minute and read the log. A fetch
  warning names the reason, such as a refused connection, a status code or the 128 MiB limit.
- *A blocked service blocks nothing.* Filtering is off, or its list has not been fetched yet.
  Turn filtering on (step 1) and read `lastFetchedAtUtc` of the list in `/api/filter-lists`.
- *`400` on an entry.* The pattern does not compile, or the kind does not fit the list. Use the
  pattern preview in step 4.
- *A rewrite is ignored.* Rewrites are off (`GET /api/rewrite-settings`), a more specific rewrite
  wins, or the query type is not one the target answers.
- *A client keeps being blocked after you removed its list.* A pause or schedule still applies.
  Resume the client and list its schedules.

### Encrypted transports and DNS64

#### What it does

Plain DNS over UDP and TCP sends every query in the clear. Ninlil also answers over four encrypted
transports. Each is off by default, and each has its own on/off switch:

| Transport | Standard | Default port | Notes |
|---|---|---|---|
| DNS-over-TLS (DoT) | RFC 7858 | `853` TCP | A TLS connection that carries DNS messages. |
| DNS-over-HTTPS (DoH) | RFC 8484 | the web HTTPS port (`443`), path `/dns-query` | Served by the same listener as the console and the REST API. |
| DNS-over-QUIC (DoQ) | RFC 9250 | `853` UDP | Needs the native library `libmsquic` 2.4.20 or later on the host. The container image ships it. |
| DNSCrypt v2 | the DNSCrypt v2 specification | `5443` UDP and TCP | Needs a provider name. Clients use a DNS Stamp (`sdns://...`) to find the server. |

Ninlil can also use these transports towards an upstream. See "Forward to global forwarders" in
[Forwarding and recursion](#forwarding-and-recursion).

**DNS64** helps a client that has only IPv6. For a name with an A record and no AAAA record, Ninlil
invents an AAAA record by placing the IPv4 address inside an IPv6 prefix that a NAT64 gateway
translates (RFC 6147, RFC 6052). Skip it if you run no NAT64 gateway.

**Certificates.** DoT, DoH and DoQ use a TLS certificate.

- With no setting, Ninlil makes a self-signed certificate for the name `localhost`, valid for two
  years, and saves it as `protocol-tls-certificate.pfx` in the [data
  directory](#the-data-directory). DoT and DoQ share it. Clients refuse a self-signed certificate
  unless you tell them to trust it. Use it to rehearse.
- For clients on your network, point the PFX path of the transport at a certificate that
  your clients trust, such as one from [Certificates (ACME)](#certificates-acme).
- DoH is not a listener of its own. It uses the port, addresses and certificate of the web
  listener, so set `NINLIL_DNS_SERVER_WEB_CERTIFICATE_PFX_PATH` (and the password variable
  beside it) for DoH.

#### Before you start

1. A running Ninlil and a signed-in API token, with the Quick start values
   ([Forwarding and recursion](#forwarding-and-recursion) "Before you start" step 1).
2. A user that holds the `transportsettings:write` permission. DNS64 needs `dns64settings:write`
   and the PROXY step needs `reverseproxy:write`.
3. Free ports. The commands use `18853` for DoT and DoQ (they use TCP and UDP, so they do not
   collide) and `15443` for DNSCrypt. The Quick start ports are already taken. Check one:

   ```bash
   ss -ltnu 'sport = :18853'
   ```

   Only the header line means the port is free.
4. `openssl`, `curl` and a `dig` that supports `+tls` (version 9.18 or later; `dig -v` prints
   the version). Check them:

   ```bash
   openssl version
   curl --version
   dig -v
   ```

5. For DoQ, `libmsquic` 2.4.20 or later. Check it:

   ```bash
   ldconfig -p | grep libmsquic
   ```

   ```text
   	libmsquic.so.2 (libc6,x86-64) => /usr/lib/libmsquic.so.2
   ```

   Without a line, install it as the [Installation](#installation) section for your install type
   says. With an older version a peer that advertises a 1200-byte UDP payload size over IPv4 can
   abort the process.
6. For the Quick start examples, a zone that answers: the `www.example.test` record from
   [Quick start](#quick-start).

#### Steps

**Turn on DNS-over-TLS.**

1. Set the port, then switch the transport on. Set the port first: a transport reads its port when
   it starts. A switch takes effect at once, with no restart:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dns-over-tls -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"port":18853}'
   curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dns-over-tls -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
   ```

   ```text
   {"note":"Written to the configuration file, and the underlying transport listener was started or stopped to match -- this transport's enabled/disabled state took effect immediately, no restart required."}
   ```

   In the console: **Presets & Settings**, **Transports**.
2. Save Ninlil's self-signed certificate, so that a client can trust it. The command connects,
   reads the certificate and writes it to `ninlil-cert.pem`:

   ```bash
   openssl s_client -connect 127.0.0.1:18853 -servername localhost </dev/null 2>/dev/null | openssl x509 -out ninlil-cert.pem
   openssl x509 -in ninlil-cert.pem -noout -subject -dates
   ```

   ```text
   subject=CN=localhost
   notBefore=Oct 29 19:14:07 2026 GMT
   notAfter=Oct 29 19:14:07 2028 GMT
   ```

**Turn on DNS-over-HTTPS.**

3. DoH needs no port, because the web listener serves it. Switch it on:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dns-over-https -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
   ```

   The URL path (`/dns-query`) and the largest request (65535 bytes) are the other two fields. A
   DoH client can add one more path segment, `/dns-query/<client-id>`, to choose a client group
   (see [Filtering, rewrites and parental controls](#filtering-rewrites-and-parental-controls) step 10).

**Turn on DNS-over-QUIC.**

4. Set the port, then switch the transport on:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dns-over-quic -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"port":18853}'
   curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dns-over-quic -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
   ```

**Turn on DNSCrypt.**

5. A DNSCrypt server needs a **provider name**, the DNS name under which clients find its
   certificates. Choose a name below a domain you control, for example `dnscrypt.example.net`. Ninlil
   publishes the certificates as TXT records at `2.dnscrypt-cert.<provider-name>`. You can write the
   name with or without the `2.dnscrypt-cert.` prefix; Ninlil never doubles it. Set the name and
   the port, then switch the transport on:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dnscrypt -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"providerName":"dnscrypt.example.net","port":15443}'
   curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dnscrypt -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
   ```

   With no provider name the switch still answers `200`, but the listeners do not start and the
   log says `DNSCrypt is enabled, but no ProviderName is configured`. If you set the name after
   you switched the transport on, restart the two listeners:

   ```bash
   curl -k -X POST https://127.0.0.1:18443/api/services/dnscrypt-udp/restart -H 'Authorization: Bearer <token>'
   curl -k -X POST https://127.0.0.1:18443/api/services/dnscrypt-tcp/restart -H 'Authorization: Bearer <token>'
   ```

   Enabling DNSCrypt creates the server's identity, a key pair, in `dnscrypt-certificates.json` in
   the data directory. Ninlil keeps that file across restarts. Back it up: clients pin the
   public key, so a different identity means every client must learn a different stamp.
6. Make the DNS Stamp your clients import. A stamp holds the server address, the identity's public
   key and the provider name in the full form. This command builds one from the identity file.
   Replace `<data-directory>` with your [data directory](#the-data-directory), and
   `<server-address>` with the address and port that clients use, such as `192.0.2.1:5443`:

   ```bash
   python3 -c 'import base64,json,sys; d,a,n=sys.argv[1:4]; k=base64.b64decode(json.load(open(d+"/dnscrypt-certificates.json"))["ProviderPublicKey"]); lp=lambda b:bytes([len(b)])+b; r=b"\x01"+bytes(8)+lp(a.encode())+lp(k)+lp(n.encode()); print("sdns://"+base64.urlsafe_b64encode(r).decode().rstrip("="))' <data-directory> <server-address> 2.dnscrypt-cert.dnscrypt.example.net
   ```

   ```text
   sdns://AQAAAAAAAAAADzEyNy4wLjAuMToxNTQ0MyCZD7AEiqTnvuJGWOgu7B52ehNmoCZrsxgE4eP9Y6AqJCYyLmRuc2NyeXB0LWNlcnQucmVzb2x2ZXIyLmV4YW1wbGUudGVzdA
   ```

   (Your stamp differs.) The stamp has the layout of the DNS Stamps specification for a DNSCrypt
   server: the byte `0x01`, eight bytes of properties, then the address, the public key and the
   provider name, each with a length byte, all in unpadded base64url.

**Put a PROXY-protocol load balancer in front of DNS-over-TLS (optional).**

7. A TCP proxy in front of Ninlil hides the client's address. The proxy can send a PROXY protocol
   header (version 1 text or version 2 binary) at the start of each connection, so Ninlil sees the
   real client address in access rules, filters, rate limits and the query log. Ninlil trusts the
   header only from the addresses you list, because anyone else could forge it. This example
   trusts one proxy at `10.0.0.5`. The setting needs a restart of Ninlil:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/reverse-proxy-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"dnsOverTls":{"enable":true,"knownProxies":["10.0.0.5"]}}'
   ```

   Use `knownNetworks` (for example `["10.0.0.0/24"]`) for a proxy whose address changes. In the
   console: **Presets & Settings**, **Reverse proxy**.

**Synthesize AAAA records (DNS64).**

8. DNS64 works on the recursive path only. A forwarder's answer is relayed unchanged, so turn
   forwarding off while you use DNS64 (`{"enable":false}` to `/api/forwarding-settings`), or leave
   DNS64 off. Recursion must be on (see [Forwarding and
   recursion](#forwarding-and-recursion)). Then switch DNS64 on:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/dns64-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
   ```

   The default prefix is the Well-Known Prefix `64:ff9b::/96`. If your NAT64 gateway uses its own
   prefix, set it:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/dns64-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"synthesisPrefix":"2001:db8:64::/96"}'
   ```

   Ninlil accepts the RFC 6052 lengths `/32`, `/40`, `/48`, `/56`, `/64` and `/96` only. A prefix that
   is multicast, inside `::ffff:0:0/96` or has a non-zero "u" octet (bits 64 to 71) answers `400`.
   In the console: **Presets & Settings**, **DNS64**.

#### Check that it worked

1. DNS-over-TLS. `+tls-ca` names the saved certificate and `+tls-hostname` the name inside it:

   ```bash
   dig @127.0.0.1 -p 18853 +tls +tls-ca=ninlil-cert.pem +tls-hostname=localhost www.example.test A +noall +answer
   ```

   ```text
   www.example.test.	3600	IN	A	192.0.2.10
   ```

   Without `+tls-ca`, `dig` stops with `TLS peer certificate verification ... failed: self-signed
   certificate`. That is the expected refusal of an untrusted certificate.
2. DNS-over-HTTPS. The `dns` parameter is the query in base64url. This one asks for `www.example.com`
   type A. The status line and the media type show a DNS answer:

   ```bash
   curl -s -i --cacert ninlil-cert.pem --resolve localhost:18443:127.0.0.1 'https://localhost:18443/dns-query?dns=AAABAAABAAAAAAAAA3d3dwdleGFtcGxlA2NvbQAAAQAB' | head -6
   ```

   ```text
   HTTP/2 200 
   content-type: application/dns-message
   date: Fri, 30 Oct 2026 19:20:20 GMT
   server: Kestrel
   cache-control: private, max-age=300
   ```

   `max-age` is the smallest TTL in the answer (RFC 8484 §5.1). `private` tells a shared HTTP cache
   not to reuse the answer for another client.
3. DNS-over-QUIC. Ask the service list, then the socket:

   ```bash
   curl -k https://127.0.0.1:18443/api/services -H 'Authorization: Bearer <token>'
   ss -lun 'sport = :18853'
   ```

   ```text
   [{"name":"dns-over-https","category":"Transport","isRunning":true},{"name":"dns-over-quic","category":"Transport","isRunning":true}, ...
   ```

   ```text
   State  Recv-Q Send-Q Local Address:Port  Peer Address:Port
   UNCONN 0      0                  *:18853            *:*
   ```

   The `ss` command lists several UDP sockets on the port, because QUIC opens more than one. If
   `isRunning` is `false` the log says why, for example that `libmsquic` is missing. Test a real
   query with a DoQ client of your choice.
4. DNSCrypt. A client finds the server by asking for the TXT record of the certificates over UDP
   or TCP. Two records answer, one for each cipher:

   ```bash
   dig @127.0.0.1 -p 15443 2.dnscrypt-cert.dnscrypt.example.net TXT +noall +comments | grep -E 'status|ANSWER:'
   ```

   ```text
   ;; ->>HEADER<<- opcode: QUERY, status: NOERROR, id: 967
   ;; flags: qr; QUERY: 1, ANSWER: 2, AUTHORITY: 0, ADDITIONAL: 0
   ```

   To test a full encrypted query, add the stamp as a forwarder on a second Ninlil host (see
   [Forwarding and recursion](#forwarding-and-recursion)) and ask that host for a name. The first
   host's query log then shows `"protocol":"DnsCrypt"` for it (check 5).
5. Every query appears in the query log with its transport:

   ```bash
   curl -k 'https://127.0.0.1:18443/api/query-log?take=5' -H 'Authorization: Bearer <token>'
   ```

   ```text
   {"items":[{"id":"...","occurredAtUtc":"...","clientAddress":"127.0.0.1","protocol":"Https","queriedName":"www.example.com.", ...
   ```

   `protocol` is `Udp`, `Tcp`, `Tls`, `Https`, `Quic` or `DnsCrypt`.
6. DNS64. Ask for the AAAA record of a name that has only an A record:

   ```bash
   dig @127.0.0.1 -p 15300 ipv4.google.com AAAA +noall +comments +answer
   ```

   ```text
   ;; flags: qr rd ra; QUERY: 1, ANSWER: 7, AUTHORITY: 0, ADDITIONAL: 1
   ipv4.google.com.	...	IN	CNAME	ipv4.l.google.com.
   ipv4.l.google.com.	...	IN	AAAA	64:ff9b::8efb:1071
   ```

   (Your addresses differ.) The last 32 bits of each synthesized address are the IPv4 address. With
   a prefix of your own, the addresses start with it. The reply has no `ad` flag, because nobody
   signed the invented records.
7. PROXY protocol (step 7). After the restart, a query that arrives through the proxy shows the
   client's address, not the proxy's, as `clientAddress` in the query log (check 5). A connection
   from an address that is not in `knownProxies` or `knownNetworks` shows its own address.

#### Behaviour worth knowing

- **Every transport beyond plain DNS is off by default**, and each switch applies at once. A port,
  a certificate or a limit is read when the transport's listener starts: change it, then restart
  that listener with `POST /api/services/<name>/restart` (`dns-over-tls`, `dns-over-quic`,
  `dnscrypt-udp`, `dnscrypt-tcp`) or restart Ninlil. The ports table in [Ports](#ports) lists the
  defaults.
- **Each transport can listen on chosen addresses.** The `ListenAddresses` setting limits it, for
  example to keep it off a WAN interface. An address the host does not have is logged and
  skipped. DoH follows the web listener's addresses.
- **DNS-over-QUIC.** A query must carry message ID 0 (RFC 9250 §4.2.1). Ninlil closes the
  connection with `DOQ_PROTOCOL_ERROR` for another ID, or for a message it cannot decode, and sends no
  answer. It resets a stream that it gives up on, such as a missed deadline, with
  `DOQ_INTERNAL_ERROR`. It closes a connection over the connection limit with `DOQ_EXCESSIVE_LOAD`.
  Ninlil does not enable 0-RTT. Ninlil's own DoQ forwarder sends ID 0, as the RFC says.
- **`libmsquic` 2.4.20 or later.** An older version aborts the whole process when a peer advertises
  a UDP payload size of 1200 over IPv4, such as the Quad9 service over IPv4. The container image
  ships a fixed version. For that reason the forwarder list in the console has no
  DNS-over-QUIC entry for Cloudflare or Quad9.
- **DNS-over-HTTPS.** A GET carries the query in the `dns` parameter and a POST carries it in the
  body with the media type `application/dns-message`. Ninlil compares the media type without case
  and ignores parameters such as `; charset=utf-8`; another type answers `415`. A bad `dns`
  parameter answers `400`. A rate-limited query answers `429`. A denied client gets no answer at all.
- **DNSCrypt.**
  - The server offers two ciphers (XChaCha20-Poly1305 and XSalsa20-Poly1305) and publishes one
    certificate for each. A certificate is valid for 28 hours. Ninlil issues a replacement every 24
    hours and drops the old one, with its secret key, when it expires.
  - A DNSCrypt TCP connection carries one query. The server closes it after the answer
    (DNSCrypt v2 specification, section 6), so a client opens a new connection per query.
  - The identity file exists only after you enable DNSCrypt.
  - As a client (a forwarder defined by a stamp), Ninlil pads each query: to at least 256 bytes over
    UDP, and by a random 1 to 256 bytes over TCP, as the specification asks. It fetches the
    certificate over UDP first and over TCP when UDP fails or is truncated.
- **PROXY protocol on DNS-over-TLS.** The header must end with CRLF within its first 107 characters
  (PROXY protocol specification, section 2.1); a longer line closes the connection. A header from an address
  that is not in the list is never read, so it cannot forge a client address. A client that
  does not send a header on a trusted address is refused.
- **DNS64 details.** Under the Well-Known Prefix `64:ff9b::/96`, Ninlil leaves out IPv4 addresses
  that are not globally reachable (private, loopback, link-local, documentation and similar,
  RFC 6052 §3.1). A network-specific prefix embeds every address. Ninlil ignores a real AAAA
  record inside `::ffff:0:0/96`, because an IPv6-only client cannot use it, and counts the name as
  having no AAAA record (RFC 6147 §5.1.4). A synthesized record gets the smaller of the A
  record's TTL and the TTL of the SOA record in the empty AAAA answer, or 600 seconds without an
  SOA (RFC 6147 §5.1.7). Ninlil does not synthesize when the A answer fails DNSSEC validation (the
  client gets `SERVFAIL`, or an empty answer if it set CD), or when the client set both DO and CD.
  Synthesis is for AAAA queries that get an empty answer, never for `NXDOMAIN` or for authoritative
  data.

#### Settings

- [DNS transports](./ENVIRONMENT.md#dns-transports): the switch, port, addresses, limits and
  certificate of each transport. The DoH port and certificate rows apply only when DoH has its
  own listener, which Ninlil does not use: set the web listener's values in [Web
  host](./ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding).
- [Query pipeline & cross-cutting DNS behavior](./ENVIRONMENT.md#query-pipeline--cross-cutting-dns-behavior):
  `NINLIL_DNS_SERVER_DNS64_ENABLE` and `NINLIL_DNS_SERVER_DNS64_SYNTHESIS_PREFIX`.
- The list-valued settings (`ListenAddresses`, the self-signed certificate's alternative names, the
  PROXY protocol sources) have no variable. Set them in the console, through the API, or in
  `appsettings.json`.

#### When something goes wrong

- *`dig +tls` says `self-signed certificate`.* Pass `+tls-ca=ninlil-cert.pem` as in the check, or
  use a certificate your clients trust (see "Certificates").
- *A client says the certificate name does not match.* The self-signed certificate names only
  `localhost`. Set `certificate.selfSignedSubjectAlternativeNames` to the name clients use, delete
  `protocol-tls-certificate.pfx` from the data directory and restart: a saved certificate is not
  made again when the names change. Or use a certificate for your name.
- *The switch answers `200` but nothing listens.* The log has the reason. For DoQ it names
  `libmsquic`. For DNSCrypt it names the missing provider name.
- *Ninlil runs but a transport does not answer, and the log has an Error such as
  `DNS-over-TLS could not listen on :: port 853: Permission denied`.* An enabled transport could not
  bind its port when Ninlil started: `Permission denied` for a port below 1024 without the right to
  use it, or `Address already in use`. Ninlil logs one Error naming the transport, the address, the
  port and the reason, reports the `transports` check of `GET /status` as `Unhealthy`, and keeps
  running: the other transports, DHCP, the REST API and the admin console are not affected. Only the
  web listener failing to bind stops Ninlil, because nothing could fix it from inside. Fix the
  transport like this:
  1. Read the reason in the log: the Error line names the transport, address, port and reason. With
     a token that holds the `status:read` permission, `curl http://127.0.0.1:18080/status -H 'Authorization: Bearer <token>'`
     (use your web port) lists the same in the `transports` component. Without a token it shows
     only `"status":"Unhealthy"`.
  2. Change the port or the listen addresses (`PATCH /api/transport-settings/<transport>` or the setting in
     `appsettings.json`), or free the port: `ss -lntup | grep ':<port>'` shows the program that holds
     it.
  3. Restart Ninlil. A transport that failed to bind is not retried while Ninlil runs.
  4. Check that it worked: `GET /status` reports `transports` as `Healthy`.
- *DoQ clients are cut off.* A query with a message ID other than 0 closes the connection.
  Check the client.
- *A DNSCrypt client cannot find the server.* Check that the stamp's provider name has the
  `2.dnscrypt-cert.` prefix, and that the address in the stamp reaches the DNSCrypt port.
- *The client address in the query log is the proxy.* The PROXY setting needs a restart, and the
  proxy's address must be in `knownProxies` or `knownNetworks`.
- *DNS64 gives no AAAA record.* Forwarding is on and answered the query, the name has a real AAAA
  record, or the only A records are non-global addresses under `64:ff9b::/96`.

### DHCP, DHCPv6 and Router Advertisement

#### What it does

A device that joins a network needs an address, a router and a DNS server. Ninlil can hand these
out. Four services take part, and each one is off until you turn it on:

| Service | Answers | Standard | Default port |
|---|---|---|---|
| DHCPv4 | A device that asks for an IPv4 address, router and DNS server | RFC 2131 | UDP `67` (server), `68` (client) |
| DHCPv6 | A device that asks for an IPv6 address, or for a block of addresses to hand out on a network behind it | RFC 8415 | UDP `547` (server), `546` (client) |
| Router Advertisement (RA) | A device that builds an IPv6 address itself (SLAAC) and needs the network's prefix, router and DNS server | RFC 4861, RFC 8106 | none, it uses raw ICMPv6 |
| TFTP | A device that fetches a boot file | RFC 1350 | UDP `69`, see [TFTP and PXE](#tftp-and-pxe) |

The services run on Linux. For macOS and Windows, see [Download a binary](#download-a-binary) and
[Windows](#windows). Words this guide uses:

- A **scope** is one pool of addresses plus the settings that go with them: router, DNS servers
  and lease times. A scope is for IPv4 or for IPv6. A scope of kind `PrefixDelegation` hands out
  blocks of IPv6 addresses (prefixes) instead of single addresses.
- A **lease** is an address that Ninlil has granted to one device for a limited time. A device
  renews it before it ends.
- A **reservation** ties one address to one device, so the device always gets that address.
- A **client identifier** is how Ninlil recognises a device: its hardware (MAC) address for
  DHCPv4, or its DHCP unique identifier (DUID) for DHCPv6.
- A **custom option** is an extra DHCP option that a scope sends to every device. A **tag rule**
  sends options to the devices that match its criteria, such as a vendor class.
- A **relay agent** forwards the requests of one network to a DHCP server on another network.
  Each protocol runs either as Ninlil's DHCP server or as a relay agent, never both.

When a scope has a DNS zone, Ninlil also writes an `A` or `AAAA` record for each leased device
(see "Register leased names in DNS" below).

#### Before you start

1. A running Ninlil and a signed-in API token, with the placeholders `<token>` and `<zone-id>` and
   the Quick start values, as in [Zones and records](#zones-and-records) "Before you start". A
   Linux host.
2. A user that holds `dhcpscopes:write`, `dhcpleases:read`, `dhcpsettings:write`,
   `dhcpservices:manage`, `dhcptagrules:write` and `dhcpleases:manage`. The bootstrap administrator
   holds every permission.
3. A network where Ninlil may answer DHCP. Turn off any other DHCP server on that network first:
   two servers answer the same request, and a device takes either answer. For a first try, use a
   network that has no other DHCP server.
4. The numbers for your network. The commands use an example network `192.168.50.0/24`: Ninlil on
   `192.168.50.2`, the router on `192.168.50.1`, and the pool `192.168.50.100` to
   `192.168.50.150`. Replace them with yours. Check the host's address:

   ```bash
   ip -4 -brief address
   ```

   ```text
   lo               UNKNOWN        127.0.0.1/8
   eth0             UP             192.168.50.2/24
   ```

   (Your values differ.) Ninlil's address on that network is the **Server Identifier** that a
   device names when it accepts an offer.
5. The right to open the ports. DHCPv4 uses UDP port `67`, which is below `1024`: run Ninlil as
   root or grant `CAP_NET_BIND_SERVICE` (see [Ports](#ports)). Router Advertisement opens a raw
   socket and needs `CAP_NET_RAW` or root. In a container, DHCP needs host networking (see
   [DHCP and Router Advertisement](#dhcp-and-router-advertisement) under Running with Docker).
6. Optional, for the DNS step: the `example.test` zone from [Quick start](#quick-start) and the
   `zones:write` permission.

#### Steps

**Turn on DHCPv4.**

1. Start the service without a restart. It runs until you stop it or restart Ninlil:

   ```bash
   curl -k -X POST https://127.0.0.1:18443/api/dhcp-services/Dhcpv4/start -H 'Authorization: Bearer <token>'
   ```

   ```text
   {"kind":"Dhcpv4","enabled":false,"isRunning":true}
   ```

   `enabled` is the saved switch, `isRunning` is the running state. In the console: **DHCP Services**.
2. Save the switch, so the service starts with Ninlil. Set `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE=true`
   when you start Ninlil, or save it through the API and restart:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/protocol -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
   ```

   ```text
   {"note":"Written to the configuration file. This setting is bound via IOptions, not IOptionsMonitor, so a restart is required before this change takes effect."}
   ```

   In the console: **Presets & Settings**, **DHCP**, the **DHCPv4 Protocol** card.

**Create a scope.**

3. Create the scope for your network. Durations are written `hours:minutes:seconds`, or
   `days.hours:minutes:seconds`, so `08:00:00` is eight hours and `1.00:00:00` is one day:

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-scopes -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"lan","protocolVersion":"IPv4","rangeStartAddress":"192.168.50.100","rangeEndAddress":"192.168.50.150","subnetPrefixLength":24,"defaultLeaseDuration":"08:00:00","maxLeaseDuration":"1.00:00:00","gatewayAddresses":["192.168.50.1"],"dnsServerAddresses":["192.168.50.2"]}'
   ```

   ```text
   {"id":"<scope-id>","name":"lan","protocolVersion":"IPv4","rangeStartAddress":"192.168.50.100","rangeEndAddress":"192.168.50.150", ...
   ```

   Copy the `id`. The next commands call it `<scope-id>`. `subnetPrefixLength` `24` gives the
   subnet mask `255.255.255.0`. `defaultLeaseDuration` is the lease for a device that asks for
   none, and `maxLeaseDuration` caps what a device may ask for. In the console: **DHCP Scopes**.

**Reserve an address for one device.**

4. Give a device with the hardware address `aa:bb:cc:dd:ee:10` the fixed address
   `192.168.50.20`. Ninlil accepts the hardware address as `aa:bb:cc:dd:ee:10`,
   `aa-bb-cc-dd-ee-10`, `aabb.ccdd.ee10` or `aabbccddee10` and stores it as
   `aa:bb:cc:dd:ee:10`:

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-scopes/<scope-id>/reservations -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"clientIdentifier":"AA-BB-CC-DD-EE-10","ipAddress":"192.168.50.20","hostName":"printer"}'
   ```

   ```text
   {"id":"...","scopeId":"<scope-id>","clientIdentifier":"aa:bb:cc:dd:ee:10","ipAddress":"192.168.50.20","hostName":"printer", ...
   ```

   In the console: **DHCP Scopes**, the **Manage reservations** button of the scope.

**Register leased names in DNS (optional).**

5. Name the zone for the scope. A device that sends a host name then gets an `A` record in that
   zone while its lease is active. The zone must be a `Primary` or `CloudPrimary` zone:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-scopes/<scope-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"dnsZoneId":"<zone-id>"}'
   ```

   The answer is `204` with no body.

**Send one more option to every device of the scope (optional).**

6. A custom option has a tag (the DHCP option number) and a value written in hexadecimal. This one
   sends the domain name (option `15`, plain text). `6C616E2E6578616D706C652E74657374` is
   `lan.example.test`:

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-scopes/<scope-id>/custom-options -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"optionTag":15,"valueHex":"6C616E2E6578616D706C652E74657374"}'
   ```

   ```text
   {"id":"...","scopeId":"<scope-id>","optionTag":15,"valueHex":"6C616E2E6578616D706C652E74657374","isDisabled":false,"valueSource":"Custom", ...
   ```

   A value of `"valueSource":"ServerAddress"` (and no `valueHex`) sends Ninlil's address
   instead, for example as the time server (option `42`). In the console: **DHCP Scopes**, edit
   the scope, custom options.

**Send an option to some devices only (optional).**

7. A tag rule matches devices by what they send. This rule matches every device whose vendor
   class starts with `printer-`:

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-tag-rules -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"printers","vendorClassIdentifierMatch":"printer-","vendorClassIdentifierMatchMode":"Prefix"}'
   ```

   ```text
   {"id":"<rule-id>","name":"printers","scopeId":null,"priority":0, ...
   ```

8. Add the option the rule sends. `C0A83201` is `192.168.50.1`:

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-tag-rules/<rule-id>/options -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"optionTag":42,"valueHex":"C0A83201"}'
   ```

   ```text
   {"id":"...","tagRuleId":"<rule-id>","optionTag":42,"valueHex":"C0A83201","valueSource":"Custom", ...
   ```

   In the console: **DHCP Tag Rules**.

**Turn on DHCPv6 (optional).**

9. Start the service, then save the switch with the `dhcpv6-protocol` section:

   ```bash
   curl -k -X POST https://127.0.0.1:18443/api/dhcp-services/Dhcpv6/start -H 'Authorization: Bearer <token>'
   curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/dhcpv6-protocol -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
   ```

   (The environment variable is `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV6_ENABLE`.)
10. Create a scope for the addresses. A DHCPv6 scope has no gateway, because IPv6 routers announce
    themselves (see Router Advertisement below):

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-scopes -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"lan6","protocolVersion":"IPv6","rangeStartAddress":"fd00:50::1000","rangeEndAddress":"fd00:50::10ff","subnetPrefixLength":64,"defaultLeaseDuration":"08:00:00","maxLeaseDuration":"1.00:00:00","dnsServerAddresses":["fd00:50::2"]}'
    ```

11. To delegate blocks of addresses to routers (prefix delegation), create a second scope of kind
    `PrefixDelegation`. The range is the pool of prefixes, `subnetPrefixLength` is the size of the
    whole pool, and `delegatedPrefixLength` is the size of each block. This pool is
    `2001:db8:100::/48` and delegates `/56` blocks, 256 of them:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-scopes -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"delegation","protocolVersion":"IPv6","kind":"PrefixDelegation","rangeStartAddress":"2001:db8:100::","rangeEndAddress":"2001:db8:100:ff00::","subnetPrefixLength":48,"delegatedPrefixLength":56,"defaultLeaseDuration":"08:00:00","maxLeaseDuration":"1.00:00:00"}'
    ```

    The reservations of a DHCPv6 scope use the device's DUID as `clientIdentifier`, for example
    `00:03:00:01:aa:bb:cc:00:06:01`. A `PrefixDelegation` scope takes no reservations.

**Advertise the network with Router Advertisement (optional).**

12. Router Advertisement sends nothing until there is something to say. Set the content. This
    turns on the "O" flag (devices ask DHCPv6 for the rest), makes this host a default router for
    30 minutes, sets the link MTU, and announces the DNS server and search domain (RFC 8106):

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/ra -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"managedAddressConfiguration":false,"otherConfiguration":true,"routerLifetime":"00:30:00","linkMtu":1500,"recursiveDnsServers":["fd00:50::2"],"dnsSearchListDomains":["example.test"]}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required."}
    ```

    In the console: **Presets & Settings**, **DHCP**, the **Router Advertisement Content** card.
13. Add the prefix that devices build their addresses from. The API and the console cannot set
    prefixes: add them to `appsettings.json` (see [Configuration](#configuration)), next to any
    `Ra` settings the previous step saved. Ninlil reads the change at once:

    ```json
    {
      "DhcpServer": {
        "Ra": {
          "Prefixes": [ { "Prefix": "fd00:50::", "PrefixLength": 64 } ]
        }
      }
    }
    ```

14. Start the service and save the switch with the `ra-protocol` section:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/dhcp-services/RouterAdvertisement/start -H 'Authorization: Bearer <token>'
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/ra-protocol -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

    (The environment variable is `NINLIL_DHCP_SERVER_PROTOCOL_RA_ENABLE`.)

**Use Ninlil as a relay agent instead (optional).**

15. A relay agent forwards the requests of its network to a DHCP server somewhere else. On the
    host that acts as the relay, set `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE=true` and
    `NINLIL_DHCP_SERVER_RELAY_DHCPV4_ENABLE=true`, start Ninlil, and save the relay settings:
    the upstream servers, the relay's address on the clients' network (`giaddr`), and an
    optional circuit ID that the server can match:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/relay -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"upstreamServerAddresses":["192.168.50.2"],"relayAgentAddress":"192.168.50.3","relayAgentCircuitId":"port-7"}'
    ```

    Restart Ninlil. On the upstream server, a scope must cover the relay address
    (`192.168.50.3` falls in the scope created above). The DHCPv6 relay works the same way with
    the `dhcpv6-relay` section (`NINLIL_DHCP_SERVER_RELAY_DHCPV6_ENABLE`, the upstream addresses
    and `relayLinkAddress`).

#### Check that it worked

1. On a device on the network, renew the lease the way the operating system does it, for example
   `sudo dhclient -v <interface>` on Linux or `ipconfig /renew` on Windows.
2. List the leases:

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-leases -H 'Authorization: Bearer <token>'
   ```

   ```text
   {"items":[{"id":"...","protocolVersion":"IPv4","scopeName":"lan","clientIdentifier":"aa:bb:cc:00:00:01","ipAddress":"192.168.50.100","hostName":"laptop","state":"Active","leaseObtainedAtUtc":"2026-10-30T23:40:43.1061981+00:00","leaseExpiresAtUtc":"2026-10-31T07:40:43.1061981+00:00", ...
   ```

   (Your values differ.) A device appears with `"state":"Active"` after its request. In the
   console: **DHCP Leases**.
3. See how full a scope is:

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-scopes/<scope-id>/statistics -H 'Authorization: Bearer <token>'
   ```

   ```text
   {"scopeId":"<scope-id>","scopeName":"lan","protocolVersion":"IPv4","kind":"AddressAssignment","totalSlots":"51","leasedCount":1,"offeredCount":0,"reservedCount":1,"availableSlots":"50"}
   ```

4. If you chose the DNS step, ask Ninlil for the device's name (use your DNS port; `15300` is the
   Quick start port):

   ```bash
   dig @127.0.0.1 -p 15300 laptop.example.test A +noall +answer
   ```

   ```text
   laptop.example.test.	300	IN	A	192.168.50.100
   ```

5. If you chose Router Advertisement, read the settings back and look at the device. On a Linux
   device in the network, `ip -6 address show dev <interface>` lists an address in the advertised
   prefix, and `ip -6 route show default` lists this host as the router when the router lifetime
   is above zero.

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-settings/ra -H 'Authorization: Bearer <token>'
   ```

   ```text
   {"managedAddressConfiguration":false,"otherConfiguration":true,"routerLifetime":"00:30:00","currentHopLimit":64,"reachableTime":"00:00:00","retransTimer":"00:00:00","linkMtu":1500,"recursiveDnsServers":["fd00:50::2"], ...
   ```

6. Check the services:

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-services -H 'Authorization: Bearer <token>'
   ```

   ```text
   [{"kind":"Dhcpv4","enabled":true,"isRunning":true},{"kind":"Dhcpv6","enabled":true,"isRunning":true},{"kind":"RouterAdvertisement","enabled":true,"isRunning":true}, ...
   ```

#### Behaviour worth knowing

- **Which scope serves a device.** A request that arrives through a relay is served from the scope
  whose subnet holds the relay address, and from no other. A request from the local network is
  served, in this order, from: the scope that holds an enabled reservation for the device; the
  scope that holds the address the device already has (`ciaddr`, a renewing device); the scope that
  holds a lease row for the device; and the only enabled scope, if there is exactly one. **With
  two or more enabled scopes, a new device on the local network that has no reservation gets no
  reply.** Ninlil listens on one socket for all networks and does not know which network a
  request came from. Put a reservation on the device, or serve the other network through a relay.
  DHCPv6 follows the same rules with the relay's link address, and a request for prefixes (prefix
  delegation) also falls back to the lease row and the only scope.
- **One Server Identifier.** Ninlil names one address as the server in every reply. By default
  it is the first IPv4 address of the first interface that is up and not loopback. On a host with
  several networks, or with container bridges, set `NINLIL_DHCP_SERVER_CORE_SERVER_IDENTIFIER_ADDRESS`
  to the address your clients can reach. DHCPv6 picks its DUID and the server's IPv6 address from
  the first suitable interface in the same way, and has settings to override them
  (`NINLIL_DHCP_SERVER_CORE_SERVER_DUID` and `NINLIL_DHCP_SERVER_CORE_SERVER_IPV6_ADDRESS`).
- **Where the reply goes.** A reply to a relayed request goes to the relay. A refusal (`DHCPNAK`)
  is always broadcast. Any other reply is broadcast when the device asked for a broadcast reply,
  sent straight to the device's address when it already has one, and broadcast otherwise (RFC 2131
  Section 4.1). Ninlil cannot unicast to a device that has no address yet, so it broadcasts.
- **A device is known by its client identifier.** For DHCPv4 it is the Client-identifier option
  (option `61`) when the device sends one, and the hardware address otherwise. Many devices send
  option `61` as the type byte `01` followed by the hardware address. Ninlil then keeps
  `01:aa:bb:cc:dd:ee:10`, not `aa:bb:cc:dd:ee:10`, and a reservation written with the plain
  hardware address does not match. Read the identifier in the lease list and use that. An
  identifier is at most 85 bytes. A request with a longer one gets no reply, and the log gets one
  Warning a minute that counts the refusals: `Refused 1 DHCPv4 request(s) since the last warning
  because the client identifier is longer than 85 bytes`.
- **A reservation wins.** A device with a reservation can lease only its reserved address, and no
  other device gets that address. Ninlil does not check the reserved address against the scope's
  range or its IP version, so the address may lie outside the pool (a fixed address often does).
  Check the address: a reservation that holds an address of the other IP version (an IPv4
  address in a DHCPv6 scope) makes the server fail that device's requests with an error in the log.
  The reservation's `hostName` is a note for you. DNS registration uses the host name the device
  sends.
- **Ninlil does not check scopes against each other.** It does not check that a range lies inside
  its subnet or that two scopes do not overlap. Read the numbers twice.
- **Lease length.** A DHCPv4 device may ask for a lease time. It gets that time, capped at
  `maxLeaseDuration`, or `defaultLeaseDuration` when it asks for none. DHCPv6 ignores the times a
  device suggests: the preferred and valid lifetimes are `defaultLeaseDuration` capped at
  `maxLeaseDuration`, the renewal time (T1) is half of it and the rebinding time (T2) is 0.8 of it.
  An offered address stays held for that device for `NINLIL_DHCP_SERVER_CORE_OFFER_HOLD_DURATION`
  (30 seconds).
- **Which options a reply carries.** The subnet mask, router and DNS servers are sent only when the
  device lists them in its Parameter Request List (DHCPv4) or Option Request option (DHCPv6, for the
  DNS servers), or when it sends no list. The server identifier and the lease time are always sent.
  Custom options and tag-rule options are always sent, listed or not.
- **Custom options.** DHCPv4: tags `1` to `254`, except `52`, `53` and `82`, which Ninlil or a
  relay sets; the value is at most 255 bytes. Tags `66` (TFTP server name) and `67` (boot file
  name) are text: printable ASCII only. A binary address belongs in tag `150`. DHCPv6: codes `1` to
  `255` except the ones Ninlil writes itself, with a value of at most 65535 bytes; a code above 255
  cannot be set. Code `24` (domain search list) takes domain names in wire form, for example
  `076578616D706C65047465737400` for `example.test`. The API refuses a value
  that breaks these rules with `400`.
- **Tag rules.** A rule matches when every criterion it sets matches. A rule with no criterion
  matches every device it applies to. The criteria are the vendor class and the user class (exact
  or prefix match, case-sensitive), the relay circuit ID and remote ID, and, for DHCPv6 only, the
  relay interface ID and the vendor class enterprise number. Matching rules apply in ascending
  `priority`, then creation time, and the last rule that sets a tag wins; a rule's option beats a
  custom option with the same tag. A rule with no `scopeId` is a DHCPv4 rule that applies in every
  DHCPv4 scope, and the DHCPv6 engine ignores it. A DHCPv6 rule names a DHCPv6 scope, so make one
  rule per DHCPv6 scope. The API refuses a criterion that does not fit the rule's protocol with
  `400`. A stored rule that already breaks this never matches, and every `PATCH` of it returns
  `400` until the patch clears the criterion.
- **Declined addresses.** A device that finds its new address already in use sends `DHCPDECLINE`.
  Ninlil then keeps that address out of the pool for
  `NINLIL_DHCP_SERVER_CORE_DECLINED_ADDRESS_QUARANTINE_DURATION` (10 minutes). The lease list shows
  the address with the client identifier `declined:<address>`, and the console labels it
  **Declined address**. To end the quarantine early, release that row:
  `POST /api/dhcp-leases/<lease-id>/release` (the console button is **End the quarantine early**).
  A `DHCPDECLINE` or `DHCPRELEASE` for an address that is not the sender's lease changes nothing; for a
  decline the log gets a Warning, `Ignored DECLINE of <address> from <identifier> in scope '<name>':
  not its lease.`
- **DHCP has no authentication here.** Ninlil does not implement DHCP authentication (RFC 3118 for
  DHCPv4), so a host on the network that sends a `DHCPRELEASE` or `DHCPDECLINE` with another
  device's identifier ends that device's lease or quarantines its address. Keep untrusted hosts off
  the network that Ninlil serves.
- **Answers that are not an address.** A DHCPv4 `REQUEST` for an address Ninlil cannot confirm gets a
  `DHCPNAK`; a request from a device that no scope covers gets no reply. In DHCPv6, a `SOLICIT`
  that a scope cannot serve gets an `ADVERTISE` with the status `NoAddrsAvail` (or `NoPrefixAvail`)
  in the affected association; a `REQUEST`, `RENEW` or `REBIND` that cannot be confirmed gets
  `NoBinding`; a `SOLICIT` that no scope covers gets no reply. Ninlil handles the first
  address association (`IA_NA`) and the first prefix association (`IA_PD`) of a request, and
  ignores temporary addresses (`IA_TA`).
- **Rapid Commit.** DHCPv6 Rapid Commit (a two-message exchange) is off. Turn it on with
  `NINLIL_DHCP_SERVER_CORE_ENABLE_DHCPV6_RAPID_COMMIT=true` only when the network has one DHCPv6
  server: with several, all but one commit a lease that the device does not use (RFC 8415
  Section 21.14).
- **DNS registration.** Ninlil registers the host name a device sends: the Host Name option for
  DHCPv4, the Client FQDN option for DHCPv6. The name becomes a label of the scope's zone: letters
  are lowercased, any character that is not a letter, digit or hyphen becomes a hyphen, and the
  label is at most 63 characters. The record has a TTL of 300 seconds. Ninlil removes the record when the lease is
  released, declined or expired, and keeps the PTR record in step when the zone manages PTR
  records (see [Zones and records](#zones-and-records)). Registration works for DHCPv6 leases
  while DHCPv4 runs as a relay.
  **A name that is taken gets no record.** If the name holds a record that Ninlil's DHCP did not
  write (an `A`, `AAAA`, `CNAME`, `NS` or `TXT` record you entered), or belongs to another
  device's active lease, Ninlil writes no record and logs a Warning on every renewal, `DHCP->DNS
  registration refused for client <identifier> with host name <name>: owner name <fqdn> is
  unavailable because ...`. The device keeps its lease.
  In its DHCPv6 reply to a Client FQDN option, Ninlil sets `S=1` when it will try to register the
  name (the scope has an eligible zone and the device sent a name) and `N=1` otherwise (RFC 4704
  Section 4.1). `S=1` promises an attempt, not a result: a refused name still gets `S=1`.
- **Leases expire in a sweep.** No device announces that a lease ran out, so Ninlil checks every
  `NINLIL_DHCP_SERVER_CORE_LEASE_EXPIRY_SWEEP_INTERVAL` (one minute). An expired lease loses its
  DNS record at that sweep.
- **Which settings need a restart.** All DHCP, relay, TFTP and RA transport settings, and all
  `Core` settings except the sweep interval, are read once at start. The `PATCH` answers say so,
  and `GET` shows the value that runs until the restart. These apply without a restart: the
  Router Advertisement content (including `Prefixes` in `appsettings.json`) from the next
  advertisement, and the sweep interval from the next sweep. The start and stop calls of the
  **DHCP Services** page change the running state without saving anything.
- **A scope is removed with its settings, not its history.** Deleting a scope deletes its
  reservations and custom options. Its leases stay as records. A tag rule that names the scope
  stays and never matches.
- **A cluster does not copy DHCP data** (see [Clustering, events and webhooks](#clustering-events-and-webhooks)).
  Each Ninlil instance that serves DHCP needs separate scopes, and two instances must not serve the
  same range. On MongoDB, do not point two instances at one database (see
  [Storage](#storage)).
- **Relay agent.**
  - A protocol runs as a relay or as Ninlil's server, chosen once at start. The DHCPv4 and DHCPv6
    choices are independent, and the listener of the protocol must be on too.
  - The relay sends each request to every upstream server. It relays a reply only when the reply's
    source address is one of the listed upstream addresses, so list the address the server answers
    from. With no upstream address, the relay drops every request and logs a Warning. The
    upstream list has no environment variable: save it with the `relay` or `dhcpv6-relay`
    section, or in `appsettings.json`.
  - Set `relayAgentAddress` (`giaddr`) to the relay's address on the clients' network. The upstream
    server picks the scope from it. Left empty, the relay uses the address it detects for itself,
    which is often the wrong network on a host with several. With a circuit ID or remote ID
    set, the relay adds the Relay Agent Information option (option `82`, RFC 3046) to each client
    request, and a tag rule on the server can match it (`relayAgentCircuitIdHex`:
    `706F72742D37` is `port-7`). A request that already carries a relay address came from
    another relay: the relay keeps that address and adds no option. The DHCPv6 relay supports one
    hop.
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
- **Router Advertisement.**
  - Ninlil advertises nothing while the content is empty: no prefix, no router lifetime, no flag,
    no MTU, no DNS server, no search domain, no route. The router lifetime is `00:00:00` by default, which says this
    host is not a default router. The "M" flag tells devices to ask DHCPv6 for addresses. The "O"
    flag tells them to ask DHCPv6 for the rest.
  - Ninlil sends an advertisement on every usable interface at a random time between the minimum
    (198 seconds) and the maximum (600 seconds) interval, and answers a device's solicitation after
    a random delay of up to half a second. The API requires a minimum of at least 3 seconds and at
    most 0.75 times the maximum, and a maximum from 4 to 65535 seconds.
  - **Size limits.** An advertisement is at most 1240 octets, so no network drops it as a
    fragment. A longer one is split into several advertisements. One option cannot be split, so
    the API refuses more than 75 DNS server addresses and a search list that encodes to more than
    1208 octets with `400`. Other limits: router lifetime at most 65535 seconds, reachable time at
    most one hour, link MTU at least 1280.
  - **Per-interface content.** `interfaceOverrides` changes the content for one interface by name
    (`eth0`), and `"exclude":true` stops advertisements on it. A device that solicits from a
    unicast address still gets an advertisement with the default content, because Ninlil does not
    know which interface a solicitation arrived on.
  - Ninlil accepts a solicitation only from a link-local, unspecified or loopback address. The
    process needs `CAP_NET_RAW` or root, because it opens a raw ICMPv6 socket.
- **Statistics are computed when you ask.** `/api/dhcp-scopes/<scope-id>/statistics` counts the
  leases at that moment. `totalSlots` and `availableSlots` are strings, because an IPv6 pool can
  hold more slots than a JSON number carries.

#### Settings

- [DHCP, DHCPv6, Router Advertisement & TFTP](./ENVIRONMENT.md#dhcp-dhcpv6-router-advertisement--tftp):
  the switches, ports, server identity, hold and quarantine times, sweep interval, Rapid Commit,
  relay and RA settings.
- The list-valued settings have no variable: the relay's upstream addresses, the RA prefixes,
  DNS server addresses, search domains, routes and interface overrides. Save the ones the API
  carries with `PATCH /api/dhcp-settings/...`, and set the RA prefixes in `appsettings.json`.
- In the console: **DHCP Services**, **DHCP Scopes**, **DHCP Tag Rules**, **DHCP Leases**, and
  **Presets & Settings**, **DHCP**.

#### When something goes wrong

- *A device gets no address.* Work through the list:
  1. `GET /api/dhcp-services` must show the service `isRunning`.
  2. The scope must be enabled (`"isDisabled":false`) and have a free address (`availableSlots`).
  3. With two or more enabled scopes, a new local device needs a reservation or a relay (see
     "Which scope serves a device").
  4. The log has the reason when Ninlil refused the request: `Refused ... because the client
     identifier is longer than 85 bytes`, or `No address is currently available in scope '<name>'
     to offer client <identifier>.`
- *Ninlil stops at start with `Hosting failed to start` and `System.Net.Sockets.SocketException
  (13): Permission denied`, then `Unhandled exception`.* DHCPv4, DHCPv6 or Router Advertisement is
  enabled and the process may not open its socket: ports below `1024` need root or
  `CAP_NET_BIND_SERVICE`, and Router Advertisement needs `CAP_NET_RAW` (see "Before you start"
  step 5). `SocketException (98): Address already in use` means another program holds the port.
  An enabled DHCP service that cannot bind stops Ninlil. Fix the cause, or turn the service off
  with its `Enable` setting. The start call of **DHCP Services** answers `500` and logs the same
  exception.
- *A reservation is ignored.* The identifier does not match. Compare it with the one in the lease
  list, and remember the `01:` prefix of option `61`.
- *The DHCPv6 log has `An IAADDR option's address must be an IPv6 address.`* A reservation of the
  scope holds an IPv4 address. Correct or delete it. The same fault in a DHCPv4 scope logs
  `A DHCPv4 message's address fields must each be an IPv4 address.`
- *No DNS record for a device.* The scope needs a zone of type `Primary` or `CloudPrimary`, and
  the device must send a host name. The log shows `DHCP->DNS registration refused` when the name is
  taken.
- *The relay drops everything.* The log has `Dropping a DHCPv4 request this relay would otherwise
  forward: no upstream server addresses are configured`. Save `upstreamServerAddresses` and
  restart.
- *A relayed device gets no reply.* No enabled scope on the upstream server covers `giaddr`
  (Debug log: `DHCPv4 request relayed from <address>, which no enabled scope covers; no reply`), or
  the upstream server answers from an address that is not in the list.
- *No Router Advertisement arrives.* `GET /api/dhcp-settings/ra` must show content (a prefix, a
  router lifetime, a flag, an MTU, a DNS server or a route), the service must run, and the process
  needs `CAP_NET_RAW` or root.

### TFTP and PXE

#### What it does

PXE lets a computer boot from the network. The computer asks DHCP for an address and for the name
and place of a boot file, then fetches the file over TFTP, the Trivial File Transfer Protocol
(RFC 1350). Ninlil does both parts: the [DHCP scope](#dhcp-dhcpv6-and-router-advertisement) tells
the computer which file to fetch and from where, and Ninlil's TFTP server sends the file.

The TFTP server is read-only. It serves the files below one root directory, in binary (`octet`)
mode, in 512-byte blocks. It is off by default and listens on UDP port `69`.

#### Before you start

1. A running Ninlil, a signed-in API token and the placeholders `<token>` and `<scope-id>`, as in
   [DHCP, DHCPv6 and Router Advertisement](#dhcp-dhcpv6-and-router-advertisement) "Before you
   start". A DHCPv4 scope for the network of the computers that boot.
2. A user that holds `dhcpsettings:write` and `dhcpscopes:write`. The bootstrap administrator
   holds every permission.
3. A directory with the boot files, readable by the user that runs Ninlil. This guide uses
   `/srv/tftp` and a file `boot.bin` in it. Check them:

   ```bash
   ls -l /srv/tftp
   ```

   ```text
   -rw-r--r-- 1 root root 1300 Oct 30 19:24 boot.bin
   ```

   (Your owner, size and date differ.)

4. Port `69` is below `1024`: run Ninlil as root or grant `CAP_NET_BIND_SERVICE` (see
   [Ports](#ports)).
5. `curl` with TFTP support, to test the server. Check the protocol list for `tftp`:

   ```bash
   curl --version | grep -o tftp
   ```

   ```text
   tftp
   ```

#### Steps

1. Set the root directory and turn the server on. The settings take effect at the next start:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/tftp -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true,"rootDirectory":"/srv/tftp"}'
   ```

   ```text
   {"note":"Written to the configuration file. This setting is bound via IOptions, not IOptionsMonitor, so a restart is required before this change takes effect."}
   ```

   The environment variables are `NINLIL_DHCP_SERVER_TFTP_ENABLE` and
   `NINLIL_DHCP_SERVER_TFTP_ROOT_DIRECTORY`. Restart Ninlil. In the console: **Presets & Settings**,
   **DHCP**, the **TFTP Server** card. The **DHCP Services** page starts and stops the server
   without a restart, once a root directory is saved.
2. Tell the computers which file to fetch. `bootFileName` is the name the computer requests, as
   the TFTP server sees it (relative to the root directory). `nextServerAddress` is the address of
   the TFTP server. Without it, Ninlil sends its Server Identifier address:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-scopes/<scope-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"bootFileName":"boot.bin","nextServerAddress":"192.168.50.2"}'
   ```

   The answer is `204` with no body. In the console: **DHCP Scopes**, edit the scope, **PXE boot
   file name** and **Next server (TFTP) address**.
3. Optional: send a different boot file to computers of one kind. A tag rule can set option `67`
   (the boot file name, text) for a vendor class. This one matches vendor classes that start with
   `PXEClient:Arch:00007` (read what your computers send in a packet capture), and
   `697078652E656669` is `ipxe.efi`:

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-tag-rules -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"uefi pxe","vendorClassIdentifierMatch":"PXEClient:Arch:00007","vendorClassIdentifierMatchMode":"Prefix","priority":10}'
   curl -k https://127.0.0.1:18443/api/dhcp-tag-rules/<rule-id>/options -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"optionTag":67,"valueHex":"697078652E656669"}'
   ```

   The first answer holds the rule's `id`, which the second command calls `<rule-id>`.

#### Check that it worked

1. Fetch the file from another machine, with the address of the TFTP server:

   ```bash
   curl -s tftp://192.168.50.2/boot.bin -o boot.bin -w '%{size_download}\n'
   ```

   ```text
   1300
   ```

   The number is the size of the file in bytes.
2. Ask for a file that does not exist. `curl` exits with code `68` (TFTP: file not found):

   ```bash
   curl -s tftp://192.168.50.2/missing.bin -o /dev/null -w 'curl exit %{exitcode}\n'
   ```

   ```text
   curl exit 68
   ```

3. Read the scope back. A PXE computer receives these two values in the DHCP reply:

   ```bash
   curl -k https://127.0.0.1:18443/api/dhcp-scopes/<scope-id> -H 'Authorization: Bearer <token>'
   ```

   ```text
   {"id":"<scope-id>","name":"lan", ... ,"bootFileName":"boot.bin","nextServerAddress":"192.168.50.2","kind":"AddressAssignment","delegatedPrefixLength":null}
   ```

#### Behaviour worth knowing

- **The server serves one directory.** A file name may use `/` or `\` between directories
  (Windows PXE clients send `\`), and a leading `/` or `\` means the root directory. A name that
  leaves the root with `..`, and a symbolic link whose target or any step on the way lies outside
  the root, get an Access Violation error. The root directory itself may be a link.
- **Read requests only, binary mode only.** A write request gets the error `This TFTP server is
  read-only.` A `netascii` or `mail` request gets `Only octet (binary) transfer mode is
  supported.` TFTP options such as `blksize` (RFC 2347) are ignored, so every transfer uses
  512-byte blocks.
- **Limits.** At most `NINLIL_DHCP_SERVER_TFTP_MAX_CONCURRENT_TRANSFERS` transfers run at once
  (50), and at most four of them belong to one client address. A request over a limit is dropped
  without a reply, because an error packet sent to a forged address would be reflected traffic.
  Ninlil logs one Warning a minute that counts such drops, abandoned transfers, client errors and
  refused paths. A block that the computer does not acknowledge is sent again after
  `NINLIL_DHCP_SERVER_TFTP_RETRANSMIT_TIMEOUT` (2 seconds) up to
  `NINLIL_DHCP_SERVER_TFTP_MAX_RETRANSMISSIONS` times (5).
- **Without a root directory the server does not bind.** An enabled TFTP server with an empty or
  missing root directory logs a Warning and opens no socket. Ninlil keeps running.
- **Ninlil does not check the boot file name.** The scope sends `bootFileName` whether or not the
  file exists in the root directory.
- **DHCP carries the boot file in two places.** The scope's boot file name goes into the `file` field of
  every reply, and into option `67` when the device lists option `67` in its Parameter Request
  List. A tag rule that sets option `67` changes the option, not the `file` field. The next server
  address goes into `siaddr`, and into option `66` as text when the device lists it. A custom
  option or tag-rule option `66` must be text; use option `150` for a binary address.
- **A scope allows 128 bytes of boot file name.** This is the size of the `file` field in
  RFC 2131.

#### Settings

- [DHCP, DHCPv6, Router Advertisement & TFTP](./ENVIRONMENT.md#dhcp-dhcpv6-router-advertisement--tftp):
  the TFTP rows (switch, port, root directory, retransmission and transfer limits).
- In the console: **Presets & Settings**, **DHCP**, **TFTP Server**.

#### When something goes wrong

- *`curl` exits with `68`.* The file is missing, or the name does not match. The server's error
  text is `'<name>' does not exist.`
- *The server answers `The requested path is outside this server's configured root directory.`*
  The name uses `..`, or a link leads out of the root. Put the file below the root.
- *Nothing answers.* The log must show `The TFTP server started and is now bound to UDP port 69.`
  If it shows `The TFTP server is enabled but no DhcpServer:Tftp:RootDirectory is configured; it
  will not bind a socket.` or `... root directory '<path>' does not exist; it will not bind a
  socket.`, fix the root directory and restart. `GET /api/dhcp-services` shows `isRunning`.
- *Ninlil stops at start with `Hosting failed to start` and `SocketException (13): Permission
  denied` or `(98): Address already in use`.* The process may not open port `69`, or another
  program holds it. An enabled TFTP server that cannot bind stops Ninlil, as DHCP does (see "When
  something goes wrong" in [DHCP, DHCPv6 and Router Advertisement](#dhcp-dhcpv6-and-router-advertisement)).

### Clustering, events and webhooks

#### What it does

This guide covers three ways Ninlil moves information to other servers and systems.

- **A cluster** joins two or more Ninlil servers, called **nodes**. Every node keeps a separate
  database, separate caches and separate DNS answers, and serves queries by itself. The nodes
  exchange heartbeats, push configuration changes to each other and answer each other's dashboard
  queries. Clustering is off by default.
- **Events** are the messages Ninlil publishes inside the process when something happens (a zone
  changes, a certificate renews, a lease is granted). Every event reaches the subscribers in the
  same process directly. The admin console's live feed receives four of them over SignalR, at
  `/hubs/events`. An optional **event broker** (RabbitMQ or ActiveMQ) carries events to other Ninlil
  instances and to programs outside Ninlil.
- **Webhooks** send seven kinds of event to a URL you choose, as an HTTP `POST` with a JSON body.
  Failed deliveries are retried and recorded.

The cluster channel is a set of REST endpoints under `/api/cluster/channel/` on the web HTTPS port.
Each request is signed with a TSIG key that the invite creates, and TLS encrypts the connection.

**What a cluster copies between nodes.** A change is pushed once, when you save it, to every known
peer:

| Copied | Details |
|---|---|
| Users and roles | Including password hashes and two-factor secrets. See [Security](#security). |
| Filter lists and their entries | Includes the setting that makes a [blocked service apply to every client](#filtering-rewrites-and-parental-controls). The assignment of a list to a client or group stays on its node. |
| Access control rules and forwarders | |
| Global rewrites | A rewrite scoped to a client or group stays on its node. |
| Five settings sections | Event broker, Rate limiting, DNS Cookies, Reverse proxy and the DNS Rewrites settings page. Turning on a Safe Search rewrite also turns the rewrite engine on for every node. |

**What a cluster does not copy:** zones and records (use zone transfers or catalog zones, see
[Secondary zones and transfers](#secondary-zones-and-transfers)), refresh tokens and API tokens,
webhook subscriptions, DHCP data, clients and client groups, TLS certificates and the JWT signing
key.

#### Before you start

1. Two or more Ninlil servers, each able to reach the web HTTPS port of the others. Run one Ninlil
   version on all of them: a peer on older code can send a change the other node cannot read, and
   that node logs a warning and drops it. Check from one node (`<node-b>` is the other node's name
   or address; `-k` accepts the self-signed certificate):

   ```bash
   curl -k -s -o /dev/null -w '%{http_code}\n' https://<node-b>/status
   ```

   ```text
   200
   ```

2. An account on each node with the `clustersettings:write` and `clusternodes:write`
   permissions (the bootstrap administrator has them) and its access token. Get the token as in
   [REST API](#rest-api). The commands below call it `<token-a>` for node A and `<token-b>` for
   node B.
3. One random value for the JWT signing key, used on every node. This command prints one:

   ```bash
   openssl rand -base64 48
   ```

   ```text
   PBWhpKyqXHi+kJVU8e4noRrUgf6lEz65U5c3591BlKXGCB1BzbfD/XuskIqB5Uj4
   ```

   (your value differs). The commands below call it `<jwt-key>`.
4. To use an event broker: a RabbitMQ or ActiveMQ Classic server that every node reaches. Ninlil
   was run against RabbitMQ 4.1 and ActiveMQ Classic 6.1.7.
5. To use webhooks: a web address that accepts `POST` requests with a JSON body.

#### Steps

**Form a cluster.** Node A invites, node B joins.

1. On every node, set the same JWT signing key and restart the node. The key is read at startup;
   see [`NINLIL_JWT_SIGNING_KEY`](./ENVIRONMENT.md#identity-authentication--sso). For a container
   add `-e NINLIL_JWT_SIGNING_KEY=<jwt-key>` to the `docker run` command, for a Linux package put
   `NINLIL_JWT_SIGNING_KEY=<jwt-key>` in `/etc/ninlil/ninlil.env`.

   Why: Ninlil checks an access token's signature with this key, and its issuer, audience and
   expiry. It then checks the account in its own database, so the account must exist and be enabled
   on that node. A token that node A signed is therefore accepted by node B only when both use the
   same key and node B has the account (cluster sync copies users). Without the same key, every
   client that moves from one node to the other gets `401`. Use a value of at least 32 bytes:
   Ninlil refuses to start with a key shorter than 16 bytes.
2. On every node, switch clustering on and tell the node which address its peers use. Run this on
   node A and again on node B with that node's name (`<node-a>` is the name or IP address peers
   use to reach this node, without `https://`):

   ```bash
   curl -k -X PATCH https://<node-a>/api/cluster-settings -H 'Authorization: Bearer <token-a>' -H 'Content-Type: application/json' -d '{"enable":true,"advertisedHost":"<node-a>"}'
   ```

   ```text
   {"note":"Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required."}
   ```

   If the HTTPS port is not 443, add `"advertisedPort":<port>`. The default `0` means the
   web HTTPS port.
3. On node A, create an invite:

   ```bash
   curl -k -X POST https://<node-a>/api/cluster/nodes/invite -H 'Authorization: Bearer <token-a>'
   ```

   ```text
   {"inviteCode":"ninlil-cluster-invite-1.eyJWZXJzaW9uIjoxLC...","expiresAtUtc":"2026-10-30T22:23:38.6298395+00:00"}
   ```

   Copy the `inviteCode` value. The code holds the secret of a TSIG key that only the join can
   use, so treat it like a password and send it over a private channel. It works for 15 minutes.
4. On node B, join with that code. The display name is optional and names the peer on both nodes:

   ```bash
   curl -k -X POST https://<node-b>/api/cluster/nodes/join -H 'Authorization: Bearer <token-b>' -H 'Content-Type: application/json' -d '{"inviteCode":"<invite-code>","displayName":"office"}'
   ```

   ```text
   {"id":"3e098494-...","name":"office","remoteNodeId":"98925e78-...","host":"<node-a>","port":443,"status":"Joining", ...
   ```

   The console does the same on the **Cluster** page (**Invite a peer** and **Join a cluster**).

**Switch on an event broker** (optional). Do this on every node that should share events.

1. Set the transport and the connection in the environment of every node's Ninlil process, then
   restart it. The transport is chosen before the program starts, so the console cannot switch it
   live. For a Linux package, put these lines in `/etc/ninlil/ninlil.env`; for a container, give
   each as `-e <line>`. For RabbitMQ:

   ```text
   NINLIL_DNS_SERVER_EVENTS_TRANSPORT=RabbitMq
   NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_HOST_NAME=<broker-host>
   NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_USER_NAME=<user>
   NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_PASSWORD=<password>
   ```

   For ActiveMQ use `ActiveMq` and the `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_*` variables. Every
   variable is listed in [Event broker (message queue) transports](./ENVIRONMENT.md#event-broker-message-queue-transports).
2. Look for the connection line in the log:

   ```text
   info: Ninlil.Dns.Server.Events.RabbitMq.RabbitMqDnsServerEventBrokerTransport[0]
         Connected to the RabbitMQ event broker at <broker-host>:5672.
   ```

**Create a webhook.**

1. Start a receiver. For a first test, save this program as `receiver.py` and run
   `python3 receiver.py` in a second terminal. It prints each request and answers `204`:

   ```python
   import http.server


   class Receiver(http.server.BaseHTTPRequestHandler):
       def do_POST(self):
           body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
           print(self.headers.get("Authorization"), self.headers.get("X-Ninlil-Signature"))
           print(body.decode(), flush=True)
           self.send_response(204)
           self.send_header("Content-Length", "0")
           self.end_headers()

       def log_message(self, *args):
           return


   http.server.HTTPServer(("127.0.0.1", 8099), Receiver).serve_forever()
   ```

2. Create the subscription on one node. Webhook subscriptions are not copied to other nodes, so
   repeat this on each node that should send. `BearerToken` is the secret your receiver checks in
   the `Authorization` header. `enableHmacSigning` adds a signature header (see below):

   ```bash
   curl -k -X POST https://<node-a>/api/webhooks -H 'Authorization: Bearer <token-a>' -H 'Content-Type: application/json' -d '{"name":"zone-changes","targetUrl":"http://127.0.0.1:8099/hook","eventTypeNames":["DnsZoneChanged"],"authMode":"Bearer","authCredentials":{"BearerToken":"demo-token"},"enableHmacSigning":true,"maxDeliveryAttempts":3}'
   ```

   ```text
   {"subscription":{"id":"<webhook-id>","name":"zone-changes", ...},"authCredentials":{"BearerToken":"demo-token"},"hmacSigningSecret":"1hp++cErLc4L..."}
   ```

   Copy the `id` and the `hmacSigningSecret`. This response is the only time Ninlil shows the
   secret. The console's **Webhooks** page does the same and shows the secret once in a dialog.
3. Trigger an event by creating a zone (a zone change fires `DnsZoneChanged`):

   ```bash
   curl -k -X POST https://<node-a>/api/zones -H 'Authorization: Bearer <token-a>' -H 'Content-Type: application/json' -d '{"name":"hook.test","type":"Primary"}'
   ```

4. Read the receiver's terminal. The first line holds the `Authorization` header and the
   `X-Ninlil-Signature` header, the second line the body:

   ```text
   Bearer demo-token 58d60900e3f76305954b0bc2fab282e367720351b486c8685d35802e0e0fc538
   {"eventType":"DnsZoneChanged","deliveredAtUtc":"2026-10-30T22:45:58.1965544+00:00","data":{"occurredAtUtc":"2026-10-30T22:45:58.1904269\u002B00:00","zoneId":"548100cf-...","zoneName":"hook.test"}}
   ```

5. Check the signature. The header holds the lowercase hexadecimal HMAC-SHA256 of the request
   body, keyed with the secret text from step 2. Paste the body line between the quotes:

   ```bash
   printf '%s' '<body>' | openssl dgst -sha256 -hmac '<hmac-signing-secret>'
   ```

   ```text
   SHA2-256(stdin)= 58d60900e3f76305954b0bc2fab282e367720351b486c8685d35802e0e0fc538
   ```

   The value equals the `X-Ninlil-Signature` header from step 4.

#### Check that it worked

Each command below runs on node A unless it says otherwise.

1. The cluster lists the peer, `Healthy` within one heartbeat (30 seconds by default):

   ```bash
   curl -k https://<node-a>/api/cluster/nodes -H 'Authorization: Bearer <token-a>'
   ```

   ```text
   {"items":[{"id":"71b5848a-...","name":"office","remoteNodeId":"d90596e8-...","host":"<node-b>","port":443,"status":"Healthy","lastHeartbeatAtUtc":"2026-10-30T22:08:43.407613+00:00","missedHeartbeatCount":0, ...}],"skip":0,"take":50,"totalCount":1}
   ```

2. A setting that the cluster copies reaches the other node. Switch the rewrite engine on at node A,
   wait a second, then read it at node B:

   ```bash
   curl -k -X PATCH https://<node-a>/api/rewrite-settings -H 'Authorization: Bearer <token-a>' -H 'Content-Type: application/json' -d '{"enable":true}'
   curl -k https://<node-b>/api/rewrite-settings -H 'Authorization: Bearer <token-b>'
   ```

   ```text
   {"enable":true,"timeToLiveSeconds":300,"lastModifiedUtc":"2026-10-30T22:09:02.5367468+00:00","originatingNodeId":"98925e78-..."}
   ```

   `originatingNodeId` names the node where the change was made.
3. A token from node A works on node B when both use the same key. Sign in at node A, then call
   node B with that access token (`<token-a>`). The result is `200`. With different keys it is
   `401`:

   ```bash
   curl -k -s -o /dev/null -w '%{http_code}\n' https://<node-b>/api/zones -H 'Authorization: Bearer <token-a>'
   ```

   ```text
   200
   ```

4. The webhook delivery shows as `Succeeded`:

   ```bash
   curl -k https://<node-a>/api/webhooks/<webhook-id>/delivery-attempts -H 'Authorization: Bearer <token-a>'
   ```

   ```text
   {"items":[{"id":"...","subscriptionName":"zone-changes","eventTypeName":"DnsZoneChanged", ... "status":"Succeeded","attemptCount":1,"maxAttempts":3,"lastHttpStatusCode":204, ...}],"skip":0,"take":50,"totalCount":1}
   ```

5. With a broker, the log has the line from the broker step. `GET /status` (signed in) shows the
   `eventBroker` component as `Connected to the RabbitMq event broker.`

#### Behaviour worth knowing

- **A cluster pushes a change once, when you save it.** Ninlil keeps no backlog. A node that was
  down, or had not joined yet, during a change does not receive it later. To bring such a node up
  to date, save each of the five synced settings pages once on any node (**Event broker**,
  **Rate limiting**, **DNS Cookies**, **Reverse proxy** and **DNS Rewrites** under **Settings**),
  and save the other copied items again; every save is sent. The newest write wins on every node.
  When two writes carry the same time, every node settles it the same way.
- **A synced setting follows the same restart rule on each node.** The save on node A may say
  "restart required". The same value reaches node B at once, but node B also needs the restart
  before the setting works. The settings page on B shows the updated `lastModifiedUtc` while it still
  runs the old value.
- **An environment variable pins a synced value on its node.** The cluster still writes the
  received value into that node's configuration file, but the variable wins when the node reads
  its settings. Remove the variable and restart the node to follow the cluster again.
- **After a restore on another machine, the old access tokens stop working.** The backup does not
  hold the JWT key. Refresh tokens and API tokens come back, because they are database rows. See
  [Backup and restore](#backup-and-restore).
- **Refresh tokens and API tokens belong to the node that issued them.** Behind a load balancer,
  send each client to the same node, or the client signs in again when a refresh request reaches
  another node. Access tokens work on every node that shares the JWT key.
- **Disabling an account or changing permissions reaches issued tokens on every node.** Cluster
  sync copies users and roles to every healthy peer when you save the change, and each peer then
  rejects a disabled account's access token on its next request, and uses the permissions you saved (see
  [Security](#security)). A peer that was unreachable at that moment keeps the old state until the
  next save of that user or role, so its access tokens for the account keep working there until
  they expire (15 minutes by default).
- **A password or two-factor change reaches every node.** The changed security stamp travels with the
  user, so each peer rejects the account's older access tokens on its next request. Refresh tokens
  belong to the node that issued them: a peer that receives a changed stamp also revokes the
  account's refresh tokens, so a session opened on that node cannot refresh past the change.
- **A user name is checked on the peers.** Creating a user asks every healthy peer whether the name
  exists and returns `409` if one has it. A peer that cannot be reached is skipped.
- **A change on another node reaches DNS answers after a delay.** On the node where you make a
  change, the answer changes at once. On the other nodes, filter and client settings, rewrites,
  client schedules and forwarder zones show up after at most 60 seconds, and the entries of a
  filter list after at most 5 minutes. With a [read replica](#read-replicas-and-database-clusters),
  add the replica's lag limit (`NINLIL_STORAGE_READ_REPLICA_MAX_LAG_SECONDS`, 5 seconds by
  default).
- **Static address mappings and DHCP leases refresh every 30 seconds.** Ninlil keeps them in memory
  for client identification. A change another process makes in the same database (a second
  instance, a direct edit) reaches the in-memory copy within 30 seconds. A change made in this
  process reaches it at once.
- **An unreachable peer is marked after three missed heartbeats.** The default heartbeat is
  every 30 seconds, so the mark appears after about a minute and a half, and `GET /status` shows the
  `cluster` component as `Degraded` while a peer is `Unreachable`. A peer that answers again turns
  `Healthy` at the next heartbeat.
- **Every event reaches the process that made it directly.** The broker adds the other
  instances. Each event carries the id of the instance that made it, and an instance drops the
  events the broker sends back to it. An instance acts only on events it made, with one
  exception: the console's live feed also shows events from every instance that shares the
  broker. A `DnsZoneChanged` from another instance, for example, does not trigger this
  instance's webhooks, NOTIFY or cache reloads.
- **The broker carries every event except settings sync.** The settings-sync event holds settings
  values, secrets included, so it never leaves the process. Cluster sync uses the cluster channel.
- **Under load, per-query events are dropped first.** Per-query events (`DnsQueryReceived`,
  `DnsQueryAnswered`) wait in a bounded queue
  ([`NINLIL_DNS_SERVER_EVENTS_CAPACITY`](./ENVIRONMENT.md#query-pipeline--cross-cutting-dns-behavior),
  10000 by default). When it is full the oldest are dropped, and one warning a minute reports how
  many. Every other event stays in an unbounded queue and is never dropped there. The queue that
  feeds the broker is bounded for every event type: when it is full, the oldest event is dropped.
- **Upgrade every instance that shares a broker together.** An instance that receives an event
  name it does not know logs one warning per name, `Ignored an event of type '<name>' from the
  message broker; it is unknown here or may not cross the broker. Upgrade every instance that
  shares a broker together.`, and drops the event.
- **ActiveMQ gives up on a failure the client cannot retry.** A network outage is retried with a
  growing delay. A failure such as wrong credentials is not retried: the log says `Failed to
  connect to the ActiveMQ event broker at <host>:<port>; this transport instance will remain
  unavailable until restarted.` Fix the credentials and restart the node.
- **Delivery retries double the wait.** After a failed delivery Ninlil waits one second, then two,
  then four, up to 30 seconds ([`NINLIL_DNS_SERVER_WEBHOOKS_DELIVERY_INITIAL_RETRY_DELAY`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering)
  and `..._MAX_RETRY_DELAY`). A subscription tries a delivery up to `maxDeliveryAttempts` times
  (5 when you give none). The delivery is then `DeadLettered`, and `GET /status` shows the
  `webhookDelivery` component as `Degraded` for the next hour. A retry waits in the database, so it
  survives a restart. If Ninlil stops during the first attempt, before it saves the attempt, that
  delivery is lost.
- **A webhook can reach any address the server can.** Ninlil does not limit target addresses: a
  `POST` to `http://127.0.0.1:8099/hook` works. Give `webhooks:write` only to people you trust
  with that.
- **The seven events a webhook can deliver:**

  | Event name | When it fires | `data` fields |
  |---|---|---|
  | `DnsZoneChanged` | A zone's records or settings change: a record edit through the API, a dynamic update, a completed zone transfer. | `occurredAtUtc`, `zoneId`, `zoneName` |
  | `DnsCacheEvicted` | A forwarder or recursive cache removes entries (expired, over the size limit, flushed or removed by name). | `occurredAtUtc`, `reason`, `entryCount` |
  | `DnssecZoneResigningCompleted` | A signed zone finishes signing (after a change, before signatures expire, or at startup). | `occurredAtUtc`, `zoneId`, `zoneName`, `signatureExpiresAtUtc` |
  | `AcmeCertificateRenewalSucceeded` | A certificate is issued or renewed. | `occurredAtUtc`, `domain`, `expiresAtUtc` |
  | `AcmeCertificateRenewalFailed` | A certificate issue or renewal fails. The service retries at its next poll. | `occurredAtUtc`, `domain`, `errorMessage` |
  | `CloudDnsZoneSyncFailed` | A cloud-synced zone cannot be reconciled with its provider. | `occurredAtUtc`, `zoneId`, `zoneName`, `errorMessage` |
  | `TrustAnchorRollover` | A root trust anchor becomes `Valid` or is revoked (RFC 5011). | `occurredAtUtc`, `keyTag`, `newState` |

  Any other name in `eventTypeNames` is refused with `400`, and the message lists the seven.
- **What a delivery looks like.** Ninlil sends a `POST` with `Content-Type: application/json`. With
  `authMode` `Bearer` it adds `Authorization: Bearer <token>`, with `Basic` an HTTP Basic header
  (`{"Username":"...","Password":"..."}` in `authCredentials`), with `None` no header. With
  `enableHmacSigning` it adds `X-Ninlil-Signature`. The body is
  `{"eventType":"...","deliveredAtUtc":"...","data":{...}}`, and `deliveredAtUtc` is the time of the
  first attempt, also on a retry.
- **The signing secret cannot be changed.** `PUT` and `PATCH` leave it alone. To rotate it, delete
  the subscription and create it again.

#### Settings

| Setting | Variable and row |
|---|---|
| Clustering on or off, this node's address and the heartbeat | [`NINLIL_CLUSTER_ENABLE`, `NINLIL_CLUSTER_ADVERTISED_HOST`, `NINLIL_CLUSTER_ADVERTISED_PORT`, `NINLIL_CLUSTER_HEARTBEAT_INTERVAL`, `NINLIL_CLUSTER_HEARTBEAT_TIMEOUT`, `NINLIL_CLUSTER_MISSED_HEARTBEAT_THRESHOLD`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering). The node id is generated and kept in `data/cluster-node-id.txt`. Console page: **Settings**, **Cluster**. |
| The key that signs access tokens | [`NINLIL_JWT_SIGNING_KEY`](./ENVIRONMENT.md#identity-authentication--sso) |
| Which event transport runs | [`NINLIL_DNS_SERVER_EVENTS_TRANSPORT`](./ENVIRONMENT.md#query-pipeline--cross-cutting-dns-behavior) |
| Broker connection | [Event broker (message queue) transports](./ENVIRONMENT.md#event-broker-message-queue-transports). Console page: **Settings**, **Event broker**. |
| Webhook delivery timing | [`NINLIL_DNS_SERVER_WEBHOOKS_DELIVERY_*`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering). Console page: **Settings**, **Webhook Delivery**. |

#### When something goes wrong

- *Creating an invite answers `Clustering is not enabled on this node -- set 'Cluster:Enable' before inviting a peer.`* Run step 2 of "Form a cluster" on that node.
- *It answers `This node's own 'Cluster:AdvertisedHost' has not been configured -- a peer would have no way to reach it.`* Set `advertisedHost` (step 2).
- *Joining answers `This does not look like a Ninlil cluster invite code.`* The code was cut or changed on the way. Copy the whole value of `inviteCode`.
- *Joining answers `409` with `This node is already a member of that peer's cluster.`* The two nodes are joined. List them with `GET /api/cluster/nodes`.
- *Joining fails because the inviter cannot be reached, or because the code has expired.* Check that node B reaches node A's address and port, then create another invite on node A (an invite works for 15 minutes) and join again. A failed join leaves nothing behind, so you can repeat it.
- *A peer stays `Unreachable`.* Check that the peer is running and that the address in its row (`host` and `port`) reaches its HTTPS port from this node. The peer turns `Healthy` at the next heartbeat after it answers.
- *A token from one node gets `401` on another.* The nodes use different `NINLIL_JWT_SIGNING_KEY` values, or one of them runs another `NINLIL_JWT_ISSUER` or `NINLIL_JWT_AUDIENCE`. Set the same values on every node and restart.
- *A webhook delivery is `DeadLettered`.* Read `lastErrorMessage` and `lastHttpStatusCode` in the delivery attempt (`GET /api/webhooks/<webhook-id>/delivery-attempts`). The receiver must answer with a status from 200 to 299.
- *A webhook never fires.* A subscription fires only for events this instance produced. Check that `isDisabled` is `false` and that the event name is in `eventTypeNames`.
- *The log says `Failed to connect to the ActiveMQ event broker ...`.* See the ActiveMQ item above.

### Security

#### What it does

This guide lists what protects a Ninlil installation, what it leaves to you, and the steps that
harden a fresh installation. It covers accounts and permissions, sign-in limits, tokens, and the
files Ninlil keeps secret. Every setting it names links to its row in
[`ENVIRONMENT.md`](./ENVIRONMENT.md).

Ninlil checks every REST request against a permission string of the form `resource:action`, such as
`zones:write`. A role is a named set of permissions, and an account gets permissions from its roles
and from direct grants. The permission `*` allows everything.

#### Before you start

1. A running Ninlil and shell access to the host it runs on.
2. An account that holds `*` (the bootstrap administrator does) and its access token. Get it as in
   [REST API](#rest-api). The commands below call it `<token>`.
3. The location of the [data directory](#the-data-directory).

#### Steps

**Protect the data directory.** It holds the database, the key ring, the JWT signing key and the
TLS certificate with its private key.

1. On Linux and macOS, Ninlil sets the folder to mode 0700 (owner only) when it starts. Check it,
   with the path from [The data directory](#the-data-directory):

   ```bash
   stat -c '%a %n' <data-directory>
   ```

   ```text
   700 /var/lib/ninlil
   ```

   The key files inside are created with mode 600.
2. On Windows, a folder has an access list instead of a mode. List it:

   ```powershell
   icacls C:\Ninlil\data
   ```

   Only the service account (see [Windows](#windows)), `BUILTIN\Administrators` and
   `NT AUTHORITY\SYSTEM` should appear. A line for `BUILTIN\Users` or `NT AUTHORITY\Authenticated Users` means other
   accounts can read the keys. To remove them, switch off inheritance, which copies the inherited
   entries, then remove the two groups:

   ```powershell
   icacls C:\Ninlil\data /inheritance:d
   icacls C:\Ninlil\data /remove "BUILTIN\Users" "NT AUTHORITY\Authenticated Users"
   ```

3. In a container, the data directory is the volume you mount at `/app/data` (see
   [Running with Docker](#running-with-docker)). The container runs as user `1654` and sets the
   folder to mode 0700 itself. Keep the volume's folder on the host readable only by the people
   who administer the host.

**Replace the bootstrap administrator.** The bootstrap account `admin` holds `*`. Ninlil creates it
only while no account exists, and never deletes it.

1. Create an account for yourself. The password needs at least 12 characters with an upper-case
   letter, a lower-case letter, a digit and a symbol, and the e-mail address must be unique:

   ```bash
   curl -k https://127.0.0.1:18443/api/users -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"userName":"alice","password":"Correct-Horse-9!","email":"alice@example.com","displayName":"Alice"}'
   ```

   ```text
   {"id":"<user-id>","userName":"alice","email":"alice@example.com","displayName":"Alice","isDisabled":false,"twoFactorEnabled":false,"roles":[],"directPermissions":[], ...}
   ```

   Copy the `id`. The commands below call it `<user-id>`.
2. Put the account in the `Administrators` role, which holds `*`. Only a holder of `*` can do this:

   ```bash
   curl -k -X POST https://127.0.0.1:18443/api/users/<user-id>/roles -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"roleName":"Administrators"}'
   ```

   The answer is an empty `204`.
3. Sign in as that account (see [REST API](#rest-api)) and use its token from here on. The next
   command calls it `<alice-token>`.
4. Disable the bootstrap account. `PUT` replaces the display name, the e-mail address and the
   disabled flag, so send all three; `<admin-id>` is the `id` of `admin` in `GET /api/users`:

   ```bash
   curl -k -X PUT https://127.0.0.1:18443/api/users/<admin-id> -H "Authorization: Bearer <alice-token>" -H 'Content-Type: application/json' -d '{"displayName":"Administrator","email":"admin@localhost.local","isDisabled":true}'
   ```

   Ninlil answers `204`. Sign-in, refresh, API tokens and access tokens already issued to a disabled account are refused (`401`). The account stays
   in the list, so you can switch it back on with `"isDisabled":false`.

**Give each person the permissions they need.**

1. List the permission strings the server knows:

   ```bash
   curl -k https://127.0.0.1:18443/api/permissions -H "Authorization: Bearer <token>"
   ```

2. Create a role and grant it permissions. Records use the zone permissions:

   ```bash
   curl -k https://127.0.0.1:18443/api/roles -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"name":"zone-editor","description":"Edits zones and records"}'
   curl -k https://127.0.0.1:18443/api/roles/<role-id>/permissions -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"permission":"zones:write"}'
   curl -k https://127.0.0.1:18443/api/roles/<role-id>/permissions -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"permission":"zones:read"}'
   ```

   `<role-id>` is the `id` in the answer to the first command. You can grant only permissions you
   hold yourself. The console does the same on **Roles** and **Users**.

**Switch on two-factor sign-in** for every administrator. Open the account menu in the top-right
corner of the console, choose **Security**, and follow the steps for an authenticator app or for
e-mailed codes. Ninlil also issues single-use recovery codes. E-mailed codes need
[SMTP](./ENVIRONMENT.md#identity-authentication--sso) settings. Confirming the authenticator signs
you out of every session, yours included; sign in again with a code from the app.

**Limit who reaches the console.**

1. The REST API and the console answer on both web ports. The plain-HTTP port has no TLS, so it
   carries passwords and tokens in the clear. Check it:

   ```bash
   curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:18080/api/zones
   ```

   ```text
   401
   ```

   The `401` shows that the API answers on the HTTP port. Close that port in your firewall, or
   limit both web ports to one address with
   [`NINLIL_DNS_SERVER_WEB_LISTEN_ADDRESSES`](./ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding).
2. Behind a reverse proxy, tell Ninlil the proxy's address, so it takes the client's address
   from the proxy's `X-Forwarded-For` header. Without that, the sign-in limit sees only the proxy's
   address and counts every user together. Ninlil reads this setting at startup, so restart it
   afterwards (`<proxy-address>` is the address the proxy connects from):

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/reverse-proxy-settings -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"enable":true,"knownProxies":["<proxy-address>"]}'
   ```

   ```text
   {"note":"Written to the configuration file. This setting is bound via IOptions, not IOptionsMonitor, so a restart is required before this change takes effect."}
   ```

   The console page is **Settings**, **Reverse proxy**. A shared secret header and network ranges
   are optional; see the [Web host rows](./ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding).
3. Replace the self-signed certificate with one from [Certificates (ACME)](#certificates-acme) or
   a PFX file, set with `NINLIL_DNS_SERVER_WEB_CERTIFICATE_PFX_PATH` (see the
   [Web host](./ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding) rows).

#### Check that it worked

1. The data directory is private (step 1 above printed `700`).
2. A weak password is refused:

   ```bash
   curl -k https://127.0.0.1:18443/api/users -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"userName":"bob","password":"short1!","email":"bob@example.com","displayName":"Bob"}'
   ```

   ```text
   {"type":"https://httpstatuses.io/400","title":"One or more validation errors occurred.","status":400,"detail":"One or more validation errors occurred.","errors":{"PasswordTooShort":["Passwords must be at least 12 characters."],"PasswordRequiresUpper":["Passwords must have at least one uppercase ('A'-'Z')."]},"traceId":"..."}
   ```

3. The sign-in limit works. Seven wrong passwords from one address give `401` five times and then
   `429`, and the right password is refused until the window ends:

   ```bash
   for i in 1 2 3 4 5 6 7; do curl -k -s -o /dev/null -w "attempt $i: HTTP %{http_code}\n" https://127.0.0.1:18443/api/auth/login -H 'Content-Type: application/json' -d '{"userName":"admin","password":"wrong-password-1A!"}'; done
   ```

   ```text
   attempt 1: HTTP 401
   attempt 2: HTTP 401
   attempt 3: HTTP 401
   attempt 4: HTTP 401
   attempt 5: HTTP 401
   attempt 6: HTTP 429
   attempt 7: HTTP 429
   ```

   Run this against a test installation, because it blocks the address you run it from for five minutes.
   While it is blocked, the answer to the next attempt names the wait in seconds:

   ```bash
   curl -k -s -D - -o /dev/null https://127.0.0.1:18443/api/auth/login -H 'Content-Type: application/json' -d '{"userName":"admin","password":"wrong-password-1A!"}' | grep -i -E '^(HTTP|retry-after)'
   ```

   ```text
   HTTP/2 429
   retry-after: 290
   ```

   The number counts down while the window runs and never exceeds 300 with the default window.

#### Behaviour worth knowing

- **Passwords.** The minimum is 12 characters, with an upper-case letter, a lower-case letter, a
  digit and a symbol, and every account needs a unique e-mail address. Ninlil sets these at startup;
  the `NINLIL_IDENTITY_PASSWORD_*` and `NINLIL_IDENTITY_USER_REQUIRE_UNIQUE_EMAIL` rows in
  [`ENVIRONMENT.md`](./ENVIRONMENT.md#identity-authentication--sso) show the values. The bootstrap
  password you set must meet the same rules.
- **There is no account lockout.** A wrong password never locks an account, so nobody can lock an
  administrator out by guessing. The `lockout` fields of `/api/identity-settings` have no effect,
  and the console's Identity page does not show them. Password guessing is limited by the sign-in
  limit instead.
- **The sign-in limit counts per client address.** It allows 5 attempts in 5 minutes for the
  password sign-in and the second-factor sign-in together, and returns `429 Too Many Requests` after
  that. An IPv6 client counts by its /64 network, so an attacker cannot rotate through addresses of
  one subscriber. A Forgot password request has a separate count of 5 in 5 minutes. The window is
  fixed: a client that reaches the limit waits until the window ends, and the `429` carries a
  `Retry-After` header with the seconds that are left. The settings are read at
  startup, so a change needs a restart
  ([`NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_*`](./ENVIRONMENT.md#identity-authentication--sso)).
- **Two-factor codes have separate limits, kept per node in memory.** Five wrong codes in 15
  minutes block that account's second-factor checks (`429`), whichever address they come from. An
  e-mailed code can be requested once a minute, and a pending sign-in may ask for three. A pending
  sign-in lasts 5 minutes and its token works once. A restart, or a second node, starts these
  counts again.
- **Ninlil checks an access token against its account at every request.** An access token lasts
  15 minutes, but disabling or deleting the account, locking it out, or changing its roles or
  permissions applies to a token that already exists on the next request: a disabled or deleted
  account gets `401`, and the token carries the account's current permissions, not the ones it was
  issued with. Ninlil keeps each account's state in memory for 15 seconds, and a change made through
  the API, the console or cluster sync clears it at once, so no request reads the database. A change
  made directly in the database reaches issued tokens within those 15 seconds. A live feed
  connection (see [Using the admin console](#using-the-admin-console)) is checked when it opens and
  closes when its token expires, so one that is already open can outlast a disabled account by up
  to 15 minutes. A refresh token lasts 30 days (12 hours of inactivity for a browser session without
  "Remember me"), changes at every refresh, and cannot be used twice; a disabled account cannot
  refresh. API tokens (`Authorization: ApiToken <token>`) use the owner's current permissions at
  each request, and stop working when revoked or when the owner is disabled. The lifetimes are
  [`NINLIL_JWT_*`](./ENVIRONMENT.md#identity-authentication--sso) settings.
- **A password change or a two-factor change signs out every session of the account.** Each
  access token carries a digest of the account's security stamp, and Ninlil compares it at every
  request. These changes replace the stamp and revoke every refresh token of the account:
  changing your password, an administrator resetting it
  (`POST /api/users/<id>/reset-password`), setting a new password with a reset link (Forgot
  password), confirming an authenticator, turning two-factor off, and generating new recovery codes.
  Every access token issued before the change then answers `401` on its next request, and no refresh
  token works, so a stolen session is cut off. The session that made the change is cut off too: the
  console signs you out and the sign-in page says why, and an API client signs in again. Starting an
  authenticator setup does not end the session, because the confirmation that follows needs it;
  the sessions end when you confirm the new key. A token without a stamp claim gets `401`; the console
  then asks for a new one with its refresh cookie. API
  tokens do not carry a stamp and keep working after these changes: they belong to the account, not
  to a password, and you revoke one by name (see [REST API](#rest-api)). Using a recovery code at
  sign-in does not end other sessions.
- **The JWT signing key proves a token is Ninlil's.** Whoever holds `data/jwt-signing-key.txt` can
  make a token for any enabled account, the bootstrap administrator's included, and the token then
  gets that account's permissions. Treat the file like a root password.
- **The key ring and the JWT signing key are not encrypted on disk.** Ninlil stores the Data
  Protection key ring in the data directory, and ASP.NET Core Data Protection warns about it in the log:
  `No XML encryptor configured. Key {...} may be persisted to storage in unencrypted form.` The
  database holds the TSIG secrets, DNSSEC private keys, cloud credentials, TOTP keys and the ACME
  account key encrypted with that key ring, so a reader of the whole data directory can decrypt
  them. This is an accepted risk. The controls are the folder permissions above and the
  encrypted backups in [Backup and restore](#backup-and-restore). On Windows the files are
  also plain files and not tied to a Windows account.
- **A cluster sends some secrets in the clear on the cluster channel.** User two-factor secrets and
  settings that hold a secret travel inside the TLS connection that is also signed with a TSIG key.
  The receiving node encrypts them with the node's key ring. They never go through the event broker. An
  invite code contains a TSIG secret, so share it only over a private channel.
- **A backup holds the key ring and the TLS certificate with its private key.** Always set a
  passphrase. See [Backup and restore](#backup-and-restore).
- **Single sign-on accounts start with no permissions.** The first sign-in of an OpenID Connect
  user creates an account without any permission; an administrator grants roles. A verified
  e-mail address (`email_verified`) is stored on the account. An unverified or missing address is
  not stored; the account gets the placeholder `<sub>@sso.invalid` instead, where `<sub>` is
  the user's identifier at the provider. Mail to that address cannot be delivered, and the account has
  no password, so Forgot password cannot reach it. A verified address that belongs
  to another account refuses the sign-in. A disabled account is refused at the provider sign-in,
  too.
- **Some settings point Ninlil at an address, and Ninlil does not limit the address.** Webhook
  targets, forwarders, filter-list subscriptions and the self-update feed URL can name any host the
  server can reach, including a loopback address. Give the matching `*:write` permissions to
  people you trust with that.
- **The OpenAPI document and Swagger UI need no sign-in.** `/swagger/v1/swagger.json` and `/swagger`
  describe the API, not your data. `GET /status` shows only the overall state to a caller
  without the `status:read` permission.

#### Settings

| Setting | Variable and row |
|---|---|
| Password rules, unique e-mail | [`NINLIL_IDENTITY_PASSWORD_*`, `NINLIL_IDENTITY_USER_REQUIRE_UNIQUE_EMAIL`](./ENVIRONMENT.md#identity-authentication--sso). Console page: **Settings**, **Identity**. |
| Lockout (no effect) | `NINLIL_IDENTITY_LOCKOUT_*` |
| Sign-in limit | [`NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_*`](./ENVIRONMENT.md#identity-authentication--sso). Console page: **Settings**, **Admin Login Rate Limit**. |
| Token lifetimes, issuer, audience, signing key | [`NINLIL_JWT_*`](./ENVIRONMENT.md#identity-authentication--sso) |
| Bootstrap administrator | [Bootstrap administrator credentials](./ENVIRONMENT.md#bootstrap-administrator-credentials) |
| Web addresses and reverse proxy | [Web host](./ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding) |

#### When something goes wrong

- *An account is refused with `PasswordTooShort` or `DuplicateEmail`.* Choose a password that
  meets the rules above, or another e-mail address.
- *Every sign-in answers `429`.* The address reached the sign-in limit. Wait for the seconds in the
  `Retry-After` header (the window is 5 minutes by default) or restart Ninlil. Behind a reverse proxy without the
  Reverse proxy settings, all users share one address, so one user's mistakes block the others.
- *A token that worked answers `401` after a password or two-factor change.* That is intended: the
  change ended every session of the account. Sign in again.
- *Ninlil does not start and exits with code 78, and the log says `The JWT signing key is <n> bytes
  long`.* The JWT key (as UTF-8) is shorter than 16 bytes. Use at least 32 bytes
  (`openssl rand -base64 48`) in [`NINLIL_JWT_SIGNING_KEY`](./ENVIRONMENT.md#identity-authentication--sso),
  or delete `data/jwt-signing-key.txt` so Ninlil generates a key, and start again.
- *You are signed out when you move between cluster nodes.* See
  [Clustering, events and webhooks](#clustering-events-and-webhooks).
- *The bootstrap password is lost.* Ninlil prints a generated password once in the log, and never
  again. With another administrator account, reset the password with
  `POST /api/users/<admin-id>/reset-password` (the request body is described in Swagger UI). Without
  another administrator, restore a backup or start with an empty data directory.

#### Single sign-on and e-mail secrets

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

### Storage

This guide is for the person who runs a Ninlil installation: moving to another database engine,
upgrading safely, and the settings that change how the database behaves. Every setting it names is
listed in [`ENVIRONMENT.md`](./ENVIRONMENT.md#storage-engine). Where a step needs a file or a
setting you have not met yet, the step says where to find it.

The default is SQLite. The data directory is described in [The data directory](#the-data-directory).

#### Choosing a storage engine

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

#### Switching the storage engine

You can move an installation between SQLite, PostgreSQL, MySQL and SQL Server. MongoDB cannot be
the source or the target of a switch. A switch takes effect at the **next restart**: you ask for
it in the admin console, then restart Ninlil, and the copy runs at that start before anything else
uses the database.

The copy moves everything an administrator configured (zones, records, users, roles, filtering,
clients, DHCP scopes, reservations and leases). It leaves out history that can grow very large:
the query log, the operational log and metrics, the zone change journal, webhook delivery
attempts and the cluster sync tombstones. The Data Protection key ring, the certificates and the
configuration file stay where they are. Ninlil does not delete the old database.

#### Before you start

1. Create the target database and its account. For PostgreSQL, MySQL and SQL Server, the scripts
   in [`src/script/database/`](./src/script/database/README.md) do this.
2. Check that no read replica or cluster setting is in use (the `NINLIL_STORAGE_READ_REPLICA_*`
   and per-engine variables in [Read replicas and database clusters](#read-replicas-and-database-clusters)).
   Ninlil refuses to schedule a switch while one is set. Remove them, switch, then set them up again
   for the new engine.
3. Sign in with an account that holds the `storage:manage` permission (the bootstrap `admin` does).

#### Steps

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

#### When something goes wrong

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

#### Upgrades and migration snapshots

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
| SQLite | A file `migration-snapshots/<database file name>-<id>.sqlite3` and a `.json` manifest next to it, in the folder that holds the database file (by default `<data directory>/migration-snapshots/ninlil-dns-<id>.sqlite3`). The files are readable by the owner only, and while Ninlil runs the snapshot file also has `-wal` and `-shm` files next to it. | `ls <data directory>/migration-snapshots` |
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

#### Settings

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

#### Read replicas and database clusters

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

#### Behaviour worth knowing

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
- **The MySQL account accepts connections from any host.** `src/script/database/mysql/init.sql`
  creates `'ninlil'@'%'`, which lets the account log in from every address. Once Ninlil
  runs, narrow it to the address Ninlil connects from. The steps are in
  [Narrow the MySQL account to the application host](./src/script/database/README.md#narrow-the-mysql-account-to-the-application-host).
- **MongoDB does not enforce one lease or one reservation per address.** The relational engines
  carry unique indexes on a lease's scope with its client identifier and with its IP address, and
  on a reservation's scope with its client identifier and with its IP address. The MongoDB
  indexes for the same fields are ordinary indexes, so two writers that run at the same moment
  could store two rows for one address. Inside one Ninlil process each DHCP protocol has one
  writer, so this does not happen there. Do not point the DHCP engines of two Ninlil instances at
  one MongoDB database.

### Backup and restore

#### What it does

**Backup & Restore** writes one file, with the extension `.ltbk`, that holds what Ninlil needs to
run again, and restores such a file. A backup holds:

- the database tables for configuration and identity: zones and records, keys and credentials,
  users, roles, sessions, API tokens, filtering, clients, forwarders, rewrites, webhooks, cluster
  peers and every DHCP table (scopes, reservations, options, tag rules and leases);
- the Data Protection key ring (`key-*.xml`), because every secret in the database opens only with
  it;
- the shared TLS certificate, including its private key;
- the runtime configuration file (`appsettings.json`), which holds the settings you saved in the
  console.

A backup leaves out history and state that belongs to one node: the query log, the operational log
and metrics, the zone change journal, webhook delivery attempts, secondary-zone refresh state and
cluster sync tombstones. It also leaves out the JWT signing key, the cluster node id, the root trust
anchor file and the ACME certificate files. A restore replaces all stored data with the content
of the file. There is no partial or merged restore. Ninlil takes no backups by itself; run the
backup when you want one.

#### Before you start

1. An account with `backup:read` to create a backup and `backup:write` to restore one (the
   bootstrap administrator has both), and its access token (see [REST API](#rest-api)). The
   commands below call it `<token>`.
2. A passphrase of at least 12 characters
   ([`NINLIL_DNS_SERVER_BACKUP_MINIMUM_PASSPHRASE_LENGTH`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering)).
   Ninlil keeps no copy of it. A lost passphrase makes the file unreadable. Keep the passphrase
   apart from the file, in a password manager for example. The commands below call it
   `<passphrase>`.
3. Memory for the whole archive: Ninlil holds it in memory while it builds or restores it, and
   refuses a request body above 2 GiB.
4. To restore: a running Ninlil you can sign in to, and the `.ltbk` file. A restore replaces
   the accounts too, so sign-in afterwards uses the accounts from the backup.

#### Steps

**Create a backup.**

1. In the console, open **Backup & Restore**, enter the passphrase twice, and choose **Download
   backup**. Or call the API:

   ```bash
   curl -k -o ninlil-backup.ltbk https://127.0.0.1:18443/api/backup -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"passphrase":"<passphrase>"}'
   ```

   The file `ninlil-backup.ltbk` appears in the current folder. Without a `passphrase`, Ninlil
   writes the file unencrypted.
2. Copy the file to a place that is not on the same machine, and keep the passphrase elsewhere.

**Restore a backup** on the same machine or on another one.

1. On another machine, install Ninlil first (see [Installation](#installation)) and start it once,
   so you can sign in with its bootstrap administrator.
2. Sign in. Open **Backup & Restore**, choose the file under **Restore from a backup**, type the
   passphrase and confirm the warning. Or call the API:

   ```bash
   curl -k https://127.0.0.1:18443/api/backup/restore -H "Authorization: Bearer <token>" -F archive=@ninlil-backup.ltbk -F passphrase='<passphrase>'
   ```

   ```text
   {"totalEntitiesImported":19,"entityCounts":{"Roles":1,"Users":2,"Sessions":8,"ApiTokens":1,"Zones":4,"Records":2, ...},"note":"Restore succeeded. Restart this process ..."}
   ```

   Leave out `-F passphrase=...` for an unencrypted file.
3. Restart Ninlil (`sudo systemctl restart ninlil`, `docker restart ninlil`, or
   `Restart-Service Ninlil`). The restored key ring, certificate and settings take effect at the
   restart.
4. To keep access tokens from the old machine valid, set
   [`NINLIL_JWT_SIGNING_KEY`](./ENVIRONMENT.md#identity-authentication--sso) on the other machine to
   the content of the old machine's `data/jwt-signing-key.txt` before the restart. Without it,
   clients get a fresh access token from their refresh token; see "Tokens after a restore" below.

#### Check that it worked

1. A file made with a passphrase starts with the five characters `LTBK1`; an unencrypted file starts
   with `LTBK0`:

   ```bash
   head -c 5 ninlil-backup.ltbk; echo
   ```

   ```text
   LTBK1
   ```

2. After a restore and a restart, Ninlil is healthy and holds the restored data:

   ```bash
   curl http://127.0.0.1:18080/status
   curl -k https://127.0.0.1:18443/api/zones -H "Authorization: Bearer <token>"
   ```

   ```text
   {"status":"Healthy","components":null}
   {"items":[{"id":"...","name":"example.test", ...}],"skip":0,"take":50,"totalCount":4}
   ```

   (sign in again first if the token is refused, as described below).

#### Behaviour worth knowing

- **A restore checks the whole file before it writes anything.** Ninlil decrypts the archive, reads
  every entry and checks it in memory. A wrong passphrase, a damaged or changed file or an
  unknown format returns `400` and leaves your data as it is.
- **On a relational database the restore is all or nothing.** The wipe and the import run in one
  transaction, so a database error keeps the old data. The key ring, certificate and settings file
  are replaced only after that transaction commits. On MongoDB the restore is not atomic (Ninlil
  logs a warning): an error after the wipe can leave a partly restored database.
- **The key ring is merged.** The keys from the file are added, and no key on the machine
  is deleted.
- **Only one restore runs at a time.** A second request returns `409` and changes nothing.
- **Tokens after a restore.** Refresh tokens and API tokens are database rows, so they come back.
  Access tokens are signed with the JWT key, which a backup does not hold. On a machine with
  another key, an access token from before is refused with `401` until the client uses its refresh
  token (`POST /api/auth/refresh`) or signs in again. The console does that for you. API
  tokens keep working.
- **A restore applies to issued access tokens at once.** Ninlil keeps each account's state in
  memory for 15 seconds (see [Security](#security)) and clears it when a restore ends, so a token
  from the same key is checked against the restored accounts, roles and security stamps on its next
  request. A token for an account the backup does not hold gets `401`.
- **An unencrypted backup is readable by anyone who gets the file.** It holds the key ring and
  the certificate's private key in plain text. The console warns while the passphrase is empty.
- **The query log and other history stay as they are.** A backup never captures them, and a
  restore never touches them.
- **SQLite files.** To copy the database file itself instead, stop Ninlil first, or copy all
  three files (`ninlil-dns.sqlite3`, `-wal`, `-shm`); see [Storage](#storage).

#### Settings

| Setting | Variable and row |
|---|---|
| Minimum passphrase length (12) | [`NINLIL_DNS_SERVER_BACKUP_MINIMUM_PASSPHRASE_LENGTH`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering) |
| Largest amount of data a restore reads (4 GiB) | [`NINLIL_DNS_SERVER_BACKUP_MAXIMUM_RESTORED_PAYLOAD_BYTES`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering) |

The key-ring folder and the settings-file path of a backup are set at startup, and a value you
configure is replaced; their rows say so.

#### When something goes wrong

- *The restore answers `400` with `Failed to decrypt this backup archive. The passphrase may be incorrect, or the archive may be corrupted or have been tampered with.`* Check the passphrase for typing errors and the file for damage (compare its size with the original).
- *Creating a backup answers `400` with `A backup passphrase must be at least 12 characters long.`* Use a longer passphrase, or none.
- *The restore answers `400` with `An archive file is required.`* The `archive` form field is missing or empty. Use `-F archive=@<file>`.
- *The restore answers `409`.* Another restore is still running. Wait for it to finish.
- *Sign-in fails after the restore.* The accounts and passwords are those of the backup. Use an account that existed when the backup was made.
- *Requests fail with `401` after you restore on another machine.* The access token is signed by the old JWT key. Sign in again, or refresh the token.

### Self-update

#### What it does

Ninlil can check whether a newer release exists and show a notice on the console's dashboard and
in `GET /api/self-update`. It never installs anything: you replace the program yourself, the way you
installed it. The check reads a small JSON document, the **release feed**, from an address you set.
Ninlil publishes no feed and has no built-in address, so the check does nothing until you set
[`NINLIL_DNS_SERVER_SELF_UPDATE_RELEASE_FEED_URL`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering).
Host a feed yourself, on any web server, when you want the notice.

#### Before you start

1. A web server that serves a JSON file over HTTP or HTTPS and that the Ninlil host reaches.
2. An account with `selfupdatesettings:write` to set the address, `selfupdate:manage` to start a check
   and `selfupdate:read` to read the result (the bootstrap administrator has them), and its access
   token (see [REST API](#rest-api)). The commands below call it `<token>`.
3. The version your Ninlil runs. A release build reports a date-based version such as
   `2026.9.24.1`; a build from source reports `0.0.0.0`. Read it:

   ```bash
   curl -k https://127.0.0.1:18443/api/self-update -H "Authorization: Bearer <token>"
   ```

   ```text
   {"configured":false,"currentVersion":"0.0.0.0","latestKnownVersion":null,"updateAvailable":false, ...,"updateInstructions":"No release feed is configured yet -- set one in Self-Update Settings to enable checking for a newer release."}
   ```

#### Steps

1. Write the feed file. Only `version` is required. Write the version in the same numeric form as
   `currentVersion` (`yyyy.m.d.increment`), not the release's `v...R...` name:

   ```json
   {
     "version": "2026.9.24.1",
     "releaseUrl": "https://example.com/releases/2026.9.24.1",
     "publishedAtUtc": "2026-09-24T00:00:00Z",
     "notes": "Free text shown with the notice."
   }
   ```

   Serve it at an address such as `https://example.com/ninlil-feed.json`. For a test, save it as
   `feed.json` and run `python3 -m http.server 8098 --bind 127.0.0.1` in that folder, then
   use `http://127.0.0.1:8098/feed.json` as the address.
2. Set the address (`<feed-url>`). Ninlil checks that it is an absolute HTTP or HTTPS address and
   does not fetch it yet. The change reaches the running server within about a second, so wait a
   moment before step 3:

   ```bash
   curl -k -X PATCH https://127.0.0.1:18443/api/self-update-settings -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"releaseFeedUrl":"<feed-url>"}'
   ```

   ```text
   {"note":"Written to the configuration file. This setting is bound via IOptionsMonitor, so it takes effect immediately -- no restart required."}
   ```

   The console page is **Settings**, **Self-Update**.
3. Run a check at once instead of waiting for the next one:

   ```bash
   curl -k -X POST https://127.0.0.1:18443/api/self-update/check -H "Authorization: Bearer <token>"
   ```

4. When the notice says a newer version is available, install it the way you installed Ninlil (see
   [Installation](#installation)). Ninlil has no action that applies an update.

#### Check that it worked

The check call returns the result. With a feed that names a version above the running one:

```text
{"configured":true,"currentVersion":"0.0.0.0","latestKnownVersion":"2026.9.24.1","updateAvailable":true,"releaseUrl":"https://example.com/releases/2026.9.24.1","releaseNotes":"Free text shown with the notice.","lastCheckedAtUtc":"2026-10-30T22:05:00.2806011+00:00","lastCheckError":null,"updateInstructions":"..."}
```

The dashboard shows the same notice.

#### Behaviour worth knowing

- **When checks run.** One check runs at startup, then one every
  [`NINLIL_DNS_SERVER_SELF_UPDATE_CHECK_INTERVAL`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering)
  (24 hours by default). The checker wakes every 15 minutes, so the wait rounds up to the next
  15-minute tick. With the automatic check switched off, only a manual check runs.
- **A failed check is retried after 15 minutes.** It does not wait for the full interval. A failure
  keeps the last good result visible and sets `lastCheckError`; an `updateAvailable` that
  a check found stays `true` through an outage.
- **The result is held in memory.** After a restart the status is empty until the startup check
  finishes.
- **Only `version` decides.** The check compares it with the running version and reports an update
  when it is strictly newer. Ninlil never opens `releaseUrl`. A feed without a `version`, or a
  version that is not a dotted number, makes the check fail with a message that says so.
- **No feed address, no request.** The status then reads `configured: false` and no failure is
  logged.

#### Settings

| Setting | Variable and row |
|---|---|
| Automatic check on or off | [`NINLIL_DNS_SERVER_SELF_UPDATE_ENABLE_AUTOMATIC_CHECK`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering) |
| Interval | [`NINLIL_DNS_SERVER_SELF_UPDATE_CHECK_INTERVAL`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering) |
| Feed address | [`NINLIL_DNS_SERVER_SELF_UPDATE_RELEASE_FEED_URL`](./ENVIRONMENT.md#webhooks-backup-self-update--clustering) |

#### When something goes wrong

- *The settings call answers `400` with `'<feed-url>' is not a valid absolute HTTP or HTTPS URL.`* Write the address with `http://` or `https://`.
- *`lastCheckError` reads `Response status code does not indicate success: 404 (File not found).`* The address is wrong or the file is missing. Open the address in a browser from the Ninlil host.
- *`lastCheckError` reads `The release feed document is missing a 'version' field.`* Add the field to the feed.
- *`lastCheckError` reads `'<version>' is not a valid dotted version string.`* Write the version as numbers separated by dots, for example `2026.9.24.1`.
- *The status stays `configured: false`.* The address is empty. Run step 2.

## Using the admin console

#### What it does

The admin console is a web application that Ninlil serves on its web ports. It is built into the
program, so there is nothing to install. It calls the same [REST API](#rest-api) as any other
client, so everything it shows or changes is checked against the signed-in account's permissions.
Entries the account cannot read are missing from the sidebar, and the **Presets & Settings** page
lists only the settings pages it may read.

The sidebar holds one entry per area: Dashboard, Zones, DNS Rewrites, TSIG Keys, Cluster, Cloud
Credentials, Filter Lists, Clients, Static Address Mappings, Client Groups, the DHCP pages,
Forwarders, Access Control Rules, Query Log, Webhooks, Presets & Settings, Users, Roles, API Tokens,
Backup & Restore and Services. The settings pages are grouped by subsystem: DNS Resolution & Protocol,
Filtering & Privacy, Zone Management, Identity & Access, Integrations & Delivery, System and DHCP.
The account menu in the top-right corner holds **Profile**, **Security** (your two-factor
sign-in) and **Log out**.

#### Before you start

1. A running Ninlil and a browser. The console answers on the web HTTPS port (443 by default; the
   [Quick start](#quick-start) uses 18443) and on the plain-HTTP port. Check that it answers:

   ```bash
   curl -k -s -o /dev/null -w '%{http_code}\n' https://127.0.0.1:18443/
   ```

   ```text
   200
   ```

2. A sign-in name and password. On a fresh installation use the bootstrap administrator (see
   [First run](#first-run)).

#### Steps

**Sign in.**

1. Open `https://<host>:<port>/` in the browser. The self-signed certificate Ninlil makes at the
   first start triggers a browser warning; accept it for a first look, or install a trusted
   certificate (see [Security](#security)).
2. Enter the sign-in name and password. If the account has two-factor sign-in, the console asks for
   the code next. If single sign-on is configured, the page also offers the identity provider.
3. The console opens the Dashboard. A page address you open before you sign in, such as `/zones`,
   comes back after the sign-in.

**Change a setting.**

1. Open **Presets & Settings** and choose the group and the page in the list on the left.
2. Edit the fields and choose **Save** at the bottom of the card.
3. Read the note that appears. The server writes it, and it says whether the change applies at
   once ("takes effect immediately -- no restart required") or needs a restart ("a restart is
   required before this change takes effect"). A change that takes effect at once reaches the
   running server within about a second.
4. If the note asks for a restart, restart Ninlil (see the guide of the feature) when it suits you.
   The value is saved in `appsettings.json` and survives the restart.

**Replace the bootstrap account and add two-factor sign-in.** Follow the steps in
[Security](#security).

#### Check that it worked

Sign in as an account with fewer permissions: its sidebar shows fewer entries. A direct address to
a page it may not read, such as `/users`, shows a page that says it lacks the permission, and the server answers the
API call behind it with `403`. Check one case without a browser:

```bash
curl -k -s -o /dev/null -w '%{http_code}\n' https://127.0.0.1:18443/api/users -H 'Authorization: Bearer <token-without-users-read>'
```

```text
403
```

#### Behaviour worth knowing

- **Pages send only what you changed, and Save waits for a change.** Eight settings pages compare
  their form with what they loaded and send only the fields that differ: **ACME / Certificates**,
  **Cluster**, **DHCP** (the core card only), **Pipeline**, **Query logging**, **Recursive
  Resolver**, **Single Sign-On**, and the **Transports** cards for DNS-over-TLS, DNS-over-HTTPS,
  DNS-over-QUIC and DNSCrypt. Their **Save** button stays disabled until something changes, and its
  tooltip says "No changes to save". The reason: the server picks its note from the fields in
  the request. Saving only a setting that applies at once says so, but a request that also holds
  a field that needs a restart says a restart is still needed. A list or table field is sent
  whole when any entry changes, because a JSON merge patch replaces a list as one value.
- **The other settings pages send the whole section.** This includes the pages whose settings a
  cluster copies (**Event broker**, **Rate limiting**, **DNS Cookies**, **Reverse proxy**,
  **DNS Rewrites**) and the cards whose note is fixed (the DHCP protocol, relay, TFTP and Router
  Advertisement cards, and the Plain DNS transport card). Their **Save** button is always on. A
  secret field you leave blank (the reverse-proxy shared secret, for example) is left out of the
  request, so the stored secret stays.
- **The Storage page shows what the server reports.** It lists the environment variables for a
  read replica and the engine-specific cluster settings, as the server returns them, so the page
  cannot name a variable the server ignores. While a read replica is configured, the page hides the
  switch form and shows that Ninlil refuses a switch.
- **The Identity page has no lockout fields.** Ninlil does not lock accounts after wrong passwords;
  see [Security](#security). The API still returns the lockout values, but they have no effect.
- **A secret is shown once.** Creating a TSIG key, a cloud credential, a webhook or an API token,
  creating a cluster invite, resetting a password and generating recovery codes open a dialog with
  the secret. Copy it before you close the dialog; Ninlil cannot show it again.
- **The console renews its session without you.** When a request gets `401`, it asks for a fresh
  access token once and repeats the request. If that fails it goes to the sign-in page.
- **A password or two-factor change signs you out.** After you change your password on **Profile**,
  or confirm an authenticator, turn two-factor off or generate recovery codes on **Security**, the
  console signs you out and the sign-in page says what changed. Close the recovery-code dialog
  after you copy the codes; the sign-out follows. Sign in again with the new password or with a
  code. The other sessions of the account end too (see [Security](#security)).
- **The live feed.** The dashboard receives events through SignalR at `/hubs/events`. An account
  sees an event type only when it holds the permission that reads the same data in the REST API
  (`querylog:read` for queries). The permission is read when the connection opens, so a change
  applies at the next reconnect, which happens when the access token expires.
- **The console is a client of the API.** Anything it does, you can do with `curl`. The
  [REST API](#rest-api) section shows how.

#### Settings

| Setting | Variable and row |
|---|---|
| Web ports and addresses | [Web host](./ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding) |
| Default theme, palette, site title and logo | The `NINLIL_DNS_SERVER_APPEARANCE_*` and `NINLIL_DNS_SERVER_BRANDING_*` rows in [Web host](./ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding). Console pages: **Settings**, **Appearance** and **Branding**. |
| Where saved settings are written | [`NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH`](./ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding) |

#### When something goes wrong

- *`/` answers `404`.* The program has no console. A build from source without the Angular build
  has none; use the published binary, the packages or the container image, or build the console as
  in [Build and run from source](#build-and-run-from-source).
- *The browser warns about the certificate.* The self-signed certificate is the cause. Accept it, or
  install a trusted certificate (see [Security](#security)).
- *Save is disabled.* Nothing changed after the page loaded or saved. Edit a field.
- *A save answers with a restart note and the setting seems unchanged.* The page shows the saved
  value, and the running program uses it after the restart. A setting set by an environment variable
  stays at the variable's value; remove the variable to use the saved one.
- *A page is missing from the sidebar.* The account lacks the read permission for it. Ask an
  administrator for the role (see [Security](#security)).
- *Every sign-in answers `429`.* The address reached the sign-in limit; see [Security](#security).

## REST API

#### What it does

Everything the console does, and more, is a REST call. The API speaks JSON over HTTPS and covers
every DNS and DHCP resource, every settings page, and account and role management. The commands in
this section use the Quick start ports (`18443`); the default HTTPS port is `443`. They use `-k`
because the self-signed certificate that Ninlil makes is not trusted by `curl`.

| What | Where |
|---|---|
| Swagger UI, to read and try every call | `/swagger` |
| The OpenAPI document (JSON) | `/swagger/v1/swagger.json` |
| ReDoc reference | `/documentation` (the page loads its viewer script from `cdn.jsdelivr.net`, so the browser needs internet access) |
| Health | `GET /status` |
| Sign in | `POST /api/auth/login` |

Two credentials work. An **access token** (a JSON Web Token, JWT) comes from signing in and lasts
15 minutes. An **API token** is a long-lived secret you create for a program. Both carry the
permissions of the account behind them, and each call needs the permission its route names, such as
`zones:write`.

#### Before you start

1. A running Ninlil, the account name and the password (see [Quick start](#quick-start)).
2. `curl`. Check it with `curl --version`; it prints a version line.

#### Steps

1. Sign in. The answer holds an access token and a refresh token. The commands below keep them in
   the shell variables `TOKEN` and `REFRESH`; the `sed` commands cut the values out of the answer.
   If you prefer, copy the values by hand and use them in place of `$TOKEN` and `$REFRESH`:

   ```bash
   LOGIN=$(curl -k -s https://127.0.0.1:18443/api/auth/login -H 'Content-Type: application/json' -d '{"userName":"admin","password":"Change-Me-Now1!"}')
   echo "$LOGIN"
   TOKEN=$(echo "$LOGIN" | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')
   REFRESH=$(echo "$LOGIN" | sed -n 's/.*"refreshToken":"\([^"]*\)".*/\1/p')
   ```

   ```text
   {"requiresTwoFactor":false,"mfaToken":null,"accessToken":"eyJhbGciOi...","expiresAtUtc":"2026-10-30T22:54:51.3364873+00:00","refreshToken":"a0iJqn-Vzk..."}
   ```

   If the answer says `"requiresTwoFactor":true`, the account uses two-factor sign-in. Send the
   `mfaToken` from the answer with the code to `POST /api/auth/login/two-factor`:
   `{"mfaToken":"<mfa-token>","code":"123456","provider":"Authenticator"}` (`provider` is
   `Authenticator`, `Email` or `RecoveryCode`). That answer holds the access token.
2. Call the API with the token in an `Authorization: Bearer` header. List the zones:

   ```bash
   curl -k -s https://127.0.0.1:18443/api/zones -H "Authorization: Bearer $TOKEN"
   ```

   ```text
   {"items":[],"skip":0,"take":50,"totalCount":0}
   ```

3. Create something with `POST` and a JSON body. Send the header `Content-Type: application/json`.
   A success answers `201` and the created object:

   ```bash
   curl -k -s -w '\n[HTTP %{http_code}]\n' https://127.0.0.1:18443/api/zones -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"name":"one.test","type":"Primary"}'
   ```

   ```text
   {"id":"c20edd76-612e-4ac4-ab46-27637a65d666","name":"one.test","type":"Primary","isDnssecSigned":false, ...}
   [HTTP 201]
   ```

   Copy the `id`. The commands below call it `<zone-id>`.
4. Change part of an object with `PATCH`. A `PATCH` body is an RFC 7396 JSON merge patch: a field you
   leave out stays as it is, a field you set replaces the old value, and `null` clears a field that
   may be empty. Change one field and clear another:

   ```bash
   curl -k -s -w '[HTTP %{http_code}]\n' -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"isDisabled":true,"comments":null}'
   ```

   ```text
   [HTTP 204]
   ```

   `204` has no body. Read the zone again to see the result.
5. Page through a long list with `skip` (how many items to leave out) and `take` (how many to
   return). The answer repeats both and adds `totalCount`:

   ```bash
   curl -k -s "https://127.0.0.1:18443/api/zones?skip=1&take=1" -H "Authorization: Bearer $TOKEN"
   ```

   ```text
   {"items":[{"id":"4256d20d-...","name":"three.test", ...}],"skip":1,"take":1,"totalCount":3}
   ```

6. Renew the access token before it expires, with the refresh token from step 1. Each refresh token
   works once, and the answer holds the next one (put it in `REFRESH` for the next renewal):

   ```bash
   curl -k -s https://127.0.0.1:18443/api/auth/refresh -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$REFRESH\"}"
   ```

   ```text
   {"accessToken":"eyJhbGciOi...","expiresAtUtc":"2026-10-30T22:54:51.8835126+00:00","refreshToken":"Trqd_-lmMt..."}
   ```

7. For a program that runs unattended, create an API token. The answer shows its `rawToken` once:

   ```bash
   curl -k -s https://127.0.0.1:18443/api/api-tokens -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"name":"backup script","oneTime":false}'
   ```

   ```text
   {"rawToken":"<api-token>","token":{"id":"b5375356-...","name":"backup script","type":"Static","expiresAtUtc":null, ...}}
   ```

   Use it with the header `Authorization: ApiToken <api-token>`. `"oneTime":true` makes a token
   that works for one call. Add `"expiresAtUtc":"2027-08-31T00:00:00+00:00"` to give it an end date.
   Revoke a token with `DELETE /api/api-tokens/<id>`.
8. Sign out by revoking the refresh token:

   ```bash
   curl -k -s -o /dev/null -w '%{http_code}\n' https://127.0.0.1:18443/api/auth/logout -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$REFRESH\"}"
   ```

   ```text
   204
   ```

#### Check that it worked

Step 2 returned a JSON object with `items`, and step 3 returned `[HTTP 201]`. A call without a
token is refused:

```bash
curl -k -s -o /dev/null -w '%{http_code}\n' https://127.0.0.1:18443/api/zones
```

```text
401
```

Open `https://127.0.0.1:18443/swagger` in a browser, choose **Authorize**, paste the access token
(without the word `Bearer`) and try a call.

#### Behaviour worth knowing

- **Errors are JSON with a status.** `400` means the request is wrong, and the body names the
  field:

  ```text
  {"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"$.type":["'Bogus' is not a valid value for ZoneTypeEnum."]},"traceId":"..."}
  ```

  `401` means no valid credential, `403` means the account lacks the permission, `404` an unknown
  id, `409` a name that is taken, `415` a missing `Content-Type: application/json` header,
  and `429` too many sign-in attempts (see [Security](#security)).
- **Enum values are names, as text.** A zone `type` of `Primary` works; the names are not case
  sensitive, so `primary` also works. A name Ninlil does not know gives `400`, and so does a JSON
  number: `"type":1` answers `Expected a string value for ZoneTypeEnum but found Number.` The
  Swagger UI lists the valid names for every field.
- **`PATCH` ignores a field it does not know.** A misspelled name in a `PATCH` body is skipped and
  the answer is still `204`. Check the result with a `GET`.
- **A settings `PATCH` answers with a note.** The note says whether the change is live or needs a
  restart, as on the console. A live change reaches the running server within about a second.
- **Lists page by offset.** `take` defaults to 50, and a value above 500 is cut to 500. A
  negative `skip` counts as 0, and a `take` of 0 or less counts as 50.
- **`GET /status` has two views.** Without a credential, or without the `status:read` permission, it
  returns only the overall state. With the permission it adds one entry per subsystem.
- **There is no API version in the address and no CORS.** A browser page from another origin cannot
  call the API; the console and the API share one origin. A `curl` call is not affected.
- **The API answers on both web ports.** Use the HTTPS port, because the plain-HTTP port sends the
  token in the clear.

#### Settings

| Setting | Variable and row |
|---|---|
| Access token lifetime (15 minutes) | [`NINLIL_JWT_ACCESS_TOKEN_LIFETIME`](./ENVIRONMENT.md#identity-authentication--sso) |
| Refresh token lifetime (30 days; 12 hours without activity for a browser session) | [`NINLIL_JWT_REFRESH_TOKEN_LIFETIME`, `NINLIL_JWT_BROWSER_SESSION_REFRESH_TOKEN_LIFETIME`](./ENVIRONMENT.md#identity-authentication--sso) |
| Sign-in limit | [`NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_*`](./ENVIRONMENT.md#identity-authentication--sso) |
| Ports and addresses | [Web host](./ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding) |

#### When something goes wrong

- *`curl` prints `SSL certificate problem: self-signed certificate`.* Add `-k`, or install a trusted certificate (see [Security](#security)).
- *Every call answers `401`.* The token expired (15 minutes) or is missing. Sign in again (step 1) or refresh it (step 6). With an API token, check that the header starts with `ApiToken ` and not `Bearer `.
- *A call answers `403`.* The account lacks the permission the route needs. Swagger UI shows it in the call's description.
- *A call answers `415`.* Add `-H 'Content-Type: application/json'`.
- *A call answers `400` with `is not a valid value for ...`.* Use one of the names the Swagger UI lists.
- *`429` on sign-in.* See [Security](#security).

## Running with Docker

The published image (`git.lantea.one/lantea.one/ninlil`) is the same single combined host
described above (the DNS and DHCP engines, the REST API and the admin console in one process),
built for a small attack surface:

- **Chiseled base image** (`mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled-extra`): no
  shell and no package manager. It holds the libraries a self-contained .NET program needs (C and
  C++ runtime, ICU, time-zone data, OpenSSL) plus `libmsquic` 2.6.2 for DNS-over-QUIC and its
  dependency `libnuma1`, which the image build adds from a pinned list.
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

#### Before you start

1. Docker installed. Check it with `docker --version`.
2. Port 53 free on the host. On a Linux desktop `systemd-resolved` usually holds it for its stub
   listener, so publishing `53:53` fails with `address already in use`: publish another host port
   (`-p 5353:53/udp -p 5353:53/tcp`) or disable the stub listener.

#### Steps

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

#### Check that it worked

```bash
docker logs ninlil
curl http://localhost/status
```

The log ends with `Now listening on: http://[::]:80`, `Now listening on: https://[::]:443` and
`Application started.`. The status call prints `{"status":"Healthy","components":null}`.

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

**Fork pull requests.** The runners run steps on the host as a user in the Docker group, and a pull
request from a fork runs its own copy of every workflow, so the rule above cannot stop a malicious
fork by itself. Workflow runs from fork pull requests wait for a maintainer's approval, and the
maintainer reads the diff of `.forgejo/` and every script it runs before approving a run.

**Linux packages.** The package definition is [`src/linux-packages/nfpm.yaml`](./src/linux-packages/nfpm.yaml);
the pipeline builds it with [nFPM](https://nfpm.goreleaser.com/) through
[`linux-packages.sh`](./.forgejo/scripts/linux-packages.sh) and tests it with
[`linux-packages-smoke.sh`](./.forgejo/scripts/linux-packages-smoke.sh). To run that test on your
machine, build the packages with `in-container.sh packages` (it needs a `dist/` folder holding
`ninlil-linux-amd64` and `ninlil-linux-arm64` and the `DEB_VERSION`, `RPM_VERSION` and `RPM_RELEASE`
variables) and run `.forgejo/scripts/linux-packages-smoke.sh dist`.

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

## Contributing

This section is for people who change Ninlil. It holds the development commands, the branch model,
the conventions and two worked examples. [`AGENTS.md`](./AGENTS.md) is the guide for AI agents and
a fast orientation for people.

### Development

#### Backend (`src/dotnet/`)

```bash
dotnet tool restore                      # one-time: restores dotnet-ef, csharpier, reportgenerator
dotnet restore Ninlil.slnx
dotnet build Ninlil.slnx -c Release      # should build with 0 warnings
dotnet csharpier check .                 # formatting gate — CI enforces this
dotnet test Ninlil.slnx -c Release       # the full xUnit test suite
```

Run `dotnet csharpier format .` before committing — CI runs `dotnet csharpier check .` and will
fail on anything that doesn't already match.

#### Frontend (`src/angular/`)

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

#### Standard verification loop

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

#### Publishing the binaries

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

### Project layout

[`AGENTS.md`](./AGENTS.md#repository-layout) holds the table of every folder and root file, with the
role of each .NET project family. The files you meet first in the repository root are this
`README.md`, [`ENVIRONMENT.md`](./ENVIRONMENT.md), [`NOTICE.md`](./NOTICE.md), [`LICENSE`](./LICENSE),
`AGENTS.md`, the solution file `Ninlil.slnx`, the [`Dockerfile`](./Dockerfile) and the example
[`docker-compose.yml`](./docker-compose.yml). The reasoning behind a design choice lives with the
code: every class and public member carries a doc comment explaining why it is shaped the way it is.

## License

MIT — see [`LICENSE`](./LICENSE). See [`NOTICE.md`](./NOTICE.md) for the clean-room statement and
third-party dependency license table.

