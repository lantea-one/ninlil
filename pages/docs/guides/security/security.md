# Security

This page lets you harden a fresh Ninlil installation: protect the data directory, replace the
bootstrap administrator with your own account, give each person only the permissions they need,
switch on two-factor sign-in and limit who reaches the console. The README holds the complete
reference, including every behaviour listed under
[Behaviour worth knowing](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#behaviour-worth-knowing-13).

Ninlil checks every REST request against a **permission** of the form `resource:action`, such as
`zones:write`. A **role** is a named set of permissions. An account gets permissions from its roles
and from direct grants. The permission `*` allows everything.

## Before you start

1. A running Ninlil and shell access to the host it runs on. The commands use the ports from the
   [quick start](../../getting-started/quick-start.md): `18443` (HTTPS) and `18080` (HTTP).
2. An account that holds `*` (the bootstrap administrator does) and its access token. Sign in as the
   quick start shows. The commands call the token `<token>`.
3. The location of the data directory. The [first run](../../getting-started/first-run.md) page
   lists it for each installation.

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Protect the data directory

The data directory holds the database, the key ring, the JWT signing key and the TLS certificate
with its private key. Whoever reads it can read every secret Ninlil stores.

1. On Linux and macOS, Ninlil sets the folder to mode 0700 (owner only) when it starts. Check it
   (`<data-directory>` is the path from the first-run page):

    ```bash
    stat -c '%a %n' <data-directory>
    ```

    ```text
    700 data
    ```

    The key files inside have mode 600. The database file itself has mode 644, so the folder mode
    is what keeps other users out.
2. On Windows, a folder has an access list instead of a mode. List it:

    ```powershell
    icacls C:\Ninlil\data
    ```

    Only the service account, `BUILTIN\Administrators` and `NT AUTHORITY\SYSTEM` should appear. A line
    for `BUILTIN\Users` or `NT AUTHORITY\Authenticated Users` lets other accounts read the keys.
    Switch off inheritance, which copies the inherited entries, and remove the two groups:

    ```powershell
    icacls C:\Ninlil\data /inheritance:d
    icacls C:\Ninlil\data /remove "BUILTIN\Users" "NT AUTHORITY\Authenticated Users"
    ```

3. In a container, the data directory is the volume you mount at `/app/data`
   ([Docker](../../docker.md)). The container runs as user `1654` and sets the folder to mode 0700.
   Keep the volume's folder on the host readable only by the people who administer the host.

## Encrypt the key ring

The key ring is the set of `key-*.xml` files in the data directory. It unlocks every secret in the
database. How Ninlil protects it depends on your settings:

| Case | What happens |
|---|---|
| A certificate is set, on any platform | New key ring entries are encrypted with the certificate. Any machine that has the certificate can read them. |
| No certificate, on Windows | New entries are encrypted with Windows DPAPI. Only the same machine and the same account can read them. |
| No certificate, on Linux and macOS | New entries are not encrypted. Protection comes from the folder mode. |

1. Decide where a copy of the data directory (a backup, a disk image) may be read. If it may be
   restored on another machine, use a certificate, on Windows too.
2. Create an RSA certificate and export it with its private key as a PKCS#12 (`.pfx`) file. Nobody
   has to trust it: it only wraps the keys. This example makes a 10-year certificate (`<password>` is
   a password you choose):

    ```bash
    openssl req -x509 -newkey rsa:3072 -nodes -keyout keyring.key -out keyring.crt -subj '/CN=ninlil-keyring' -days 3650
    openssl pkcs12 -export -out keyring.pfx -inkey keyring.key -in keyring.crt -passout pass:<password>
    ```

    Keep `keyring.pfx` outside the data directory, and keep a copy apart from your backups. Without
    the certificate, nobody can read the key ring or any secret stored with it.
3. Set the path and the password, then restart Ninlil
   ([`NINLIL_DATA_PROTECTION_CERTIFICATE_PATH`, `NINLIL_DATA_PROTECTION_CERTIFICATE_PASSWORD`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#data-protection-key-ring)):

    ```bash
    NINLIL_DATA_PROTECTION_CERTIFICATE_PATH=/etc/ninlil/keyring.pfx
    NINLIL_DATA_PROTECTION_CERTIFICATE_PASSWORD=<password>
    ```

4. To change the certificate later, set the new one as `NINLIL_DATA_PROTECTION_CERTIFICATE_PATH`
   and the old one as `NINLIL_DATA_PROTECTION_PREVIOUS_CERTIFICATE_PATH` (with its password), then
   restart. Remove the previous setting only after every key written under it has expired and you
   have taken a new backup.
5. On a cluster whose nodes share one key ring folder, give every node the same certificate
   settings.

The JWT signing key stays a plain file in the data directory. The folder mode protects it. Whoever
holds `jwt-signing-key.txt` can make a token for any enabled account, so treat it like a root
password.

### Check that it worked

A key file that Ninlil wrote after the restart holds an `encryptedSecret` element and no
`masterKey` element. This lists the key files with no `masterKey` element. On a new data directory,
the key file must appear:

```bash
grep -L masterKey <data-directory>/key-*.xml
```

```text
data/key-2b319380-872e-4861-9a4a-9a7bc7bc466c.xml
```

Keys written before you set the certificate stay readable and stay unencrypted until Data
Protection creates a new key, which happens every 90 days.

A path that does not exist, a wrong password, a file without a private key or an ECDSA certificate
stops the start with [exit code 78](../../reference/exit-codes.md). Ninlil never falls back to an
unencrypted key ring:

```text
Startup refused: DataProtection:CertificatePath names the file '/etc/ninlil/nope.pfx', which does not exist or cannot be read. Ninlil will not start the key ring without it: it would write new keys unencrypted and could not read keys the certificate encrypted. Fix the path or clear the setting.
```

## Replace the bootstrap administrator

The account `admin` holds `*`. Ninlil creates it only while no account exists and never deletes it.
Replace it with an account of your own, then disable it.

1. Create your account. The password needs at least 12 characters with an upper-case letter, a
   lower-case letter, a digit and a symbol. The e-mail address must be unique:

    ```bash
    curl -k https://127.0.0.1:18443/api/users -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"userName":"alice","password":"Correct-Horse-9!","email":"alice@example.com","displayName":"Alice"}'
    ```

    ```text
    {"id":"<user-id>","userName":"alice","email":"alice@example.com","displayName":"Alice","isDisabled":false,"twoFactorEnabled":false,"roles":[],"directPermissions":[], ...}
    ```

    Copy the `id`. The next commands call it `<user-id>`.
2. Put the account in the `Administrators` role, which holds `*`. Only a holder of `*` can do this:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/users/<user-id>/roles -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"roleName":"Administrators"}'
    ```

    The answer is an empty `204`.
3. Sign in as the new account and use its token from here on. The next command calls it
   `<alice-token>`:

    ```bash
    curl -k https://127.0.0.1:18443/api/auth/login -H 'Content-Type: application/json' -d '{"userName":"alice","password":"Correct-Horse-9!"}'
    ```

4. Disable the bootstrap account. `PUT` replaces the display name, the e-mail address and the
   disabled flag, so send all three. `<admin-id>` is the `id` of `admin` in `GET /api/users`:

    ```bash
    curl -k -X PUT https://127.0.0.1:18443/api/users/<admin-id> -H "Authorization: Bearer <alice-token>" -H 'Content-Type: application/json' -d '{"displayName":"Administrator","email":"admin@localhost.local","isDisabled":true}'
    ```

    The answer is `204`. In the console, the same steps are on **Users** (`/users`).

### Check that it worked

The disabled account can no longer sign in, and the token it already held stops working:

```bash
curl -k -s -o /dev/null -w '%{http_code}\n' https://127.0.0.1:18443/api/auth/login -H 'Content-Type: application/json' -d '{"userName":"admin","password":"Change-Me-Now1!"}'
curl -k -s -o /dev/null -w '%{http_code}\n' https://127.0.0.1:18443/api/zones -H "Authorization: Bearer <token>"
```

```text
401
401
```

The account stays in the list. Set `"isDisabled":false` in the same `PUT` to switch it back on.

## Give each person the permissions they need

1. List the permission strings the server knows (the answer is a JSON array such as
   `["accesscontrol:read","accesscontrol:write", ...]`):

    ```bash
    curl -k https://127.0.0.1:18443/api/permissions -H "Authorization: Bearer <token>"
    ```

2. Create a role. The answer holds the role's `id`, called `<role-id>` below:

    ```bash
    curl -k https://127.0.0.1:18443/api/roles -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"name":"zone-editor","description":"Edits zones and records"}'
    ```

3. Grant it permissions, one call each. You can grant only permissions you hold yourself. Each call
   answers `204`:

    ```bash
    curl -k https://127.0.0.1:18443/api/roles/<role-id>/permissions -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"permission":"zones:write"}'
    curl -k https://127.0.0.1:18443/api/roles/<role-id>/permissions -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"permission":"zones:read"}'
    ```

4. Put an account in the role with the call from the previous section, using the role's name:
   `{"roleName":"zone-editor"}`. The console does the same on **Roles** (`/roles`) and **Users**.

### API tokens for programs

A program that calls the API uses an **API token** instead of a password. It works until you revoke
it, and it carries its owner's current permissions. Create one and send it in an `ApiToken`
header:

```bash
curl -k -X POST https://127.0.0.1:18443/api/api-tokens -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"name":"ci"}'
```

```text
{"rawToken":"QJLd-5X9le0C5lpjw_Jjw8SbIfPlhzshwgtvg7_sZek","token":{"id":"<token-id>","name":"ci","type":"Static", ...}}
```

Ninlil shows `rawToken` once. Use it:

```bash
curl -k https://127.0.0.1:18443/api/zones -H 'Authorization: ApiToken <raw-token>'
```

Revoke it with `DELETE /api/api-tokens/<token-id>`. The console page is **API tokens**
(`/api-tokens`). A token created with `"oneTime":true` works once.

## Switch on two-factor sign-in

Switch it on for every administrator. In the console, open the account menu in the top-right
corner, choose **Security** and follow the steps for an authenticator app or for e-mailed codes.
E-mailed codes need SMTP settings
([Single sign-on and e-mail secrets](sso-and-email-secrets.md)). Through the API, an account sets up
an authenticator like this:

1. Ask for a shared key (`<alice-token>` is the account's own token):

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/auth/two-factor/authenticator/setup -H "Authorization: Bearer <alice-token>"
    ```

    ```text
    {"sharedKey":"GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ","authenticatorUri":"otpauth://totp/Ninlil.Dns:alice?secret=GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ&issuer=Ninlil.Dns&digits=6"}
    ```

    Enter `sharedKey` in an authenticator app, or scan `authenticatorUri` as a QR code. The sample
    `sharedKey` above is the RFC 6238 test secret, not a real one. The values you get differ every
    time.
2. Confirm it with the six-digit code the app shows. Confirming ends every session of the account,
   this one included:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/auth/two-factor/authenticator/enable -H "Authorization: Bearer <alice-token>" -H 'Content-Type: application/json' -d '{"code":"<code>"}'
    ```

    The answer is an empty `204`.
3. Sign in again. The password step answers `requiresTwoFactor` and an `mfaToken`. Send the
   code from the app with that token:

    ```bash
    curl -k https://127.0.0.1:18443/api/auth/login/two-factor -H 'Content-Type: application/json' -d '{"mfaToken":"<mfa-token>","code":"<code>","provider":"Authenticator"}'
    ```

    The answer holds the `accessToken`.
4. Generate single-use recovery codes and store them offline. The call returns ten codes once:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/auth/two-factor/recovery-codes/generate -H "Authorization: Bearer <alice-token>"
    ```

    ```text
    {"codes":["MX6BX-QQDKB","RXGTN-BF995", ...]}
    ```

    To sign in with a recovery code, send `"provider":"RecoveryCode"` in step 3.

### Check that it worked

```bash
curl -k https://127.0.0.1:18443/api/auth/two-factor/status -H "Authorization: Bearer <alice-token>"
```

```text
{"isEnabled":true,"hasAuthenticator":true,"canReceiveEmailCode":false,"recoveryCodesRemaining":10}
```

## Limit who reaches the console

1. The REST API and the console answer on both web ports. The plain-HTTP port has no TLS, so it
   carries passwords and tokens in the clear. Check it:

    ```bash
    curl -s -o /dev/null -w '%{http_code}\n' http://127.0.0.1:18080/api/zones
    ```

    ```text
    401
    ```

    The `401` shows that the API answers on the HTTP port. Close that port in your firewall, or limit
    both web ports to one address with
    [`NINLIL_DNS_SERVER_WEB_LISTEN_ADDRESSES`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding).
2. Behind a reverse proxy, tell Ninlil the proxy's address. Ninlil then takes the client's address
   from the proxy's `X-Forwarded-For` header. Without that, the sign-in limit sees only the proxy and
   counts every user together. `<proxy-address>` is the address the proxy connects from:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/reverse-proxy-settings -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"enable":true,"knownProxies":["<proxy-address>"]}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read once, at startup, so a restart is required before this change takes effect."}
    ```

    Restart Ninlil. The console page is **Settings**, **Reverse proxy**.
3. Replace the self-signed certificate with one from
   [Certificates (ACME)](../dns/certificates-acme.md) or a PFX file, set with
   `NINLIL_DNS_SERVER_WEB_CERTIFICATE_PFX_PATH` (see the
   [Web host rows](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding)).

## Check that it worked

1. The data directory is private: the `stat` command above printed `700`.
2. A weak password is refused:

    ```bash
    curl -k https://127.0.0.1:18443/api/users -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"userName":"bob","password":"short1!","email":"bob@example.com","displayName":"Bob"}'
    ```

    ```text
    {"type":"https://httpstatuses.io/400","title":"One or more validation errors occurred.","status":400,"detail":"One or more validation errors occurred.","errors":{"PasswordTooShort":["Passwords must be at least 12 characters."],"PasswordRequiresUpper":["Passwords must have at least one uppercase ('A'-'Z')."]},"traceId":"..."}
    ```

3. The sign-in limit works. It allows 5 attempts in 5 minutes for each client address and then
   answers `429`. Run this against a test installation only: it blocks your address for five
   minutes. Attempts that came earlier in the same window count, so on an address that signed in
   before, the `429` starts sooner:

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

    The `429` carries a `Retry-After` header with the seconds that are left. Restarting Ninlil
    clears the count.

## When something goes wrong

- *An account is refused with `PasswordTooShort` or `DuplicateEmail`.* Use a password that meets the
  rules above, or another e-mail address.
- *Every sign-in answers `429`.* The address reached the sign-in limit. Wait for the seconds in the
  `Retry-After` header. Behind a reverse proxy without the reverse-proxy settings, all users share
  one address, so one user's mistakes block the others.
- *A token that worked answers `401` after a password or two-factor change.* A password, e-mail or
  two-factor change ends every session of the account. Sign in again. API tokens keep working.
- *Ninlil exits with code 78 and the log says `The JWT signing key is <n> bytes long`.* The key is
  shorter than 16 bytes. Use at least 32 bytes (`openssl rand -base64 48`) in
  [`NINLIL_JWT_SIGNING_KEY`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#identity-authentication--sso),
  or delete `data/jwt-signing-key.txt` so Ninlil generates a key.
- *The bootstrap password is lost.* Ninlil prints a generated password once. With another
  administrator account, reset it with `POST /api/users/<admin-id>/reset-password` (Swagger UI
  describes the body). Without one, restore a [backup](../operations/backup-and-restore.md) or start
  with an empty data directory.

## Next steps

- [Single sign-on and e-mail secrets](sso-and-email-secrets.md)
- [Backup and restore](../operations/backup-and-restore.md): a backup holds the key ring, so set a
  passphrase.
- README: [Security](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#security)
  for the sign-in limit, token lifetimes and accepted risks, and
  [SECURITY.md](https://git.lantea.one/lantea-one/ninlil/src/branch/main/SECURITY.md) for how to
  report a vulnerability.
