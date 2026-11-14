# Troubleshooting

This page helps you find the cause of a failed start, a sign-in that is refused, a listener that is
down or a DNS query that gets no answer. Each entry gives the symptom, the cause, a check and a
fix. The message texts are the ones Ninlil prints.

## Before you start

1. Know where the log is. Ninlil writes its log to the console of the process. Where you read it
   depends on how you installed Ninlil:

    | Installed as | Read the log with |
    |---|---|
    | Linux package (Deb, RPM, Arch) | `sudo journalctl -u ninlil -e` |
    | Docker | `docker logs ninlil` |
    | Downloaded binary or `dotnet run` | the terminal where you started it |
    | Windows service | `Get-WinEvent -FilterHashtable @{LogName='Application'; ProviderName='Ninlil.Web'} -MaxEvents 5 \| Format-List TimeCreated, Message` |

2. Know the web port. The checks below use the plain-HTTP port of the [Quick start](getting-started/quick-start.md),
   `18080`. Replace it with yours.
3. For the health detail, an access token of an account with the `status:read` permission
   ([REST API](rest-api.md#sign-in)).

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](getting-started/quick-start.md#self-signed-certificate).

## Steps

### Read the health first

1. Ask for the overall state:

    ```bash
    curl http://127.0.0.1:18080/status
    ```

    ```text
    {"status":"Healthy","components":null}
    ```

2. If it is not `Healthy`, ask with a token for the component that is not:

    ```bash
    curl http://127.0.0.1:18080/status -H 'Authorization: Bearer <token>'
    ```

    Each component has a `name`, a `status` (`Healthy`, `Degraded` or `Unhealthy`) and a
    `description` that says why.

### A port is in use

**The web port is in use.** Ninlil does not start. It exits with code 134 and the log holds:

```text
Hosting failed to start
System.IO.IOException: Failed to bind to address http://[::]:18080: address already in use.
```

- Cause: another program (often another Ninlil) holds that web port.
- Check: `ss -ltn 'sport = :18080'` lists the holder.
- Fix: stop the other program, or choose another port with
  [`NINLIL_DNS_SERVER_WEB_HTTP_PORT`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding)
  and `NINLIL_DNS_SERVER_WEB_HTTPS_PORT`. See [Ports](reference/ports.md).

**A DNS port is in use.** Ninlil keeps running without that transport. The log holds, one entry for
TCP and one for UDP:

```text
Plain DNS (UDP) could not listen on :: port 15300: Address already in use. This transport is not running; the rest of the server is unaffected. Fix the port or the listen addresses and restart Ninlil.
```

`/status` answers `{"status":"Unhealthy","components":null}`, and with a token the `transports`
component says:

```text
2 enabled transport(s) are not listening: Plain/UDP (:: (Address already in use)), Plain/TCP (:: (Address already in use)). Listening: none.
```

- Cause: another program holds the port. On a Linux desktop it is usually the `systemd-resolved`
  stub on port 53.
- Fix: free the port, or move Ninlil's transport with its port setting, then restart Ninlil.

### Ninlil exits with code 78

The log has an entry that begins `Startup refused:`. For example, a JWT signing key that is too
short (`NINLIL_JWT_SIGNING_KEY=short`):

```text
Startup refused: The JWT signing key is 5 bytes long, and HMAC-SHA256 needs at least 16. Set NINLIL_JWT_SIGNING_KEY to a random value of 32 bytes or more. If the key comes from data/jwt-signing-key.txt, delete that file so Ninlil generates a new key (this signs every user out).
```

- Cause: Ninlil refuses to run on what it finds. The message names the setting or file.
- Fix: do what the message says and start again. [Exit codes](reference/exit-codes.md) lists every
  cause and what restarts a refused start.

### Ninlil exits with code 134

A start that fails on a missing or wrong certificate file or an unreachable database ends with an
unhandled exception and exit code 134, not 78. The first lines of the log name the cause:

```text
Unhandled exception. System.Security.Cryptography.CryptographicException: The certificate data cannot be read with the provided password, the password may be incorrect.
```

```text
Unhandled exception. System.Security.Cryptography.CryptographicException: Error occurred during a cryptographic operation.
 ---> System.IO.DirectoryNotFoundException: Could not find a part of the path '/nonexistent/web.pfx'.
```

```text
Unhandled exception. Npgsql.NpgsqlException (0x80004005): Failed to connect to 127.0.0.1:5432
 ---> System.Net.Sockets.SocketException (111): Connection refused
```

- Certificate cause: `NINLIL_DNS_SERVER_WEB_CERTIFICATE_PFX_PATH` names a file that is missing, or
  `NINLIL_DNS_SERVER_WEB_CERTIFICATE_PFX_PASSWORD` is wrong. Check with `ls -l <path>` and
  `openssl pkcs12 -in <path> -noout` (it asks for the password). Fix the path or the password, or
  unset both to use the self-signed certificate.
- Database cause: `NINLIL_STORAGE_CONNECTION_STRING` points at a server that is down or at the wrong
  host or port. Check that the database accepts connections from the Ninlil host, for example
  `nc -zv <db-host> <db-port>`. Fix the connection string or start the database.

### I cannot sign in

The status of the sign-in answer tells the cause:

| Answer | Console message | Cause and fix |
|---|---|---|
| `401` | Incorrect username or password. | The password is wrong, the account is disabled or the name is unknown. Check the name. Another administrator can reset the password. |
| `423` | This account is locked. Try again later, or ask an administrator to unlock it. | The account has a lockout end date in the future. Wrong passwords never set it. An administrator or the system sets it. |
| `429` | Too many sign-in attempts. Try again in `<n>` minutes. | The address reached the sign-in limit. See below. |

**`429` after a few sign-ins.** One address may make 5 password or second-factor sign-in attempts in
each 5-minute window
([`NINLIL_DNS_SERVER_ADMIN_LOGIN_RATE_LIMIT_PERMIT_LIMIT`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#identity-authentication--sso)
and `..._WINDOW`). Every attempt counts, successful ones too. Wrong passwords answer `401` until the limit is used up.
Every further attempt answers `429`, and the response header names the wait in seconds (your value
differs):

```text
HTTP/2 429
retry-after: 167
```

- Check: `curl -k -s -D - -o /dev/null https://127.0.0.1:18443/api/auth/login -H 'Content-Type: application/json' -d '{"userName":"x","password":"x"}' | grep -i -E '^(HTTP|retry-after)'`.
- Fix: wait for `retry-after` seconds, or restart Ninlil. Behind a reverse proxy that is not set up
  in the Reverse proxy settings, all users share one address, so one user's mistakes block the others.

**A second factor is refused.** Five wrong codes in 15 minutes for an account answer `429`. Use a
recovery code, or wait.

#### I lost the password of the administrator

Ninlil prints a generated bootstrap password once, in the log, and never again. With another
administrator, reset the password with `POST /api/users/<admin-id>/reset-password` (the request body
is in Swagger UI, [REST API](rest-api.md)). Without another administrator, restore a backup
([Backup and restore](guides/operations/backup-and-restore.md)) or start with an empty data
directory.

### A call answers 403 or the console says Access Denied

- Cause: the account lacks the permission the route needs. An account with no role holds none.
  The answer is `403` with an empty body.
- Check: Swagger UI names the permission in the call's description.
- Fix: add a role that holds the permission ([Using the admin console](admin-console.md#what-a-permission-hides)).

### Queries get REFUSED or no answer

**`status: REFUSED`.** A query for a name Ninlil does not host, with recursion off, gets this (the
id differs):

```bash
dig @127.0.0.1 -p 15300 example.com A +noall +comments
```

```text
;; ->>HEADER<<- opcode: QUERY, status: REFUSED, id: 48481
;; flags: qr rd; QUERY: 1, ANSWER: 0, AUTHORITY: 0, ADDITIONAL: 1
;; WARNING: recursion requested but not available
```

- Cause: the client is not permitted to recurse.
- Fix: turn recursion on, or add an access control rule for the client's network with the
  `RecursionAccess` capability ([Forwarding and recursion](guides/dns/forwarding-and-recursion.md)).

**No answer at all.** `dig` prints `;; no servers could be reached`. Two causes answer by silence on
purpose, so a sender cannot tell them from packet loss:

- Rate limiting is on and the client used its budget. With
  `NINLIL_DNS_SERVER_RATE_LIMITING_QUERIES_PER_SECOND=1` and `..._BURST_SIZE=2`, Ninlil answers the
  first queries and drops the rest. Fix: raise the limits, or give the client's network an access
  control rule with the `RateLimitBypass` capability.
- The client-source check denies the client. The log at the Debug level records the drop reason
  ("Dropping every query from <address>: <reason>.").

Also check that the transport listens (`/status`, `transports`).

### A certificate warning or error

- *The browser or `curl` warns about the certificate.* Ninlil made a self-signed certificate at the
  first start. Accept it for a first look, or install a trusted one
  ([Security](guides/security/security.md), [Certificates (ACME)](guides/dns/certificates-acme.md)).
- *The `tlsCertificate` component is not `Healthy`.* Its description gives the expiry, for example
  `The current TLS certificate ('CN=localhost') is valid until 2028-11-13T02:52:15.0000000Z.` Renew
  the certificate before that date.
- *Ninlil exits with code 134 on the certificate.* See above.

### The database is unreachable or a replica is not used

- *`storage` is `Unhealthy`.* The description is `<engine> storage could not be reached.` or
  `Failed to reach <engine> storage.`. Check the database server and the connection string.
- *`storage` is `Degraded` with a read replica.* The description ends with `The read replica
  serves no reads and the primary is used instead (<lag>): <reason>`. Everything still works; reads
  use the primary. Fix the replica or its lag
  ([Storage](guides/operations/storage.md)).
- *The start is refused because the MySQL server has `read_only` set.* The message begins
  `The MySQL server that Storage:ConnectionString reaches has read_only set`. The connection string
  reaches a replica. Point it at the writable primary.

## Check that it worked

After the fix, `curl http://127.0.0.1:18080/status` answers `{"status":"Healthy","components":null}`
and the sign-in or the query that failed succeeds.

## Next steps

- [Exit codes](reference/exit-codes.md): every cause of code 78.
- [REST API](rest-api.md): the status route and the sign-in.
- [Ports](reference/ports.md): which port each listener uses.
- The [README](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md) guides each have a
  "When something goes wrong" part for one feature.
