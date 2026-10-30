# Environment Variable Reference

This file lists every `NINLIL_*` environment variable Ninlil reads: one table row per variable, with
what it controls and its default. It is the only list; the [README](./README.md) links here from
each guide instead of repeating names. Ninlil recognizes no other environment-variable name for its
own settings. ASP.NET Core's default double-underscore form (`Section__Key`) is disabled and does
nothing here.

## How to read this file

1. **Find the row.** Use the table of contents below, or search the file for part of the variable name.
2. **Read the Default column.** A value in code font is the value the setting has when nothing sets it.
   `*(empty)*` is an empty string. `*(none)*` is no value at all. A note in italics says what Ninlil
   does instead when the setting is unset.
3. **Check for a startup rule.** A few defaults are changed or filled in while Ninlil starts. Those
   rows say so (for example the Identity password rules, the cluster node id and the JWT signing key).
4. **Locate a `data/` path.** A path shown as `data/...` is inside the `data` folder next to the
   Ninlil program files (the application's base directory), not inside the current working
   directory.
5. **Set the variable.** Put it in the environment of the Ninlil process. An environment variable
   overrides `appsettings.json`, and a command-line argument overrides an environment variable.
   Every setting in the tables outside "Framework-level variables" also has a matching key in
   `appsettings.json` (nested JSON that mirrors the `:`-separated configuration path). Most also
   have an admin console page and a REST endpoint; the README says which surface to prefer for
   day-to-day work.

A `TimeSpan` default uses .NET's `[d.]hh:mm:ss` format: `1.00:00:00` is one day and `00:05:00` is
five minutes.

## How the names are built

A configuration path is a run of `:`-separated segments written in PascalCase, for example
`DnsServer:Web:HttpPort`. The variable name is `NINLIL_` followed by every PascalCase word of every
segment, uppercased and joined with `_`: `DnsServer:Web:HttpPort` becomes
`NINLIL_DNS_SERVER_WEB_HTTP_PORT`. `IPv4` and `IPv6` each count as one word (`IPV4`, `IPV6`).

A list-valued setting has no variable, because the configuration binder stores a list as an
open-ended run of index-suffixed keys. Allowed DNSSEC algorithms, ACME contact emails, a reverse
proxy's known-proxy list and the recursive resolver's Insecure domains are examples. Each is
reachable through `appsettings.json`, the REST API and the admin console.

## What keeps this file correct

`EnvironmentVariableDocumentationTests` (in `Ninlil.Web.Tests`) fails when a row is missing, when a
row names a variable that does not exist, when a variable has two rows, or when a number in the
totals sentence below is wrong. It builds the expected list from the same mapping code that
`Program.cs` runs at startup (`OptionsEnvironmentVariableMap.BuildMappings`). The test does not
check the Default column or the descriptions. When you change an option's default or behavior,
update its row by hand, then run:

```bash
dotnet test src/dotnet/tests/Ninlil.Web.Tests --filter "FullyQualifiedName~EnvironmentVariableDocumentationTests|FullyQualifiedName~OptionsEnvironmentVariableRegistryTests"
```

When you add an option, add its row to the section for its class and take the totals from the
mapping, not from hand arithmetic.

<!-- EnvironmentVariableDocumentationTests matches the "Total:" sentence with a regular expression
(TotalsPattern). Keep its wording and change only the numbers. -->
Total: **332 individual settings** across 58 bound configuration classes, plus **3 standalone**
variables read directly by configuration key (the two bootstrap administrator credentials and
`NINLIL_ALLOWED_HOSTS`), plus **7 framework-level** variables that redirect onto .NET's unprefixed
equivalents, giving **342 environment variables in total**. The `NINLIL_DOTNET_*` runtime settings
in the [.NET runtime settings](#net-runtime-settings) section are recognized names, not part of that
total.

---

## Table of contents

- [How to read this file](#how-to-read-this-file)
- [How the names are built](#how-the-names-are-built)
- [What keeps this file correct](#what-keeps-this-file-correct)
- [Storage engine](#storage-engine)
- [Identity, authentication & SSO](#identity-authentication--sso)
- [DNS transports](#dns-transports)
- [Authoritative DNS engine](#authoritative-dns-engine)
- [Query pipeline & cross-cutting DNS behavior](#query-pipeline--cross-cutting-dns-behavior)
- [Recursive resolver](#recursive-resolver)
- [DNSSEC, ACME & CAA](#dnssec-acme--caa)
- [TSIG](#tsig)
- [Event broker (message queue) transports](#event-broker-message-queue-transports)
- [Filtering, rewrites, rate limiting & query log](#filtering-rewrites-rate-limiting--query-log)
- [Forwarding](#forwarding)
- [Client presence & MAC vendor lookup](#client-presence--mac-vendor-lookup)
- [Webhooks, backup, self-update & clustering](#webhooks-backup-self-update--clustering)
- [DHCP, DHCPv6, Router Advertisement & TFTP](#dhcp-dhcpv6-router-advertisement--tftp)
- [Web host (Kestrel, reverse proxy, branding)](#web-host-kestrel-reverse-proxy-branding)
- [Framework-level variables](#framework-level-variables)
  - [.NET runtime settings](#net-runtime-settings)
- [Bootstrap administrator credentials](#bootstrap-administrator-credentials)
- [Host filtering](#host-filtering)

---

## Storage engine

Which database backend the whole application persists every DNS/DHCP/identity entity into.

How to use these settings is explained step by step in the README:
[Switching the storage engine](./README.md#switching-the-storage-engine),
[Upgrades and migration snapshots](./README.md#upgrades-and-migration-snapshots) (the
`..._MIGRATION_SAFETY_*` variables) and
[Read replicas and database clusters](./README.md#read-replicas-and-database-clusters) (the
`..._READ_REPLICA_*` variables and the per-engine variables for PostgreSQL, MySQL, SQL Server and
MongoDB).

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_STORAGE_ENGINE` | Which storage backend is active: `Sqlite`, `PostgreSql`, `MySql`, `SqlServer`, or `MongoDb`. Relational engines get their EF Core migrations applied automatically on startup; MongoDB is schemaless. | `Sqlite` |
| `NINLIL_STORAGE_CONNECTION_STRING` | The connection string for the selected engine. Ignored for SQLite unless you want to override the default local database file location. | *(none — SQLite uses the file `data/ninlil-dns.sqlite3`)* |
| `NINLIL_STORAGE_MIGRATION_SAFETY_TAKE_SNAPSHOT` | Whether a snapshot of the database is taken before pending migrations are applied to a database that already holds data. SQLite snapshots are files in a `migration-snapshots` folder next to the database file. Set `false` to migrate without one. | `true` |
| `NINLIL_STORAGE_MIGRATION_SAFETY_SNAPSHOTS_TO_KEEP` | How many finished snapshots are kept after a successful migration; the oldest are deleted first. Values below 1 count as 1. | `3` |
| `NINLIL_STORAGE_MIGRATION_SAFETY_RETRY_FAILED_MIGRATION` | Whether a start may try a migration again after an earlier attempt failed and may have left the database partly migrated. By default that start is refused until the snapshot is restored. | `false` |
| `NINLIL_STORAGE_MIGRATION_SAFETY_RESTORE_SNAPSHOT` | The id of a snapshot to restore at the next start, before anything else touches the database. Remove it after the restore; a snapshot that was already restored is not restored again. | *(empty)* |
| `NINLIL_STORAGE_READ_REPLICA_CONNECTION_STRING` | The read-only connection string of a relational engine. PostgreSQL and MySQL may list several hosts in it; for SQL Server it names the availability group listener. Refused on SQLite and MongoDB (MongoDB uses `NINLIL_STORAGE_MONGODB_READ_PREFERENCE`). It contains a password, so set it as a secret. | *(empty — no read replica)* |
| `NINLIL_STORAGE_READ_REPLICA_STICKY_AFTER_WRITE_SECONDS` | How many seconds a user keeps reading from the primary after saving something, so the admin console shows what was just written. Must be at least 1. | `10` |
| `NINLIL_STORAGE_READ_REPLICA_MAX_LAG_SECONDS` | How many seconds a replica may be behind and still receive reads. Must be below the sticky window above and below 60 when a replica is configured (60 s is the shortest refresh interval of the in-memory caches, which reload from a replica; a larger lag could bring back data older than the cache holds). Ninlil refuses to start otherwise. | `5` |
| `NINLIL_STORAGE_READ_REPLICA_LAG_CHECK_INTERVAL_SECONDS` | How often, in seconds, the replica's lag and schema version are measured. Must be at least 1. | `5` |
| `NINLIL_STORAGE_POSTGRESQL_TARGET_SESSION_ATTRIBUTES` | PostgreSQL only. The Npgsql `Target Session Attributes` of the primary connection: `ReadWrite` or `Primary`. A primary connection string with several hosts gets `ReadWrite` when this is empty. Refused when the connection string says something different. | *(empty)* |
| `NINLIL_STORAGE_POSTGRESQL_REPLICA_TARGET_SESSION_ATTRIBUTES` | PostgreSQL only. The `Target Session Attributes` of the read connection: `PreferStandby`, `Standby`, `ReadOnly`, `PreferPrimary` or `Any`. `ReadWrite` and `Primary` are refused. | *(empty — `PreferStandby` when the read string lists several hosts)* |
| `NINLIL_STORAGE_POSTGRESQL_LOAD_BALANCE_HOSTS` | PostgreSQL only. The Npgsql `Load Balance Hosts` value. When empty, a read connection with several hosts balances and the primary connection does not. | *(empty)* |
| `NINLIL_STORAGE_POSTGRESQL_HOST_RECHECK_SECONDS` | PostgreSQL only. The Npgsql `Host Recheck Seconds` value: how long a host's state is trusted before it is checked again. | *(empty — driver default, 10)* |
| `NINLIL_STORAGE_MYSQL_LOAD_BALANCE` | MySQL only. The `LoadBalance` mode of the primary connection. Only `FailOver` is accepted, and only an external failover that leaves one host writable makes several hosts safe. | *(empty)* |
| `NINLIL_STORAGE_MYSQL_REPLICA_LOAD_BALANCE` | MySQL only. The `LoadBalance` mode of the read connection: `RoundRobin`, `Random`, `LeastConnections` or `FailOver`. | *(empty — `RoundRobin` for several hosts)* |
| `NINLIL_STORAGE_SQLSERVER_MULTI_SUBNET_FAILOVER` | SQL Server only. Sets `MultiSubnetFailover` on both connections, for a listener that spans several subnets. The read connection always gets `ApplicationIntent=ReadOnly`. | `false` |
| `NINLIL_STORAGE_MONGODB_REPLICA_SET` | MongoDB only. The replica set name (`replicaSet`). Refused when the connection string names another one. | *(empty)* |
| `NINLIL_STORAGE_MONGODB_READ_PREFERENCE` | MongoDB only. The read preference for work that tolerates replica lag: `Primary`, `PrimaryPreferred`, `Secondary`, `SecondaryPreferred` or `Nearest`. Work that needs the primary always uses `Primary`. | *(empty — `Primary`)* |
| `NINLIL_STORAGE_MONGODB_MAX_STALENESS_SECONDS` | MongoDB only. The driver's `maxStalenessSeconds` for that read preference. `0` leaves it unset; otherwise at least 90. | `0` |
| `NINLIL_STORAGE_MONGODB_WRITE_CONCERN` | MongoDB only. `Majority`, `Acknowledged` (w:1) or `W2` to `W9`. When empty, `Majority` is used on a replica set and the connection string's value otherwise. | *(empty)* |
| `NINLIL_STORAGE_MONGODB_JOURNAL` | MongoDB only. The write concern's journal flag (`j`). When empty, `true` is used with a write concern that Ninlil applies. | *(empty)* |

## Identity, authentication & SSO

User accounts, password/lockout policy, JWT bearer tokens, outbound email (used for 2FA codes and
password-reset links), admin-login brute-force protection, and OpenID Connect single sign-on.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_IDENTITY_USER_REQUIRE_UNIQUE_EMAIL` | Whether each account must have a unique email address. `true` refuses an account whose address another account has, and an account with no address. Ninlil sets this to `true` at startup before it reads your configuration. | `true` |
| `NINLIL_IDENTITY_USER_ALLOWED_USER_NAME_CHARACTERS` | The character set a sign-in username may be composed from. | `abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+` |
| `NINLIL_IDENTITY_PASSWORD_REQUIRED_LENGTH` | Minimum password length. Ninlil sets this to 12 at startup before it reads your configuration. | `12` |
| `NINLIL_IDENTITY_PASSWORD_REQUIRED_UNIQUE_CHARS` | Minimum count of distinct characters a password must contain. | `1` |
| `NINLIL_IDENTITY_PASSWORD_REQUIRE_DIGIT` | Whether a password must contain a digit. | `true` |
| `NINLIL_IDENTITY_PASSWORD_REQUIRE_LOWERCASE` | Whether a password must contain a lowercase letter. | `true` |
| `NINLIL_IDENTITY_PASSWORD_REQUIRE_UPPERCASE` | Whether a password must contain an uppercase letter. | `true` |
| `NINLIL_IDENTITY_PASSWORD_REQUIRE_NON_ALPHANUMERIC` | Whether a password must contain a non-alphanumeric character. | `true` |
| `NINLIL_IDENTITY_LOCKOUT_ALLOWED_FOR_NEW_USERS` | No effect. Sign-in does not count failed passwords and nothing else starts a lockout, so this flag is never acted on. The admin console does not show it; the identity settings API still accepts it. | `true` |
| `NINLIL_IDENTITY_LOCKOUT_MAX_FAILED_ACCESS_ATTEMPTS` | No effect. Sign-in does not count failed passwords, so no account reaches this limit. The admin console does not show it; the identity settings API still accepts it. | `5` |
| `NINLIL_IDENTITY_LOCKOUT_DEFAULT_LOCKOUT_TIME_SPAN` | No effect. Sign-in does not count failed passwords, so no account is locked out for this time. The admin console does not show it; the identity settings API still accepts it. To slow password guessing, use the admin login rate limit below. Ninlil sets this to 15 minutes at startup before it reads your configuration. | `00:15:00` |
| `NINLIL_IDENTITY_SIGN_IN_REQUIRE_CONFIRMED_ACCOUNT` | Whether sign-in is blocked until the account is confirmed. | `false` |
| `NINLIL_IDENTITY_SIGN_IN_REQUIRE_CONFIRMED_EMAIL` | Whether sign-in is blocked until the account's email is confirmed. | `false` |
| `NINLIL_IDENTITY_SIGN_IN_REQUIRE_CONFIRMED_PHONE_NUMBER` | Whether sign-in is blocked until the account's phone number is confirmed. | `false` |
| `NINLIL_IDENTITY_STORES_MAX_LENGTH_FOR_KEYS` | Maximum key length ASP.NET Core Identity's own store abstraction enforces (`0` = provider default). | `0` |
| `NINLIL_IDENTITY_STORES_PROTECT_PERSONAL_DATA` | Whether ASP.NET Core Identity's own personal-data protection wrapper is enabled. | `false` |
| `NINLIL_IDENTITY_TOKENS_AUTHENTICATOR_ISSUER` | The issuer name embedded in a TOTP authenticator QR code/URI. | `Microsoft.AspNetCore.Identity.UI` |
| `NINLIL_IDENTITY_TOKENS_AUTHENTICATOR_TOKEN_PROVIDER` | The named token provider used for TOTP authenticator codes. | `Authenticator` |
| `NINLIL_IDENTITY_TOKENS_EMAIL_CONFIRMATION_TOKEN_PROVIDER` | The named token provider used for email confirmation links. | `Default` |
| `NINLIL_IDENTITY_TOKENS_PASSWORD_RESET_TOKEN_PROVIDER` | The named token provider used for password-reset links. | `Default` |
| `NINLIL_IDENTITY_TOKENS_CHANGE_EMAIL_TOKEN_PROVIDER` | The named token provider used for change-of-email confirmation. | `Default` |
| `NINLIL_IDENTITY_TOKENS_CHANGE_PHONE_NUMBER_TOKEN_PROVIDER` | The named token provider used for change-of-phone-number confirmation. | `Phone` |
| `NINLIL_IDENTITY_CLAIMS_IDENTITY_USER_ID_CLAIM_TYPE` | The claim type name carrying the user's ID in an issued `ClaimsPrincipal`. | `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier` |
| `NINLIL_IDENTITY_CLAIMS_IDENTITY_USER_NAME_CLAIM_TYPE` | The claim type name carrying the username. | `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name` |
| `NINLIL_IDENTITY_CLAIMS_IDENTITY_EMAIL_CLAIM_TYPE` | The claim type name carrying the email address. | `http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress` |
| `NINLIL_IDENTITY_CLAIMS_IDENTITY_ROLE_CLAIM_TYPE` | The claim type name carrying role membership. | `http://schemas.microsoft.com/ws/2008/06/identity/claims/role` |
| `NINLIL_IDENTITY_CLAIMS_IDENTITY_SECURITY_STAMP_CLAIM_TYPE` | The claim type name carrying the account's security stamp (invalidated on password change). | `AspNet.Identity.SecurityStamp` |
| `NINLIL_JWT_ISSUER` | The `iss` claim value every issued access/refresh token carries. | `Ninlil.Dns` |
| `NINLIL_JWT_AUDIENCE` | The `aud` claim value every issued token carries and every request is validated against. | `Ninlil.Web` |
| `NINLIL_JWT_SIGNING_KEY` | The secret that signs and verifies every JWT access token. When it is unset, Ninlil generates a random key at the first start and keeps it in `data/jwt-signing-key.txt` (readable by its owner only), so tokens stay valid across restarts. Set it yourself when several nodes must accept each other's tokens, or when a reinstall or a restore on another host must keep sessions valid: Ninlil does not copy the key between cluster nodes, and a backup does not contain it. Use a random value of at least 32 bytes. No REST endpoint or console page changes it. | *(none — generated and kept in `data/jwt-signing-key.txt`)* |
| `NINLIL_JWT_ACCESS_TOKEN_LIFETIME` | How long an issued access token remains valid. | `00:15:00` |
| `NINLIL_JWT_REFRESH_TOKEN_LIFETIME` | How long an issued refresh token remains valid — for API clients and for browser sign-ins with "Remember me" checked. Renewed on every refresh. | `30.00:00:00` |
| `NINLIL_JWT_BROWSER_SESSION_REFRESH_TOKEN_LIFETIME` | How long a browser sign-in *without* "Remember me" stays renewable without activity. Every refresh restarts the window. | `12:00:00` |
| `NINLIL_JWT_CLOCK_SKEW` | Allowed clock drift tolerance when validating a token's expiry. | `00:00:30` |
| `NINLIL_EMAIL_SMTP_HOST` | The outbound SMTP host used for 2FA codes/password-reset emails. | *(none)* |
| `NINLIL_EMAIL_SMTP_PORT` | The SMTP port. | `587` |
| `NINLIL_EMAIL_USERNAME` | The SMTP authentication username. | *(none)* |
| `NINLIL_EMAIL_PASSWORD` | The SMTP authentication password. Kept in plaintext by nature; prefer the admin console, which stores it encrypted. When set, it overrides a password saved in the admin console. | *(none)* |
| `NINLIL_EMAIL_PASSWORD_ENCRYPTED` | The SMTP password encrypted with this installation's Data Protection key ring. Written by the admin console; there is no need to set it by hand. | *(none)* |
| `NINLIL_EMAIL_USE_TLS` | Whether the SMTP connection uses TLS. | `true` |
| `NINLIL_EMAIL_FROM_ADDRESS` | The `From:` address on outbound mail. | *(none)* |
| `NINLIL_EMAIL_FROM_DISPLAY_NAME` | The `From:` display name on outbound mail. | `Ninlil` |
| `NINLIL_EMAIL_PUBLIC_BASE_URL` | The address users open the admin console at (for example `https://dns.example.com`); password-reset links point here. Links are never built from the request's Host header. While unset, no reset link is sent: an administrator's e-mailed reset fails with an error and Forgot Password only logs a warning. | *(none)* |
| `NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_ENABLE` | Whether admin-console sign-in attempts are rate-limited (brute-force protection, separate from the DNS-query-level rate limiter below). | `true` |
| `NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_PERMIT_LIMIT` | How many sign-in attempts are permitted within the window. | `5` |
| `NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_WINDOW` | The sliding window the permit limit above applies over. | `00:05:00` |
| `NINLIL_IDENTITY_SSO_ENABLE` | Whether OpenID Connect single sign-on is enabled (needs a restart to take effect — the auth scheme is registered once, before DI is built). | `false` |
| `NINLIL_IDENTITY_SSO_AUTHORITY` | The OIDC identity provider's authority URL. | *(empty)* |
| `NINLIL_IDENTITY_SSO_CLIENT_ID` | The OIDC client ID registered with the identity provider. | *(empty)* |
| `NINLIL_IDENTITY_SSO_CLIENT_SECRET` | The OIDC client secret in plaintext, for container and secret-store deployments. When set, it overrides a secret saved in the admin console, which then shows that it is overridden. The admin console never writes it. A value typed into the configuration file is encrypted at startup; a value from the environment is never written to disk. | *(empty)* |
| `NINLIL_IDENTITY_SSO_CLIENT_SECRET_ENCRYPTED` | The OIDC client secret encrypted with this installation's Data Protection key ring. Written by the admin console; do not set it by hand, use `NINLIL_IDENTITY_SSO_CLIENT_SECRET` instead. A plaintext value here cannot be decrypted: SSO sign-in then fails and the server logs an Error, but the rest of the server keeps working. | *(empty)* |
| `NINLIL_IDENTITY_SSO_DISPLAY_NAME` | The label shown on the "sign in with SSO" button. | `Single Sign-On` |

## DNS transports

Each transport beyond plain UDP/TCP is off by default. `Certificate*` settings appear once per
TLS-based transport (DNS-over-TLS, DNS-over-HTTPS, DNS-over-QUIC), each with its own independent
listener configuration but all sharing the same underlying self-signed/ACME certificate file by
default.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT` | The plain UDP/TCP DNS port (RFC 1035). | `53` |
| `NINLIL_DNS_SERVER_PROTOCOL_PLAIN_LISTEN_ADDRESSES` | Comma-separated IP addresses this transport listens on (for example `192.168.1.1, ::1`); empty listens on every address. An address this host doesn't have is logged and skipped while the others keep serving. | *(empty — every address)* |
| `NINLIL_DNS_SERVER_PROTOCOL_PLAIN_MAX_CONCURRENT_TCP_CONNECTIONS` | Max simultaneous plain-DNS TCP connections. | `512` |
| `NINLIL_DNS_SERVER_PROTOCOL_PLAIN_TCP_IDLE_TIMEOUT` | Idle timeout before an inactive plain-DNS TCP connection is closed. | `00:00:30` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_ENABLE` | Whether DNS-over-TLS (RFC 7858) is enabled. | `false` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_PORT` | The DoT listen port. | `853` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_LISTEN_ADDRESSES` | Comma-separated IP addresses this transport listens on (for example `192.168.1.1, ::1`); empty listens on every address. An address this host doesn't have is logged and skipped while the others keep serving. | *(empty — every address)* |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_MAX_CONCURRENT_CONNECTIONS` | Max simultaneous DoT connections. | `512` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_IDLE_TIMEOUT` | Idle timeout before an inactive DoT connection is closed. | `00:00:30` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_CERTIFICATE_PFX_PATH` | Path to a custom PFX certificate for DoT (leave unset to use the shared auto-generated/ACME certificate). | *(empty)* |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_CERTIFICATE_PFX_PASSWORD` | Password for the custom PFX file above. | *(empty)* |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_CERTIFICATE_SELF_SIGNED_CERTIFICATE_STATE_PATH` | Where the auto-generated self-signed certificate is persisted. | `data/protocol-tls-certificate.pfx` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_CERTIFICATE_ALLOWED_PROTOCOLS` | Explicit TLS protocol version override (`None` = OS/runtime default negotiation). | `None` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_CERTIFICATE_CERTIFICATE_RELOAD_POLL_INTERVAL` | How often the certificate file's mtime is polled for an ACME-renewed replacement. | `00:05:00` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_PROXY_PROTOCOL_ENABLE` | Whether this DoT listener accepts a PROXY protocol (v1/v2) preamble from a trusted upstream load balancer. | `false` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_ENABLE` | Whether DNS-over-HTTPS (RFC 8484) is enabled. | `false` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_PORT` | The DoH listen port (served from the same combined Kestrel host). | `443` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_PATH` | The URL path DoH queries are served from. | `/dns-query` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_MAX_MESSAGE_SIZE_BYTES` | Maximum accepted DoH request/response message size. | `65535` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_CERTIFICATE_PFX_PATH` | Custom PFX certificate path for DoH (if served on a dedicated listener rather than the main Kestrel one). | *(empty)* |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_CERTIFICATE_PFX_PASSWORD` | Password for the custom PFX file above. | *(empty)* |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_CERTIFICATE_SELF_SIGNED_CERTIFICATE_STATE_PATH` | Where the auto-generated self-signed certificate is persisted. | `data/protocol-tls-certificate.pfx` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_CERTIFICATE_ALLOWED_PROTOCOLS` | Explicit TLS protocol version override. | `None` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_HTTPS_CERTIFICATE_CERTIFICATE_RELOAD_POLL_INTERVAL` | Certificate reload poll interval. | `00:05:00` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_ENABLE` | Whether DNS-over-QUIC (RFC 9250) is enabled. Needs `libmsquic` — included in the Docker image; install it yourself on a bare-metal host. | `false` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_PORT` | The DoQ listen port. | `853` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_LISTEN_ADDRESSES` | Comma-separated IP addresses this transport listens on (for example `192.168.1.1, ::1`); empty listens on every address. An address this host doesn't have is logged and skipped while the others keep serving. | *(empty — every address)* |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_IDLE_TIMEOUT` | QUIC connection idle timeout, also the deadline for a stream's query to arrive and for each response write. | `00:00:30` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_MAX_CONCURRENT_BIDIRECTIONAL_STREAMS` | Max concurrent bidirectional QUIC streams per connection. | `100` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_MAX_CONCURRENT_CONNECTIONS` | Max concurrent QUIC connections; one over the limit is closed with `DOQ_EXCESSIVE_LOAD`. | `512` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_CERTIFICATE_PFX_PATH` | Custom PFX certificate path for DoQ. | *(empty)* |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_CERTIFICATE_PFX_PASSWORD` | Password for the custom PFX file above. | *(empty)* |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_CERTIFICATE_SELF_SIGNED_CERTIFICATE_STATE_PATH` | Where the auto-generated self-signed certificate is persisted. | `data/protocol-tls-certificate.pfx` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_CERTIFICATE_ALLOWED_PROTOCOLS` | Explicit TLS protocol version override. | `None` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_QUIC_CERTIFICATE_CERTIFICATE_RELOAD_POLL_INTERVAL` | Certificate reload poll interval. | `00:05:00` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_ENABLE` | Whether DNSCrypt v2 is enabled. | `false` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_PORT` | The DNSCrypt UDP/TCP port. | `5443` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_LISTEN_ADDRESSES` | Comma-separated IP addresses this transport listens on (for example `192.168.1.1, ::1`); empty listens on every address. An address this host doesn't have is logged and skipped while the others keep serving. | *(empty — every address)* |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_PROVIDER_NAME` | The DNSCrypt provider name clients use to build an `sdns://` stamp against this server. | *(empty — must be set to actually use DNSCrypt)* |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_MAX_CONCURRENT_TCP_CONNECTIONS` | Max simultaneous DNSCrypt TCP connections. | `512` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_TCP_IDLE_TIMEOUT` | Idle timeout before an inactive DNSCrypt TCP connection is closed. | `00:00:30` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_CERTIFICATE_VALIDITY_DURATION` | How long a generated DNSCrypt provider certificate stays valid. | `1.04:00:00` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_CERTIFICATE_ROTATION_INTERVAL` | How often the DNSCrypt certificate is rotated. | `1.00:00:00` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_CERTIFICATE_CLOCK_SKEW_TOLERANCE` | Clock skew tolerance when validating certificate timestamps. | `00:05:00` |
| `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_CERTIFICATE_STATE_FILE_PATH` | Where DNSCrypt's own provider key/certificate state is persisted (filesystem-permissions-protected, not `IDataProtector`-encrypted). | `data/dnscrypt-certificates.json` |

## Authoritative DNS engine

Zone hosting, transfers (AXFR/IXFR), NOTIFY, dynamic update support, automated DNSSEC re-signing
and key rollover, RFC 9432 Catalog Zone sync, and CAA/PTR reconciliation timing.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_AUTHORITATIVE_HOSTNAME_STRING` | This server's advertised hostname, used in generated SOA/NS content and in CHAOS `hostname.bind` answers. | *(the host's machine name)* |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_VERSION_STRING` | The version string returned by a CHAOS `version.bind`/`version.server` query. | `Ninlil` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ENABLE_CHAOS_VERSION_QUERIES` | Whether CHAOS-class version queries are answered at all. | `false` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_RECORDS_PER_MESSAGE` | How many records are packed into each AXFR/IXFR response message. | `100` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_CONNECT_TIMEOUT` | Connect timeout when this server pulls a zone transfer as a secondary. | `00:00:05` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_READ_TIMEOUT` | Read timeout during an in-progress transfer pull. | `00:00:10` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_SOA_QUERY_TIMEOUT` | Timeout for the preliminary SOA-serial check before a transfer is attempted. | `00:00:03` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_PRIMARY_PORT` | Default port used to reach a primary over plain TCP. | `53` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_PRIMARY_TLS_PORT` | Default port used to reach a primary over TLS-wrapped transfer. | `853` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_PRIMARY_QUIC_PORT` | Default port used to reach a primary over QUIC-wrapped transfer. | `853` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_VALIDATE_SERVER_CERTIFICATE` | Whether a TLS/QUIC transfer validates the primary's own certificate chain. | `true` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_NOTIFY_MAX_ATTEMPTS` | Max RFC 1996 NOTIFY delivery attempts per secondary. | `5` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_NOTIFY_RETRY_DELAY` | Delay between NOTIFY retry attempts. | `00:00:03` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_NOTIFY_RESPONSE_TIMEOUT` | How long to wait for a secondary's NOTIFY acknowledgment. | `00:00:03` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_NOTIFY_SECONDARY_PORT` | Default port NOTIFY is sent to on each secondary. | `53` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_SECONDARY_ZONE_REFRESH_POLL_INTERVAL` | How often this server checks whether a secondary zone's SOA serial needs refreshing. | `00:00:15` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_SECONDARY_ZONE_REFRESH_DEFAULT_RETRY_INTERVAL` | Wait before the next check of a zone with no local SOA yet and of a stub zone. Other zones retry after their SOA Retry value. | `00:00:30` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_SIGNING_POLL_INTERVAL` | How often the DNSSEC re-signing background pass checks every signed zone. | `06:00:00` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_SIGNING_SIGNATURE_VALIDITY_PERIOD` | How long a freshly generated RRSIG stays valid. | `30.00:00:00` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_SIGNING_REFRESH_BEFORE_EXPIRATION` | How long before RRSIG expiration a zone is proactively re-signed. | `5.00:00:00` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_SIGNING_INCEPTION_OFFSET` | How far in the past a freshly generated RRSIG's inception timestamp is backdated (clock-skew tolerance). | `01:00:00` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_DNSSEC_KEY_ROLLOVER_POLL_INTERVAL` | How often the RFC 6781 key-rollover walker checks every zone's own keys. | `00:15:00` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_DNSSEC_KEY_ROLLOVER_PROPAGATION_DELAY` | Assumed DNS propagation delay factored into rollover timing. | `01:00:00` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_DNSSEC_KEY_ROLLOVER_EXTERNAL_PARENT_DS_TTL` | Assumed DS-record TTL used to time the final withdrawal wait when a KSK's parent zone is external (this server cannot query a registrar for the DS TTL). | `1.00:00:00` |
| `NINLIL_CATALOG_SYNC_ADDRESS_RECONCILE_INTERVAL` | How often RFC 9432 Catalog Zone membership is reconciled against actually-hosted zones. | `00:01:00` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_PTR_SYNC_DEFAULT_ENABLED` | The global default for whether a new zone auto-manages PTR records (a zone can override this). | `false` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_PTR_SYNC_DEFAULT_IPV4_REVERSE_ZONE_PREFIX_BITS` | Default IPv4 reverse-zone grouping granularity (a zone can override, `25`-`30` enabling RFC 2317 classless delegation). | `24` |
| `NINLIL_DNS_SERVER_AUTHORITATIVE_PTR_SYNC_DEFAULT_IPV6_REVERSE_ZONE_PREFIX_BITS` | Default IPv6 reverse-zone grouping granularity. | `64` |

## Query pipeline & cross-cutting DNS behavior

Settings shared by every resolver (authoritative, forwarder, recursive) inside
`DnsQueryProcessingPipeline`: worker/channel sizing, anti-amplification limits, the event bus, DNS
Cookies, source-port pooling, EDNS Client Subnet, and DNS64.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_PIPELINE_WORKER_COUNT` | How many tasks take queries off the queue. | *(the number of processors)* |
| `NINLIL_DNS_SERVER_PIPELINE_MAX_CONCURRENT_QUERIES` | How many queries are answered at once. When all slots are busy, queries wait in the queue (and transports wait to enqueue). | `1000` |
| `NINLIL_DNS_SERVER_PIPELINE_CHANNEL_CAPACITY` | The bounded channel capacity queries wait in before a worker picks them up. | `4096` |
| `NINLIL_DNS_SERVER_PIPELINE_MAX_UDP_PAYLOAD_SIZE_OCTETS` | Maximum UDP response size before truncation/EDNS fragmentation logic applies. | `1232` |
| `NINLIL_DNS_SERVER_PIPELINE_MIN_UDP_AMPLIFICATION_FLOOR_SIZE_OCTETS` | The minimum request size below which the amplification-factor check is skipped entirely. | `512` |
| `NINLIL_DNS_SERVER_PIPELINE_MAX_UDP_AMPLIFICATION_FACTOR` | The maximum allowed response-to-request size ratio over UDP, an anti-DNS-amplification-attack guard. | `20` |
| `NINLIL_DNS_SERVER_EVENTS_CAPACITY` | How many per-query events (`DnsQueryReceived`, `DnsQueryAnswered`) may wait for delivery or for the broker publisher; when full, the oldest are dropped and one warning per minute reports how many. Other events are never dropped. | `10000` |
| `NINLIL_DNS_SERVER_EVENTS_TRANSPORT` | Which event transport is active: `InMemory`, `RabbitMq`, or `ActiveMq` (needs a restart to change — selected before DI is built). | `InMemory` |
| `NINLIL_DNS_SERVER_COOKIES_ENABLE` | Whether RFC 7873 DNS Cookies are supported. | `true` |
| `NINLIL_DNS_SERVER_COOKIES_REQUIRE` | Whether a valid cookie is required to answer a query (stricter anti-spoofing posture). | `false` |
| `NINLIL_DNS_SERVER_COOKIES_SECRET_ROTATION_INTERVAL` | How often the server-side cookie secret is rotated. | `1.00:00:00` |
| `NINLIL_DNS_SERVER_OUTBOUND_QUERY_ENABLE_SOURCE_PORT_POOL` | Whether outbound (recursive/forwarder) queries draw from a pre-bound pool of source ports rather than binding a fresh ephemeral port per query. Off by default so every query gets a fresh random source port; turn on only when per-query binds cost too much. | `false` |
| `NINLIL_DNS_SERVER_OUTBOUND_QUERY_POOL_SIZE_PER_ADDRESS_FAMILY` | How many source ports are pre-bound per IP address family. | `128` |
| `NINLIL_DNS_SERVER_CLIENT_SUBNET_ENABLE` | Whether EDNS Client Subnet (ECS) is attached to outbound recursive queries. | `false` |
| `NINLIL_DNS_SERVER_CLIENT_SUBNET_OVERRIDE_ADDRESS` | A fixed address to report via ECS instead of the querying client's address. | *(none)* |
| `NINLIL_DNS_SERVER_CLIENT_SUBNET_SOURCE_PREFIX_LENGTH_IPV4` | IPv4 prefix length truncated into the ECS option; also caps an ECS option a client sends. | `24` |
| `NINLIL_DNS_SERVER_CLIENT_SUBNET_SOURCE_PREFIX_LENGTH_IPV6` | IPv6 prefix length truncated into the ECS option; also caps an ECS option a client sends. | `56` |
| `NINLIL_DNS_SERVER_DNS64_ENABLE` | Whether DNS64 (synthesizing AAAA from A for an IPv6-only client) is enabled. | `false` |
| `NINLIL_DNS_SERVER_DNS64_SYNTHESIS_PREFIX` | The NAT64 prefix used to synthesize an AAAA record. | `64:ff9b::/96` |

## Recursive resolver

Full iterative resolution behavior — QNAME minimization, DNSSEC validation, serve-stale, prefetch,
and RFC 5011 automated trust-anchor rollover.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_RECURSION` | Whether this server offers recursive service to every client, by iteration and by forwarding (off by default — capability-expanding; an access rule granting recursion access allows single clients while it is off). | `false` |
| `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_QNAME_MINIMIZATION` | Whether RFC 9156 QNAME minimization is used during iterative resolution. | `true` |
| `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_DNSSEC_VALIDATION` | Whether recursively resolved and forwarded answers are DNSSEC-validated. | `true` |
| `NINLIL_DNS_SERVER_RECURSIVE_USE_CUSTOM_INSECURE_DOMAINS` | Whether the Insecure domain list (`DnsServer:Recursive:InsecureDomains`, the names that skip DNSSEC validation and are never answered with AD) replaces the built-in list. Off: the built-in list applies (`home.arpa`, `internal`, and the RFC 6303 / RFC 7793 locally-served reverse zones). On: only `InsecureDomains` applies, and an empty list exempts nothing. The list itself has no variable; edit it in the admin console, through `PATCH /api/recursive-settings` or in the configuration file. | `false` |
| `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_SERVE_STALE` | Whether RFC 8767 serve-stale (answering from an expired cache entry when upstream is unreachable) is enabled. | `true` |
| `NINLIL_DNS_SERVER_RECURSIVE_MAX_STALE_SERVE_AGE` | The maximum age of a stale cache entry still eligible to be served. | `1.00:00:00` |
| `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_PREFETCH` | Whether a soon-to-expire, recently-popular cache entry is proactively refreshed before it expires. | `true` |
| `NINLIL_DNS_SERVER_RECURSIVE_PREFETCH_WINDOW` | How close to expiry a cache entry must be to qualify for prefetch. | `00:00:10` |
| `NINLIL_DNS_SERVER_RECURSIVE_PREFETCH_RECENT_ACCESS_WINDOW` | How recently a cache entry must have been queried to be considered "popular" for prefetch. | `00:05:00` |
| `NINLIL_DNS_SERVER_RECURSIVE_MAX_CACHE_ENTRIES` | Upper bound on the recursive resolver's own in-memory cache size. | `100000` |
| `NINLIL_DNS_SERVER_RECURSIVE_MAX_CNAME_CHAIN_LENGTH` | Maximum CNAME hops followed within a single resolution before giving up. | `15` |
| `NINLIL_DNS_SERVER_RECURSIVE_MAX_REFERRAL_DEPTH` | Maximum delegation-referral hops followed during an iterative walk. | `30` |
| `NINLIL_DNS_SERVER_RECURSIVE_MAX_UPSTREAM_QUERIES` | Maximum total upstream queries permitted while resolving a single client query. | `100` |
| `NINLIL_DNS_SERVER_RECURSIVE_UPSTREAM_QUERY_TIMEOUT` | Per-upstream-query timeout during iterative resolution. | `00:00:03` |
| `NINLIL_DNS_SERVER_RECURSIVE_OVERALL_QUERY_TIMEOUT` | Overall wall-clock budget for resolving one client query, across every upstream hop. | `00:00:15` |
| `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_AUTOMATIC_TRUST_ANCHOR_UPDATE` | Whether RFC 5011 automated root trust-anchor rollover is enabled. | `true` |
| `NINLIL_DNS_SERVER_RECURSIVE_TRUST_ANCHOR_REFRESH_INTERVAL` | Longest wait between two RFC 5011 checks of the root DNSKEY RRset; the service checks sooner when the RRset's TTL or signature lifetime is shorter (RFC 5011 §2.3), and the first check runs one minute after start. | `15.00:00:00` |
| `NINLIL_DNS_SERVER_RECURSIVE_TRUST_ANCHOR_HOLD_DOWN_DURATION` | RFC 5011's Add-Pending hold-down duration before a new trust anchor becomes valid. | `30.00:00:00` |
| `NINLIL_DNS_SERVER_RECURSIVE_TRUST_ANCHOR_REMOVE_HOLD_DOWN_DURATION` | RFC 5011's hold-down duration before a revoked trust anchor is fully removed. | `30.00:00:00` |
| `NINLIL_DNS_SERVER_RECURSIVE_TRUST_ANCHOR_STATE_FILE_PATH` | Where the current trust-anchor rollover state is persisted across restarts. | `data/recursive-trust-anchors.json` |

## DNSSEC, ACME & CAA

Algorithm policy for newly generated DNSSEC keys, the RFC 8555 ACME DNS-01 client, and automated
per-zone CAA record management.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_ACME_ENABLE` | Whether the ACME client is enabled. | `false` |
| `NINLIL_DNS_SERVER_ACME_DIRECTORY_URI` | The ACME CA's directory endpoint. | `https://acme-v02.api.letsencrypt.org/directory` |
| `NINLIL_DNS_SERVER_ACME_POLL_INTERVAL` | How often the renewal background service checks certificate expiry. | `12:00:00` |
| `NINLIL_DNS_SERVER_ACME_RENEW_BEFORE_EXPIRATION` | How long before expiration a certificate is renewed. | `30.00:00:00` |
| `NINLIL_DNS_SERVER_ACME_CSR_ORGANIZATION` | The Organization field on the generated certificate signing request. | `Ninlil` |
| `NINLIL_DNS_SERVER_ACME_ACCOUNT_STATE_FILE_PATH` | Where the encrypted ACME account state is persisted. | `data/acme-account.json` |
| `NINLIL_DNS_SERVER_ACME_CERTIFICATE_DIRECTORY_PATH` | Where issued certificates are stored on disk. | `data/acme-certificates` |
| `NINLIL_DNS_SERVER_ACME_CAA_AUTO_MANAGEMENT_DEFAULT_ENABLED` | The global default for whether an ACME-enrolled zone auto-manages its own CAA record. | `true` |
| `NINLIL_DNS_SERVER_ACME_CAA_AUTO_MANAGEMENT_DEFAULT_ISSUER_DOMAIN` | Default CAA issuer domain used when a zone doesn't specify its own. | *(empty)* |

## TSIG

RFC 8945 transaction signature settings shared by dynamic update and zone-transfer authentication.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_TSIG_DEFAULT_FUDGE_SECONDS` | The default allowed clock-skew ("fudge") window for a TSIG-signed message's timestamp. | `300` |

## Event broker (message queue) transports

Connection settings for the two optional external message-broker event transports (only relevant
when `NINLIL_DNS_SERVER_EVENTS_TRANSPORT` selects one of them).

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_HOST_NAME` | RabbitMQ broker hostname. | `localhost` |
| `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_PORT` | RabbitMQ broker port. | `5672` |
| `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_VIRTUAL_HOST` | RabbitMQ virtual host. | `/` |
| `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_USER_NAME` | RabbitMQ auth username. | `guest` |
| `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_PASSWORD` | RabbitMQ auth password. Set it from outside the application only. When set and not empty, it overrides a password saved in the admin console. The admin console and cluster sync never write it to the configuration file: they store the password in the `..._ENCRYPTED` variable below. | `guest` |
| `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_PASSWORD_ENCRYPTED` | The RabbitMQ password encrypted with this node's Data Protection key ring. Written by the server (admin console, cluster sync, and the startup step that encrypts a plaintext password left in the file); each cluster node encrypts it with its own key ring. Do not set it by hand: a value from another key ring cannot be decrypted, and the server then logs an Error and uses an empty password, which the broker refuses, until the password is entered again. | *(empty)* |
| `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_EXCHANGE_NAME` | The fanout exchange events are published to. | `ninlil-dns-events` |
| `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_INITIAL_RECONNECT_DELAY` | Initial delay before a reconnect attempt after a dropped connection. | `00:00:01` |
| `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_MAX_RECONNECT_DELAY` | Maximum backoff delay between reconnect attempts. | `00:00:30` |
| `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_HOST_NAME` | ActiveMQ broker hostname. | `localhost` |
| `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_PORT` | ActiveMQ broker port. | `61616` |
| `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_USER_NAME` | ActiveMQ auth username. | `admin` |
| `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_PASSWORD` | ActiveMQ auth password. Set it from outside the application only. When set and not empty, it overrides a password saved in the admin console. The admin console and cluster sync never write it to the configuration file: they store the password in the `..._ENCRYPTED` variable below. | `admin` |
| `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_PASSWORD_ENCRYPTED` | The ActiveMQ password encrypted with this node's Data Protection key ring. Written by the server (admin console, cluster sync, and the startup step that encrypts a plaintext password left in the file); each cluster node encrypts it with its own key ring. Do not set it by hand: a value from another key ring cannot be decrypted, and the server then logs an Error and uses an empty password, which the broker refuses, until the password is entered again. | *(empty)* |
| `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_TOPIC_NAME` | The topic events are published to. | `ninlil-dns-events` |
| `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_PUBLISH_TIMEOUT` | Per-publish timeout. | `00:00:02` |
| `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_INITIAL_RECONNECT_DELAY` | Initial delay before a reconnect attempt. | `00:00:01` |
| `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_MAX_RECONNECT_DELAY` | Maximum backoff delay between reconnect attempts. | `00:00:30` |

## Filtering, rewrites, rate limiting & query log

Block/allow-list filtering, DNS rewrites, per-client-IP abuse rate limiting, and the persistent
query log.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_FILTERING_ENABLE` | Whether block/allow-list filtering is applied to queries. | `false` |
| `NINLIL_DNS_SERVER_FILTERING_PRECEDENCE` | When a domain matches both a block and an allow list, which wins: `AllowWins` or `BlockWins`. | `AllowWins` |
| `NINLIL_DNS_SERVER_FILTERING_BLOCK_RESPONSE_MODE` | How a blocked query is answered: `NxDomain` or `SinkholeAddress`. | `NxDomain` |
| `NINLIL_DNS_SERVER_FILTERING_SINKHOLE_ADDRESS_V4` | The IPv4 address returned for a blocked A query when sinkhole mode is active. | `0.0.0.0` |
| `NINLIL_DNS_SERVER_FILTERING_SINKHOLE_ADDRESS_V6` | The IPv6 address returned for a blocked AAAA query when sinkhole mode is active. | `::` |
| `NINLIL_DNS_SERVER_FILTERING_SINKHOLE_TIME_TO_LIVE_SECONDS` | The TTL attached to a sinkhole response. | `60` |
| `NINLIL_DNS_SERVER_FILTERING_BLOCK_CNAME_CLOAKING` | Whether an answer is also blocked when a CNAME target in it matches the client's domain block lists (catches trackers hidden behind a site's own subdomain). A name on an allow list is never blocked for where it points. | `true` |
| `NINLIL_DNS_SERVER_REWRITES_ENABLE` | Whether DNS rewrites (domain-to-address/CNAME overrides) are applied. Cluster-synced: a write on one node reaches every peer, and enabling Safe Search turns it on everywhere; a variable pins the value on its node. | `false` |
| `NINLIL_DNS_SERVER_REWRITES_TIME_TO_LIVE_SECONDS` | The TTL attached to a rewritten answer. Cluster-synced like `NINLIL_DNS_SERVER_REWRITES_ENABLE`. | `300` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_ENABLE` | Whether per-client-IP query rate limiting is enforced. | `false` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_QUERIES_PER_SECOND` | The sustained per-client token-bucket refill rate. | `20` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_BURST_SIZE` | The token-bucket burst capacity above the sustained rate. | `40` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_MAX_TRACKED_CLIENTS` | Upper bound on tracked per-client state before the limiter fails open (never denies, to avoid becoming its own DoS vector). | `100000` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_TRACKED_CLIENT_EXPIRY` | How long an idle client's tracked state is retained. | `00:05:00` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_CLEANUP_INTERVAL` | How often expired tracked-client state is swept. | `00:01:00` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_SUBNET_PREFIX_LENGTH_IPV4` | IPv4 prefix length used to bucket clients into a subnet-level rate-limit group. | `24` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_SUBNET_PREFIX_LENGTH_IPV6` | IPv6 prefix length used for the same subnet-level bucketing. | `56` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_SUBNET_SAMPLE_WINDOW` | The sampling window subnet-level query counts are measured over. | `00:00:10` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_SUBNET_UDP_QUERY_LIMIT` | Max UDP queries per subnet per sample window before truncation/throttling kicks in. | `500` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_SUBNET_TCP_QUERY_LIMIT` | Max TCP queries per subnet per sample window. | `100` |
| `NINLIL_DNS_SERVER_RATE_LIMITING_SUBNET_TRUNCATE_PERCENTAGE` | The percentage of over-limit UDP responses forced to truncate (pushing the client to retry over TCP) rather than being dropped outright. | `50` |
| `NINLIL_DNS_SERVER_QUERY_LOGGING_ENABLE` | Whether the persistent, searchable query log is active. It is on by default because the log is how an operator sees what the server answers. | `true` |
| `NINLIL_DNS_SERVER_QUERY_LOGGING_PRIVACY_LEVEL` | How much client-identifying detail is recorded: `Full`, `DomainRedacted`, `ClientAnonymized`, or `FullyAnonymous`. | `Full` |
| `NINLIL_DNS_SERVER_QUERY_LOGGING_MAX_RETAINED_ENTRIES` | Hard cap on total retained log entries. | `1000000` |
| `NINLIL_DNS_SERVER_QUERY_LOGGING_RETENTION_PERIOD` | Age-based retention window; older entries are swept regardless of the count cap. | `30.00:00:00` |
| `NINLIL_DNS_SERVER_QUERY_LOGGING_CLEANUP_INTERVAL` | How often the age/count-bounded cleanup sweep runs. | `01:00:00` |

## Forwarding

General upstream forwarding with health-checked failover — independent of the recursive resolver.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_FORWARDING_ENABLE` | Whether configured upstream forwarders are consulted (between the authoritative and recursive resolvers in dispatch order). Only for queries with RD set from clients allowed recursion (`NINLIL_DNS_SERVER_RECURSIVE_ENABLE_RECURSION` or an access rule granting recursion access). | `false` |
| `NINLIL_DNS_SERVER_FORWARDING_FAILOVER_STRATEGY` | How multiple forwarders are chosen between: `TryInOrder` or `HealthCheckedRoundRobin`. | `HealthCheckedRoundRobin` |
| `NINLIL_DNS_SERVER_FORWARDING_HEALTH_CHECK_INTERVAL` | How often each forwarder's health is probed. | `00:00:30` |
| `NINLIL_DNS_SERVER_FORWARDING_PER_FORWARDER_TIMEOUT` | Per-query timeout against a single forwarder before failing over. | `00:00:03` |
| `NINLIL_DNS_SERVER_FORWARDING_ENABLE_CACHE` | Whether forwarded answers are cached until their TTL runs out (negative answers per RFC 2308, capped at 3 hours; no answer outlives its RRSIG expiration). Filtering and rewrites still apply per client on a cached answer. Serve-stale follows `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_SERVE_STALE`. | `true` |
| `NINLIL_DNS_SERVER_FORWARDING_MAX_CACHE_ENTRIES` | Upper bound on how many forwarded answers are cached; the soonest-to-expire are evicted first. | `10000` |

## Client presence & MAC vendor lookup

Passive known-client presence (never active scanning) and MAC vendor lookup against the IEEE
MA-L/MA-M/MA-S registries. Vendor lookup works offline from a snapshot compiled into the build;
the settings below govern only its automatic refresh from the IEEE's own published files.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_CLIENT_PRESENCE_ONLINE_WINDOW` | How recently a known client must have been seen to be reported online. | `00:10:00` |
| `NINLIL_DNS_SERVER_CLIENT_PRESENCE_FLUSH_INTERVAL` | How often buffered presence sightings are written to storage. | `00:00:30` |
| `NINLIL_DNS_SERVER_CLIENT_PRESENCE_ENABLE_NEIGHBOR_TABLE_SIGNAL` | Whether the kernel neighbor table (ARP/NDP, confirmed-reachable entries only) is sampled as a presence signal. | `true` |
| `NINLIL_DNS_SERVER_CLIENT_PRESENCE_NEIGHBOR_TABLE_POLL_INTERVAL` | How often the kernel neighbor table is sampled. | `00:00:15` |
| `NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_ENABLE_AUTOMATIC_REFRESH` | Whether the IEEE registries are periodically re-downloaded (conditionally; a failed or implausible download never replaces loaded data). | `true` |
| `NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_REFRESH_INTERVAL` | How long after a successful check the next one is due (at least one day via the REST API). | `14.00:00:00` |
| `NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_MA_L_REGISTRY_URL` | Where the MA-L (24-bit OUI) registry CSV is downloaded from. | `https://standards-oui.ieee.org/oui/oui.csv` |
| `NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_MA_M_REGISTRY_URL` | Where the MA-M (28-bit) registry CSV is downloaded from. | `https://standards-oui.ieee.org/oui28/mam.csv` |
| `NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_MA_S_REGISTRY_URL` | Where the MA-S (36-bit) registry CSV is downloaded from. | `https://standards-oui.ieee.org/oui36/oui36.csv` |
| `NINLIL_DNS_SERVER_MAC_VENDOR_LOOKUP_DATA_FILE_PATH` | Where refreshed vendor data is persisted (plus a `.state.json` bookkeeping file beside it); read once at startup. | `data/mac-vendors.snapshot.br` |

## Webhooks, backup, self-update & clustering

Outbound event webhooks with persisted retry, encrypted full-system backup, the check-only
self-update service, and multi-node clustering.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_WEBHOOKS_DELIVERY_REQUEST_TIMEOUT` | Per-attempt HTTP timeout for a webhook delivery POST. | `00:00:10` |
| `NINLIL_DNS_SERVER_WEBHOOKS_DELIVERY_INITIAL_RETRY_DELAY` | Initial backoff delay before the first retry after a failed delivery. | `00:00:01` |
| `NINLIL_DNS_SERVER_WEBHOOKS_DELIVERY_MAX_RETRY_DELAY` | Maximum backoff delay between retries. | `00:00:30` |
| `NINLIL_DNS_SERVER_WEBHOOKS_DELIVERY_SWEEP_INTERVAL` | How often the persisted retry-sweep background service looks for deliveries due for another attempt. | `00:00:05` |
| `NINLIL_DNS_SERVER_WEBHOOKS_DELIVERY_ATTEMPT_RETENTION_PERIOD` | How long a completed delivery attempt's history is retained before it's aged out. | `30.00:00:00` |
| `NINLIL_DNS_SERVER_BACKUP_DATA_PROTECTION_KEY_RING_DIRECTORY` | The directory a backup reads the Data Protection key ring (`key-*.xml`) from. A backup bundles these files because every encrypted secret in the database can be decrypted only with them. Startup sets this to the `data/` directory and replaces any value you configure, so changing it has no effect. | `data/` |
| `NINLIL_DNS_SERVER_BACKUP_MINIMUM_PASSPHRASE_LENGTH` | Minimum required length for a backup archive's own encryption passphrase. | `12` |
| `NINLIL_DNS_SERVER_BACKUP_RUNTIME_CONFIGURATION_FILE_PATH` | The runtime settings JSON file a backup bundles and a restore writes back. Startup sets this to the file named by `NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH` and replaces any value you configure, so changing it has no effect. | *(empty — set at startup)* |
| `NINLIL_DNS_SERVER_BACKUP_MAXIMUM_RESTORED_PAYLOAD_BYTES` | The most decompressed bytes a restore reads from one archive, summed over all entries. It stops a zip bomb; there is no reason to change it in production. | `4294967296` (4 GiB) |
| `NINLIL_DNS_SERVER_SELF_UPDATE_ENABLE_AUTOMATIC_CHECK` | Whether the self-update checker runs automatically. It only checks and shows a notice; it never applies an update. With no feed URL (the next row) each check records an "unconfigured" status and fetches nothing. | `true` |
| `NINLIL_DNS_SERVER_SELF_UPDATE_CHECK_INTERVAL` | How often the automatic check runs (beyond the always-performed startup check). | `1.00:00:00` |
| `NINLIL_DNS_SERVER_SELF_UPDATE_RELEASE_FEED_URL` | The URL of the release-feed JSON document to check. The checker does nothing until you set it, and Ninlil does not publish a feed, so point it at a feed you host. | *(empty — no feed is checked)* |
| `NINLIL_CLUSTER_ENABLE` | Whether multi-node clustering is enabled. | `false` |
| `NINLIL_CLUSTER_NODE_ID` | This node's unique cluster identifier. When it is unset, startup creates one at the first start and keeps it in `data/cluster-node-id.txt`, so it survives restarts. Every peer that joins records this id, so do not change it afterwards. No REST endpoint changes it. | `00000000-0000-0000-0000-000000000000` (replaced by a generated id at startup) |
| `NINLIL_CLUSTER_ADVERTISED_HOST` | The hostname/address this node advertises to its cluster peers. | *(empty)* |
| `NINLIL_CLUSTER_ADVERTISED_PORT` | The port this node advertises for cluster membership traffic; `0` means the web HTTPS port. | `0` |
| `NINLIL_CLUSTER_HEARTBEAT_INTERVAL` | How often this node sends a heartbeat to its peers. | `00:00:30` |
| `NINLIL_CLUSTER_HEARTBEAT_TIMEOUT` | How long to wait for a peer's heartbeat response. | `00:00:10` |
| `NINLIL_CLUSTER_MISSED_HEARTBEAT_THRESHOLD` | Consecutive missed heartbeats before a peer is considered down. | `3` |

## DHCP, DHCPv6, Router Advertisement & TFTP

The dual-stack DHCPv4/DHCPv6 engine, its mutually-exclusive relay-agent alternative, SLAAC/Router
Advertisement content, and the PXE-boot TFTP server. Every DHCP-family transport is off by default.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DHCP_SERVER_CORE_SERVER_IDENTIFIER_ADDRESS` | The address this DHCP server identifies itself with (auto-detected from the primary interface when unset). | *(none — auto-detected)* |
| `NINLIL_DHCP_SERVER_CORE_SERVER_DUID` | This server's own DHCPv6 Unique Identifier (auto-generated and persisted when unset). | *(none — auto-generated)* |
| `NINLIL_DHCP_SERVER_CORE_SERVER_IPV6_ADDRESS` | The server's own IPv6 address, used for a DHCPv6 option whose value is the server address. When unset or not a valid non-link-local IPv6 address, the first such address on an interface that is up is detected at startup. Set it when the host has several IPv6 interfaces. | *(none — auto-detected)* |
| `NINLIL_DHCP_SERVER_CORE_OFFER_HOLD_DURATION` | How long a DHCPv4 OFFER's address is held reserved awaiting the client's REQUEST. | `00:00:30` |
| `NINLIL_DHCP_SERVER_CORE_DECLINED_ADDRESS_QUARANTINE_DURATION` | How long an address a client DECLINEd is quarantined before being offered again. | `00:10:00` |
| `NINLIL_DHCP_SERVER_CORE_LEASE_EXPIRY_SWEEP_INTERVAL` | How often the natural-lease-expiry sweep runs. | `00:01:00` |
| `NINLIL_DHCP_SERVER_CORE_ENABLE_DHCPV6_RAPID_COMMIT` | Whether DHCPv6 Rapid Commit (two-message SOLICIT/REPLY exchange) is honored when a client requests it. | `false` |
| `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE` | Whether the local (non-relay) DHCPv4 engine's UDP listener is active. | `false` |
| `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_SERVER_PORT` | The DHCPv4 server-side UDP port. | `67` |
| `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_CLIENT_PORT` | The DHCPv4 client-side UDP port replies are sent to. | `68` |
| `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV6_ENABLE` | Whether the local (non-relay) DHCPv6 engine's UDP listener is active. | `false` |
| `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV6_SERVER_PORT` | The DHCPv6 server-side UDP port. | `547` |
| `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV6_CLIENT_PORT` | The DHCPv6 client-side UDP port. | `546` |
| `NINLIL_DHCP_SERVER_RELAY_DHCPV4_ENABLE` | Whether the DHCPv4 relay-agent engine is active instead of the local engine (mutually exclusive with it). | `false` |
| `NINLIL_DHCP_SERVER_RELAY_DHCPV4_RELAY_AGENT_ADDRESS` | The `giaddr` address this relay agent inserts into forwarded requests. | *(none)* |
| `NINLIL_DHCP_SERVER_RELAY_DHCPV4_RELAY_AGENT_CIRCUIT_ID` | The RFC 3046 Relay Agent Information Circuit ID this relay inserts: printable ASCII, at most 253 characters each and 251 together (invalid values are logged and the option is left out). | *(none)* |
| `NINLIL_DHCP_SERVER_RELAY_DHCPV4_RELAY_AGENT_REMOTE_ID` | The RFC 3046 Relay Agent Information Remote ID this relay inserts: printable ASCII, at most 253 characters each and 251 together (invalid values are logged and the option is left out). | *(none)* |
| `NINLIL_DHCP_SERVER_RELAY_DHCPV4_UPSTREAM_SERVER_PORT` | The port the upstream real DHCPv4 server is reached on. | `67` |
| `NINLIL_DHCP_SERVER_RELAY_DHCPV6_ENABLE` | Whether the DHCPv6 relay-agent engine is active instead of the local engine (mutually exclusive with it). | `false` |
| `NINLIL_DHCP_SERVER_RELAY_DHCPV6_UPSTREAM_SERVER_PORT` | The port the upstream real DHCPv6 server is reached on. | `547` |
| `NINLIL_DHCP_SERVER_RELAY_DHCPV6_RELAY_LINK_ADDRESS` | The RFC 8415 `link-address` this relay inserts into RELAY-FORW messages; a global or unique-local address on the client-facing link. When unset, the first IPv6 address that is neither loopback nor link-local is detected at startup. | *(none)* |
| `NINLIL_DHCP_SERVER_RELAY_DHCPV6_INTERFACE_ID` | The RFC 8415 Section 21.18 Interface-ID, as ASCII text, added to every RELAY-FORW; when unset, no Interface-ID option is added. | *(none)* |
| `NINLIL_DHCP_SERVER_PROTOCOL_RA_ENABLE` | Whether the raw-socket Router Advertisement/Solicitation transport is active. | `false` |
| `NINLIL_DHCP_SERVER_PROTOCOL_RA_MIN_ROUTER_ADVERTISEMENT_INTERVAL` | RFC 4861's shortest interval between unsolicited advertisements (MinRtrAdvInterval). The default is about a third of the default maximum. The settings API requires at least 3 seconds and at most 0.75 times the maximum below. | `00:03:18` |
| `NINLIL_DHCP_SERVER_PROTOCOL_RA_MAX_ROUTER_ADVERTISEMENT_INTERVAL` | RFC 4861's longest interval between unsolicited advertisements (MaxRtrAdvInterval). Each interval is a random time between the minimum and the maximum. The settings API requires 4 to 65535 seconds. | `00:10:00` |
| `NINLIL_DHCP_SERVER_RA_ROUTER_LIFETIME` | The advertised router lifetime: how long hosts may use this server as a default router. `00:00:00` means this server is not a default router. The field holds at most 65535 seconds: the settings API rejects a larger value and a value from the configuration file is sent as 65535. | `00:00:00` |
| `NINLIL_DHCP_SERVER_RA_CURRENT_HOP_LIMIT` | The advertised current IPv6 hop limit. `0` means no recommendation. | `64` |
| `NINLIL_DHCP_SERVER_RA_REACHABLE_TIME` | The advertised neighbor-reachable time. `00:00:00` means no recommendation. The maximum is one hour (RFC 4861 §6.2.1). | `00:00:00` |
| `NINLIL_DHCP_SERVER_RA_RETRANS_TIMER` | The advertised neighbor-solicitation retransmit timer. `00:00:00` means no recommendation. | `00:00:00` |
| `NINLIL_DHCP_SERVER_RA_MANAGED_ADDRESS_CONFIGURATION` | Whether the advertised "M" flag tells hosts to use DHCPv6 for address assignment. | `false` |
| `NINLIL_DHCP_SERVER_RA_OTHER_CONFIGURATION` | Whether the advertised "O" flag tells hosts to use DHCPv6 for other (non-address) configuration. | `false` |
| `NINLIL_DHCP_SERVER_RA_LINK_MTU` | The link MTU to advertise in the MTU option. When unset, no MTU option is sent. A value below 1280 (RFC 8200 §5) is rejected by the settings API and left out of the advertisement. | *(none)* |
| `NINLIL_DHCP_SERVER_RA_RECURSIVE_DNS_SERVER_LIFETIME` | The lifetime of the RFC 8106 RDNSS option, which gives SLAAC hosts their DNS servers. The server addresses are a list and have no variable: at most 75 IPv6 addresses fit in one option, and the advertisement skips any beyond that. The default is three times the default maximum interval above. | `00:30:00` |
| `NINLIL_DHCP_SERVER_RA_DNS_SEARCH_LIST_LIFETIME` | The lifetime of the RFC 8106 DNSSL option, which the router advertisement sends to give SLAAC hosts their DNS search domains. The domains are a list and have no variable: the option is at most 1208 octets, and the advertisement leaves out any domain that would not fit. | `00:30:00` |
| `NINLIL_DHCP_SERVER_TFTP_ENABLE` | Whether the read-only PXE-boot TFTP server is active. | `false` |
| `NINLIL_DHCP_SERVER_TFTP_PORT` | The TFTP UDP port. | `69` |
| `NINLIL_DHCP_SERVER_TFTP_ROOT_DIRECTORY` | The filesystem root TFTP serves boot files from (a leading `/` means this root; a path or link leading outside it is always rejected). | *(empty)* |
| `NINLIL_DHCP_SERVER_TFTP_RETRANSMIT_TIMEOUT` | Per-block retransmit timeout. | `00:00:02` |
| `NINLIL_DHCP_SERVER_TFTP_MAX_RETRANSMISSIONS` | Max retransmissions before giving up on a transfer. | `5` |
| `NINLIL_DHCP_SERVER_TFTP_MAX_CONCURRENT_TRANSFERS` | Max simultaneous transfers (at most 4 per client address, at least 1). A request over a limit is dropped without a reply and counted in a warning once a minute. | `50` |

## Web host (Kestrel, reverse proxy, branding)

The combined host's own HTTP(S)/Kestrel listener, reverse-proxy trust, and admin-console
appearance/branding.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_DNS_SERVER_WEB_HTTP_PORT` | The plain-HTTP port that serves the REST API and the admin console. Requests on it are answered over HTTP; Ninlil does not redirect them to HTTPS. | `80` |
| `NINLIL_DNS_SERVER_WEB_HTTPS_PORT` | The HTTPS port serving the REST API, SignalR hub, admin console, and DNS-over-HTTPS. | `443` |
| `NINLIL_DNS_SERVER_WEB_LISTEN_ADDRESSES` | Comma-separated IP addresses the HTTP and HTTPS ports listen on, and so the admin console, REST API and DNS-over-HTTPS (for example `192.168.1.10`); empty listens on every address. An address this host doesn't have is skipped; if none can be used, the listener falls back to loopback so the console can't be locked out. Applies on restart. | *(empty — every address)* |
| `NINLIL_DNS_SERVER_WEB_CERTIFICATE_PFX_PATH` | Custom PFX certificate path for the web listener. | *(empty)* |
| `NINLIL_DNS_SERVER_WEB_CERTIFICATE_PFX_PASSWORD` | Password for the custom PFX file above. | *(empty)* |
| `NINLIL_DNS_SERVER_WEB_CERTIFICATE_SELF_SIGNED_CERTIFICATE_STATE_PATH` | Where the auto-generated self-signed certificate is persisted — the same file every TLS-based DNS transport shares by default. | `data/protocol-tls-certificate.pfx` |
| `NINLIL_DNS_SERVER_WEB_CERTIFICATE_ALLOWED_PROTOCOLS` | Explicit TLS protocol version override. | `None` |
| `NINLIL_DNS_SERVER_WEB_CERTIFICATE_CERTIFICATE_RELOAD_POLL_INTERVAL` | Certificate reload poll interval. | `00:05:00` |
| `NINLIL_DNS_SERVER_REVERSE_PROXY_ENABLE` | Whether this host trusts `X-Forwarded-*` headers from a reverse proxy (independent from DNS-over-TLS's own separate PROXY-protocol trust list). | `false` |
| `NINLIL_DNS_SERVER_REVERSE_PROXY_SHARED_SECRET_HEADER_NAME` | The header name a trusted reverse proxy must present a shared secret in. | `X-Ninlil-Proxy-Secret` |
| `NINLIL_DNS_SERVER_REVERSE_PROXY_SHARED_SECRET` | The shared secret value the header above must match. Set it from outside the application only. When set and not empty, it overrides a secret saved in the admin console. The admin console and cluster sync never write it to the configuration file: they store the secret in the `..._ENCRYPTED` variable below. | *(none)* |
| `NINLIL_DNS_SERVER_REVERSE_PROXY_SHARED_SECRET_ENCRYPTED` | The shared secret encrypted with this node's Data Protection key ring. Written by the server (admin console, cluster sync, and the startup step that encrypts a plaintext secret left in the file); each cluster node encrypts it with its own key ring. Do not set it by hand: a value from another key ring cannot be decrypted, and the server then logs an Error and trusts no reverse proxy (it fails closed) until the secret is entered again. | *(empty)* |
| `NINLIL_DNS_SERVER_APPEARANCE_MODE` | The admin console's default theme mode: `Light`, `Dark`, or `System`. | `System` |
| `NINLIL_DNS_SERVER_APPEARANCE_PALETTE` | The admin console's default color palette: `Teal`, `Indigo`, `Amber`, `Rose`, or `Emerald`. | `Teal` |
| `NINLIL_DNS_SERVER_BRANDING_SITE_TITLE` | A custom browser-tab/header title overriding the default "Ninlil". | *(none)* |
| `NINLIL_DNS_SERVER_BRANDING_LOGO_DATA_URI` | A custom logo, as a `data:` URI, overriding the default. | *(none)* |
| `NINLIL_DNS_SERVER_RUNTIME_SETTINGS_CONFIGURATION_FILE_PATH` | Path of the JSON file that the settings pages merge their changes into at runtime, so a change survives a restart without editing the file by hand. | *(empty — startup uses `appsettings.json` in the content root)* |

## Framework-level variables

Each of these copies its value onto the unprefixed .NET or ASP.NET Core hosting variable named in
the second column. Ninlil does the copy before the web host builder reads its configuration, in
`FrameworkEnvironmentVariableBootstrap.Apply` (called from `Program.cs`). They configure the host,
not Ninlil's own options.

| Variable | Redirects to | Controls |
|---|---|---|
| `NINLIL_ASPNETCORE_ENVIRONMENT` | `ASPNETCORE_ENVIRONMENT` | The ASP.NET Core hosting environment name (`Development`/`Production`/...). Takes precedence over the generic host variable below when both are set. |
| `NINLIL_DOTNET_ENVIRONMENT` | `DOTNET_ENVIRONMENT` | The generic host's own hosting environment name. |
| `NINLIL_ASPNETCORE_URLS` | `ASPNETCORE_URLS` | A semicolon-separated list of URLs Kestrel binds to by default (this project's own `WebListenOptions` configures Kestrel directly, so this is a secondary/legacy path). |
| `NINLIL_ASPNETCORE_HTTP_PORTS` | `ASPNETCORE_HTTP_PORTS` | The newer, simpler "just the port numbers" alternative to `ASPNETCORE_URLS` for HTTP. |
| `NINLIL_ASPNETCORE_HTTPS_PORTS` | `ASPNETCORE_HTTPS_PORTS` | The same, for HTTPS. |
| `NINLIL_ASPNETCORE_CONTENTROOT` | `ASPNETCORE_CONTENTROOT` | Where the host looks for `appsettings.json`/`wwwroot`. Takes precedence over the generic host variable below. |
| `NINLIL_DOTNET_CONTENTROOT` | `DOTNET_CONTENTROOT` | The generic host's own content-root variable. |

### .NET runtime settings

A few `DOTNET_*` settings are read by the native runtime itself — before any managed code runs,
so no in-process redirection can reach them. Ninlil still accepts them under the `NINLIL_` scheme:
on Linux (the release binaries and the container image), when a `NINLIL_DOTNET_<SETTING>` variable
is present at startup, the server restarts itself in place — the same process, replaced with
`execve`, keeping its PID — with that setting exported under the runtime's own exact spelling. The
log shows `Restarting to apply .NET runtime settings: ...` when it does. Matching ignores case and
underscores, because the runtime's names are case-sensitive and mixed-case — `DOTNET_gcServer`
works, `DOTNET_GCSERVER` is silently ignored — so `NINLIL_DOTNET_GC_SERVER=1` becomes
`DOTNET_gcServer=1`. On macOS, set these under their `DOTNET_*` names instead.

| Variable | Becomes | Controls |
|---|---|---|
| `NINLIL_DOTNET_GC_SERVER` | `DOTNET_gcServer` | Server garbage collection (`1`) instead of workstation (`0`, the default). |
| `NINLIL_DOTNET_GC_CONCURRENT` | `DOTNET_gcConcurrent` | Background (concurrent) garbage collection. |
| `NINLIL_DOTNET_GC_HEAP_HARD_LIMIT` | `DOTNET_GCHeapHardLimit` | A hard cap on the managed heap, in bytes (hexadecimal). |
| `NINLIL_DOTNET_GC_HEAP_HARD_LIMIT_PERCENT` | `DOTNET_GCHeapHardLimitPercent` | The same cap as a percentage of available memory (hexadecimal). |
| `NINLIL_DOTNET_GC_CONSERVE_MEMORY` | `DOTNET_GCConserveMemory` | How hard the GC works to compact the heap (0–9). |
| `NINLIL_DOTNET_TIERED_PGO` | `DOTNET_TieredPGO` | Dynamic profile-guided optimization. |
| `NINLIL_DOTNET_ENABLE_DIAGNOSTICS` | `DOTNET_EnableDiagnostics` | The diagnostics IPC channel, debugger, and profiler attach (`0` disables). |
| `NINLIL_DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` | `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT` | Culture-invariant globalization mode. |

Also recognized: `GCHeapCount`, `GCHeapAffinitizeMask`, `GCHeapAffinitizeRanges`,
`GCNoAffinitize`, `GCRetainVM`, `GCgen0size`, `GCDynamicAdaptationMode`, `TieredCompilation`,
`TC_QuickJitForLoops`, `ReadyToRun`, `DbgEnableMiniDump`, `DbgMiniDumpType`, `DbgMiniDumpName`, and
`SYSTEM_GLOBALIZATION_PREDEFINED_CULTURES_ONLY` (see `NativeRuntimeSettingsRelaunch` in
`Ninlil.Web`). A `NINLIL_DOTNET_*` name that matches none of them is not applied and is reported
as unrecognized at startup, rather than being passed through under a guessed spelling the runtime
might silently ignore.

## Bootstrap administrator credentials

Two settings read directly by configuration key rather than through a bound `Options` class — a
one-time bootstrap credential does not need to be an injectable configuration object.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_USER_NAME` | The bootstrap administrator account's username. | `admin` |
| `NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD` | The bootstrap administrator account's password. If unset, a strong random password is generated and printed once to the startup log — there is no way to retrieve it again afterward. Setting this explicitly is useful for a scripted/unattended first boot. | *(none — random-generated if unset)* |

## Host filtering

ASP.NET Core's `AllowedHosts` setting, also read directly by key.

| Variable | Controls | Default |
|---|---|---|
| `NINLIL_ALLOWED_HOSTS` | A semicolon-separated list of Host header values the web listener answers (for example `dns.example.com;192.168.1.10`; `*.example.com` matches subdomains). Any other Host gets `400`. Unset accepts every Host, because an install is usually reached by an address or name the server cannot know in advance; nothing the server builds (such as password-reset links, see `NINLIL_EMAIL_PUBLIC_BASE_URL`) trusts the Host header. | `*` |
