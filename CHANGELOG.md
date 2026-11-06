# Changelog

Release notes for Ninlil, newest release first. The `Unreleased` section holds the release that
the next release branch cuts; a maintainer renames it to the release's name (see [Cutting a
release](./CONTRIBUTING.md#cutting-a-release)). Each section describes Ninlil as that release
ships. Installation, operation and every setting are in the [README](./README.md) and
[`ENVIRONMENT.md`](./ENVIRONMENT.md); these notes link the guide that explains each item.
Each entry ends with a link to its issue, for example
`([#42](https://git.lantea.one/lantea-one/ninlil/issues/42))`. The full URL keeps the link working
outside Forgejo.

## Unreleased

### Security

- **The Data Protection key ring is encrypted at rest.** Set `NINLIL_DATA_PROTECTION_CERTIFICATE_PATH`
  (an RSA certificate in a PFX file) to encrypt it with that certificate on every platform;
  `NINLIL_DATA_PROTECTION_PREVIOUS_CERTIFICATE_PATH` keeps entries written under the old certificate
  readable after a change. With no certificate, Windows encrypts it with DPAPI for the account that
  runs Ninlil, which cannot be read on another machine or account. Elsewhere it stays unencrypted in
  an owner-only folder. A certificate setting that does not work stops the start with exit code 78.
  A restore refuses a backup whose key ring this server cannot read and names the setting to fix.
  See [Encrypt the key ring](./README.md#security) and
  [`ENVIRONMENT.md`](./ENVIRONMENT.md#data-protection-key-ring).
  ([#42](https://git.lantea.one/lantea-one/ninlil/issues/42))

### Accepted risks

- **The JWT signing key is stored unencrypted at rest, and so is the Data Protection key ring when no
  certificate is set on Linux and macOS.** Both live in the data directory. On Linux and macOS
  Ninlil sets the folder to mode 0700 and the files to 0600; on Windows you restrict the folder with
  `icacls`. Anyone who can read the whole data directory can mint tokens for any enabled account,
  and, with an unencrypted key ring, decrypt the stored secrets (TSIG secrets, DNSSEC private keys,
  cloud credentials, TOTP keys, the ACME account key). Set a Data Protection certificate to encrypt
  the key ring. Keep the data directory readable by the administrators of the host only, and protect
  backups with a passphrase. See [Security](./README.md#security).
  ([#42](https://git.lantea.one/lantea-one/ninlil/issues/42))

### Deprecated

- **The identity settings lockout fields are deprecated.** `defaultLockoutTimeSpan`,
  `maxFailedAccessAttempts` and `allowedForNewUsers` of `/api/identity-settings`, and the
  `NINLIL_IDENTITY_LOCKOUT_*` variables, never had an effect: Ninlil has no account lockout, and
  sign-in is rate limited per client address instead. The OpenAPI document marks the three fields
  `deprecated`. Reads and writes keep working; a later release removes them
  ([#32](https://git.lantea.one/lantea-one/ninlil/issues/32)).

### Removed

- **Settings that had no effect are gone.** These variables never changed anything, because
  DNS-over-HTTPS is served by the web listener and startup sets the two backup paths itself:
  - `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_PORT`
  - `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_CERTIFICATE_PFX_PATH`
  - `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_CERTIFICATE_PFX_PASSWORD`
  - `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_CERTIFICATE_SELF_SIGNED_CERTIFICATE_STATE_PATH`
  - `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_CERTIFICATE_ALLOWED_PROTOCOLS`
  - `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_CERTIFICATE_CERTIFICATE_RELOAD_POLL_INTERVAL`
  - `NINLIL_DNS_SERVER_BACKUP_DATA_PROTECTION_KEY_RING_DIRECTORY`
  - `NINLIL_DNS_SERVER_BACKUP_RUNTIME_CONFIGURATION_FILE_PATH`

  An environment that still sets one starts as before and logs a warning that the variable
  matches no known setting and was ignored; the same keys in a configuration file are ignored
  silently. Remove them to silence the warning
  ([#27](https://git.lantea.one/lantea-one/ninlil/issues/27)).
- **Library API (NuGet).** `DnsOverHttpsOptions.Port` and `Certificate` moved to the new
  `DnsOverHttpsListenerOptions`, which `DnsOverHttpsHostedService` now also takes in its
  constructor; Ninlil itself does not bind it. `BackupOptions.DataProtectionKeyRingDirectory` and
  `RuntimeConfigurationFilePath` moved to the new `BackupPathSettings`, which `SystemBackupService`
  takes in its constructor. Code that builds these types itself needs the new argument.

### Behaviour worth knowing

- **A node that joins a cluster receives the inviting node's configuration, and a peer that missed
  a change catches up.** When a join completes, the inviting node sends the new peer its
  filter lists, the entries of local lists, access control rules, global rewrites, forwarders,
  users, roles, deletion records and the five synced settings sections, after the next heartbeat
  that reaches it. A failed live delivery, or a change made while a peer is `Unreachable`, makes
  the next successful heartbeat send that peer a full pass too, so a node that was down catches up
  without another save. A failed pass runs again in full, after a wait that doubles from the heartbeat interval up to 1 hour. Zones, records, DHCP data, the entries
  of subscribed lists and rewrites scoped to a client or group are not copied. The joining node's
  own bootstrap account and role merge by the newest-write rule and the loser is renamed: delete the
  joining node's own pair after the join. Membership stays pairwise and changes are not relayed.
  Password hashes, two-factor secrets and synced secrets travel inside the signed, encrypted
  cluster channel, so treat an invite code as full trust. The log shows each pass as `Config
  backfill to peer node '<name>' started`, `done: <n> items delivered` or `failed`. See
  [Clustering, events and webhooks](./README.md#clustering-events-and-webhooks)
  ([#59](https://git.lantea.one/lantea-one/ninlil/issues/59)).
- **Renaming a DHCP scope keeps its leases.** A lease refers to its scope by id, so a renamed
  scope keeps its leases, the DHCP engine renews them, and their DNS records stay registered. On
  upgrade, each existing lease takes the id of the scope with its scope name, and a lease whose
  scope name matches no scope (its scope was deleted) is deleted. A backup taken before this
  change restores the same way: its leases are relinked by scope name and the orphans are
  dropped. See [DHCP, DHCPv6 and Router
  Advertisement](./README.md#dhcp-dhcpv6-and-router-advertisement)
  ([#58](https://git.lantea.one/lantea-one/ninlil/issues/58)).
- **Settings saved to a custom runtime configuration file now take effect.** If
  `NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH` points at a file other than
  `appsettings.json`, Ninlil now loads and watches that file as a configuration source: its values
  apply at start, and a saved change applies at once where the page says so. Values already saved
  in that file, which Ninlil ignored until now, take effect after the upgrade. They override
  `appsettings.json` and `appsettings.{Environment}.json`; `NINLIL_*` variables and command-line
  arguments still override them. Review or clear the file before you upgrade. Ninlil creates
  the file's folder at start when it is missing, so a mistyped path leaves an empty folder. See
  [Configuration](./README.md#configuration)
  ([#314](https://git.lantea.one/lantea-one/ninlil/issues/314),
  [#321](https://git.lantea.one/lantea-one/ninlil/issues/321)).
- **Enum fields in API requests take names only.** A request body field that holds an enum
  (for example a zone's `type`) accepts a declared name. Names match without regard to case,
  and surrounding spaces are ignored. A flags field takes a comma list of names, such as
  `"Sunday, Monday"` for a schedule's days. A digit string (`"1"`), a JSON number (`1`) or a
  value that is no name (`"99"`) now answers `400` with a message that lists the valid values,
  and a `null` for a required enum answers `400` too. This covers create and update bodies,
  `PATCH` merge-patch bodies, the DNSSEC policy's allowed algorithms, the storage switch's
  encryption mode and the settings pages that take enum text. Responses are unchanged: they
  write names, and a number only for a code that has no name. Enums inside a record's `data`
  payload are not covered by this rule. Enum query-string parameters and route segments
  (a client list's `presence` and `sort`, the DHCP option catalog's `protocolVersion`, the
  `{kind}` of `/api/dhcp-services/{kind}/start`) follow it too: a digit string or an undefined
  name answers `400` with the valid names. Scripts that send numbers
  must send the names that the OpenAPI document (`/swagger/v1/swagger.json`) lists for the
  field. The MySQL load-balance and MongoDB read-preference replica settings refuse comma lists
  and numbers in the same way. See [REST API](./README.md#rest-api)
  ([#296](https://git.lantea.one/lantea-one/ninlil/issues/296),
  [#316](https://git.lantea.one/lantea-one/ninlil/issues/316),
  [#327](https://git.lantea.one/lantea-one/ninlil/issues/327)).

- **A zone transfer or DNSCrypt answer over TCP is no longer lost when the client sent one more
  message.** After the last reply the server shuts down its side, discards what the client still
  sends for up to 2 seconds, and then closes, so the close is not a connection reset. On Windows
  a reset made the client throw away the final reply it had not read yet.
  ([#12](https://git.lantea.one/lantea-one/ninlil/issues/12))

## v2026.10.31.1

Ninlil is a DNS, DHCPv4, DHCPv6 and Router Advertisement server suite that runs as one process with
a REST API and an admin console.

### Highlights

- **A DNS server for every role.** Primary, secondary, stub, forwarder and catalog zones; recursive
  resolution with DNSSEC validation; DNSSEC signing with automated key rollover. See [Zones and
  records](./README.md#zones-and-records), [DNSSEC](./README.md#dnssec) and [Forwarding and
  recursion](./README.md#forwarding-and-recursion).
- **Every major DNS transport.** Plain UDP and TCP, DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC
  and DNSCrypt v2; every transport beyond plain DNS is off until you enable it. See [Encrypted
  transports and DNS64](./README.md#encrypted-transports-and-dns64).
- **Zone transfers, signed updates and certificates.** AXFR and IXFR over TCP, TLS and QUIC with
  TSIG, RFC 2136 dynamic updates, cloud DNS sync for Cloudflare, AWS Route 53 and DigitalOcean, and
  an ACME DNS-01 client. See [Secondary zones and transfers](./README.md#secondary-zones-and-transfers),
  [Cloud DNS sync](./README.md#cloud-dns-sync) and [Certificates
  (ACME)](./README.md#certificates-acme).
- **Filtering and parental controls.** Block lists, blocked services, Safe Search, rewrites,
  per-client and per-group policy and schedules. See [Filtering, rewrites and parental
  controls](./README.md#filtering-rewrites-and-parental-controls).
- **DHCP in the same process.** DHCPv4, DHCPv6 with prefix delegation, Router Advertisement, relay
  agents, a read-only TFTP server for PXE and automatic DNS registration of leases. See [DHCP,
  DHCPv6 and Router Advertisement](./README.md#dhcp-dhcpv6-and-router-advertisement) and [TFTP and
  PXE](./README.md#tftp-and-pxe).
- **A REST API and an admin console.** Every resource is in the API, which Swagger UI and ReDoc
  document. The Angular console covers the same resources with a live event feed. Sign-in supports
  two-factor codes, API tokens and OpenID Connect. See [Using the admin
  console](./README.md#using-the-admin-console), [REST API](./README.md#rest-api) and
  [Security](./README.md#security).
- **Clusters, events and webhooks.** Nodes share configuration, users and roles, and publish events
  to webhooks and to a message broker. See [Clustering, events and
  webhooks](./README.md#clustering-events-and-webhooks).
- **Five storage engines and encrypted backups.** SQLite, PostgreSQL, MySQL, SQL Server and
  MongoDB, with migration snapshots, read replicas and passphrase-encrypted backups. The query
  path and the storage layer are measured with the benchmark project (see the Benchmark section of
  [`AGENTS.md`](./AGENTS.md#benchmark)). See [Storage](./README.md#storage) and [Backup and
  restore](./README.md#backup-and-restore).

### Platform support

| Artefact | What runs | Tested by the pipeline |
|---|---|---|
| `ninlil-linux-amd64` binary, Arch Linux, Deb and RPM packages (amd64), container image | Everything: DNS, DHCP, Router Advertisement, TFTP, REST API, console | The .NET and console test suites run in Linux containers. Each Deb and RPM package is installed and started on Debian 12, Ubuntu 22.04, Ubuntu 24.04, Rocky Linux 9 and Fedora 41. |
| `ninlil-linux-arm64` binary, arm64 packages and image layer | The same as amd64 | Built from the same sources. No job starts them. |
| `ninlil-macos-amd64`, `ninlil-macos-arm64` | DNS, REST API and console. DHCP, Router Advertisement and passive device presence need Linux. | Built. No job starts them. |
| `ninlil-windows-amd64.zip` (the executable and `msquic.dll`) | DNS, REST API and console. DHCP, Router Advertisement and passive device presence need Linux. | A smoke test unzips it, starts the executable and checks `/status`, the console, a DNS query over UDP, HTTPS and DNS-over-TLS on the first start, and DNS-over-QUIC. There is no Windows test suite. |

Binaries are unsigned; the macOS binaries are ad-hoc signed and not notarized. Check every download
against `SHA256SUMS`. See [Download a binary](./README.md#download-a-binary),
[Windows](./README.md#windows) and [Debian, Ubuntu, Fedora, RHEL and Rocky
Linux](./README.md#debian-ubuntu-fedora-rhel-and-rocky-linux).

### Behaviour worth knowing

Sign-in and sessions ([Security](./README.md#security)):

- An access token follows its account at every request. Disabling or deleting the account, locking
  it out, or changing its roles or permissions applies to a token that already exists on the next
  request. Ninlil keeps each account's state in memory for 15 seconds, and a change made through the
  API, the console or cluster sync clears it at once.
- A password change, an administrator's password reset, a password reset by link, confirming an
  authenticator, turning two-factor off and generating recovery codes each end every session of the
  account. Access tokens answer `401` and refresh tokens stop working, including the changer's.
  The console signs the user out and says why. API tokens keep working and are revoked by name.
- A backup restore clears the access-token check. A restore on a machine with another JWT key
  refuses the old access tokens with `401` until the client refreshes or signs in again; refresh
  tokens and API tokens come back with the database. See [Backup and
  restore](./README.md#backup-and-restore).
- A JWT signing key shorter than 16 bytes stops the start with exit code 78 and a message that names
  the remedy. See [`NINLIL_JWT_SIGNING_KEY`](./ENVIRONMENT.md#identity-authentication--sso).
- A sign-in or Forgot password request that the rate limit refuses answers `429` with a
  `Retry-After` header that holds the seconds left in the window. Sign-in never locks an account.

Transports and protocols ([Encrypted transports and DNS64](./README.md#encrypted-transports-and-dns64),
[Secondary zones and transfers](./README.md#secondary-zones-and-transfers)):

- An enabled DNS transport that cannot bind its port logs one error, shows as unhealthy in
  `GET /status` with the reason, and leaves the rest of Ninlil running. The web listener must bind,
  or Ninlil stops.
- DNS-over-HTTPS answers carry `Content-Length`.
- A request signed with a truncated TSIG MAC (HMAC-SHA1-96, HMAC-SHA256-128 and similar) is
  verified. A MAC outside the limits of RFC 8945 gets `FORMERR`, and HMAC-SHA1 with 10 or 11 octets
  gets `BADTRUNC`.
- A dynamic update for a zone class other than `IN` gets `NOTAUTH`.
- A DNS64 synthesized AAAA record gets the smaller of the A record's TTL and the SOA TTL of the empty
  AAAA answer, or 600 seconds at most when the answer has no SOA.
- DNS-over-QUIC resets a stream the server gives up on with `DOQ_INTERNAL_ERROR` and closes the
  connection with `DOQ_PROTOCOL_ERROR` for a message that does not decode.
- A PROXY protocol v1 header must end with CRLF within its first 107 characters.
- The DNSCrypt client pads UDP queries to at least 256 bytes and TCP queries by a random 1 to 256
  bytes, and it fetches the certificate over TCP when UDP fails, is truncated or yields no usable
  certificate. The DNSCrypt listener closes a TCP connection after one answer. A DNSCrypt
  certificate is valid for 28 hours by default.
- The forwarder form's resolver dropdown offers Mullvad DNS, DNS4EU, AdGuard DNS, Control D and
  CleanBrowsing, with each provider's filtering variants over plain DNS, DNS-over-TLS,
  DNS-over-HTTPS and, for AdGuard DNS and Control D, DNS-over-QUIC. See [Forwarding and
  recursion](./README.md#forwarding-and-recursion).
- A Blocked Services toggle on the settings page blocks the service for every client, including
  clients that no group matches. Client and group toggles add to it. See [Filtering, rewrites and
  parental controls](./README.md#filtering-rewrites-and-parental-controls).

DHCP ([DHCP, DHCPv6 and Router Advertisement](./README.md#dhcp-dhcpv6-and-router-advertisement)):

- A DHCPv6 server ignores a Solicit, Confirm or Rebind that carries a Server Identifier, and an
  Information-request that names another server or carries an IA.
- A Router Advertisement Route Information option may carry a longer prefix field than its prefix
  length needs (RFC 4191 section 2.3).
- A DHCPv4 relay forwards a request that another relay already stamped with a `giaddr` without
  adding or replacing Relay Agent Information. A relay that adds the option drops a request whose
  `giaddr` is the relay's address.
- A reservation must hold an address of its scope's IP version. The API answers `400` otherwise.
- A scope range must lie inside one subnet. An enabled scope must not share an address with another
  enabled scope of the same IP version and kind. An IPv4 range must not include the network or
  broadcast address of its subnet, except in a `/31` or `/32`. The API answers `400` otherwise.
- An enabled DHCPv4, DHCPv6, Router Advertisement or TFTP service that cannot bind its socket logs
  one error, shows in the `dhcpServices` check of `GET /status` with the reason, and leaves the rest
  of Ninlil running.

### Known limitations

- **DNS-over-QUIC needs `libmsquic` 2.4.20 or later.** An older `libmsquic` (2.4.11 was tested)
  aborts the whole process when a DNS-over-QUIC peer advertises a maximum UDP payload size of 1200
  bytes over IPv4. The Deb and RPM packages suggest `libmsquic (>= 2.4.20)` and the Arch packages
  list `msquic>=2.4.20` as optional, and neither installs it. The container image ships 2.6.2.
  Install 2.4.20 or later before you enable DNS-over-QUIC. See [Encrypted transports and
  DNS64](./README.md#encrypted-transports-and-dns64).
- **DHCP, Router Advertisement and passive device presence need Linux.** The macOS and Windows
  builds run DNS, the REST API and the console.
- **The self-update check needs a feed URL.** Ninlil publishes no feed. See
  [Self-update](./README.md#self-update).
- **DNS64 synthesis runs on the recursive path only.** Forwarded and authoritative answers are
  relayed unchanged.
- **A cluster pushes a change once, when you save it.** A node that joins or returns later converges
  at the next save. DHCP data and webhook subscriptions are not copied. See [Clustering, events and
  webhooks](./README.md#clustering-events-and-webhooks).
- **Webhooks deliver seven event types.**
- **MongoDB cannot be the source or the target of a storage switch.** A standalone MongoDB server
  saves several documents non-atomically; a replica set is atomic. See
  [Storage](./README.md#storage).
- **MySQL cannot roll back a failed migration.** Restore the migration snapshot. See [Upgrades and
  migration snapshots](./README.md#upgrades-and-migration-snapshots).
- **DHCPv6 option codes above 255 cannot be stored.** The DHCPv4 Client FQDN option (RFC 4702,
  option 81) and option 119 are not in the option catalog.
- **The query log records an RFC 2136 update as an SOA query for the zone.**
- **A live-feed connection can outlast a disabled account by up to 15 minutes.** Ninlil checks it
  when it opens and closes it when its token expires.

### Accepted risks

- **The Data Protection key ring and the JWT signing key are stored unencrypted at rest, on every
  platform.** Both live in the data directory. On Linux and macOS Ninlil sets the folder to mode
  0700 and the files to 0600; on Windows you restrict the folder with `icacls`. Anyone who can read
  the whole data directory can decrypt the stored secrets (TSIG secrets, DNSSEC private keys, cloud
  credentials, TOTP keys, the ACME account key) and can mint tokens for any enabled account. Keep
  the data directory readable by the administrators of the host only, and protect backups with a
  passphrase. See [Security](./README.md#security).
- **Sign-in does not lock accounts.** The per-address sign-in rate limit bounds password guessing
  (an IPv6 client counts by its /64 network). The API's lockout settings have no effect.
- **The two-factor attempt limit and pending sign-ins are kept per node, in memory.** A restart or a
  second node starts the counts again.
- **Cluster-synced secrets travel over the signed and encrypted cluster channel.** Each receiving
  node encrypts them with its key ring.
- **Webhook targets, forwarders, filter-list subscriptions and the self-update feed URL can name any
  host the server can reach**, including a loopback address. Grant the matching `*:write`
  permissions to people you trust.

### Security notes

- **MySqlConnector 2.6.2.** MySqlConnector before 2.6.1 sends the account password before it checks
  the server certificate in zero-configuration TLS mode (advisory GHSA-473q-m89c-ghf8). The MySQL
  storage engine references MySqlConnector 2.6.2.
- **DNSCrypt.** A certificate is valid for 28 hours and Ninlil issues a replacement every 24 hours.
  Ninlil drops an expired certificate with its secret key and does not answer a query encrypted for
  it. A replacement certificate never gets a client magic that starts with seven zero bytes or that another
  valid certificate uses. A DNSCrypt TCP connection carries one query.
- **DNS-over-QUIC and `libmsquic`.** The abort described under Known limitations comes from a check
  inside `libmsquic`. The same check runs on the server side, so a DNS-over-QUIC client that
  advertises a maximum UDP payload size of 1200 over IPv4 may be able to stop a listener that runs
  on a `libmsquic` older than 2.4.20. No client here could set that parameter, so this is not
  confirmed. Use 2.4.20 or later.
- **Vulnerability reports.** Report a vulnerability privately, as [`SECURITY.md`](./SECURITY.md)
  describes. A fix is listed here, and the reporter is named unless they ask not to be.
