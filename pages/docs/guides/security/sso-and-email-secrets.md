# Single sign-on and e-mail secrets

This page shows how to give Ninlil the OpenID Connect (SSO) client secret, the SMTP password and
the other stored secrets, either through the admin console or from the environment, and how to
recover when the key ring that protects them is lost. The README holds the complete reference:
[Single sign-on and e-mail secrets](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#single-sign-on-and-e-mail-secrets).

A secret can come from two places. Saved in the admin console, Ninlil stores it encrypted with this
server's Data Protection key ring (the `key-*.xml` files in the data directory). Supplied from the
environment, it is plaintext and wins over a saved one.

| Secret | Plaintext variable | Admin console page |
|---|---|---|
| SSO client secret | `NINLIL_IDENTITY_SSO_CLIENT_SECRET` | Settings, Single sign-on (`/settings/sso`) |
| SMTP password | `NINLIL_EMAIL_PASSWORD` | Settings, SMTP (`/settings/smtp`) |
| Reverse-proxy shared secret | `NINLIL_DNS_SERVER_REVERSE_PROXY_SHARED_SECRET` | Settings, Reverse proxy |
| RabbitMQ password | `NINLIL_DNS_SERVER_EVENTS_RABBIT_MQ_PASSWORD` | Settings, Event broker |
| ActiveMQ password | `NINLIL_DNS_SERVER_EVENTS_ACTIVE_MQ_PASSWORD` | Settings, Event broker |

## Before you start

1. A running Ninlil and an account with `ssosettings:write` and `smtpsettings:write` (the bootstrap
   administrator holds them). Sign in as the [quick start](../../getting-started/quick-start.md)
   shows. The commands call the access token `<token>` and use the quick start's HTTPS port `18443`.
2. For SSO: an OpenID Connect provider and a client registered there. You need its authority URL (the
   address that serves `/.well-known/openid-configuration`), the client ID and the client secret.
3. For e-mail: an SMTP server that Ninlil's host reaches.
4. Know the data directory, which holds the key ring
   ([First run](../../getting-started/first-run.md)).

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Save the secrets in the admin console or through the API

1. Open **Settings**, then **Single sign-on**. Enter the authority, the client ID and the client
   secret, switch **Enable** on and save. Through the API:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/sso-settings -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"enable":true,"authority":"<authority-url>","clientId":"<client-id>","clientSecret":"<client-secret>","scopes":["openid","profile","email"]}'
    ```

    ```text
    {"note":"Written to the configuration file. The OpenID Connect authentication scheme reads 'enable', 'authority', 'clientId', 'clientSecret' and 'scopes' only once, while the application is starting up -- a restart is required before a change to any of them changes how admin sign-in through your identity provider works. 'displayName' applies at once."}
    ```

2. Restart Ninlil. SSO settings are read once, at startup.
3. Open **Settings**, then **SMTP**. Enter the host, port, TLS choice, user name, password, sender
   address and the public base URL (the address users type to reach the console, which Ninlil
   puts in the links it e-mails). Through the API:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/smtp-settings -H "Authorization: Bearer <token>" -H 'Content-Type: application/json' -d '{"smtpHost":"<smtp-host>","smtpPort":587,"useTls":true,"username":"<user>","password":"<password>","fromAddress":"<sender-address>","fromDisplayName":"Ninlil","publicBaseUrl":"https://<console-address>"}'
    ```

    A user name needs a password: Ninlil rejects a save that leaves a user name without one. Clear
    the user name for a mail server that accepts anonymous connections.

## Supply a secret from the environment

Use this for containers and secret stores. The secret is never written to disk.

1. Set the variable for the Ninlil process, for example in your compose file or systemd unit:

    ```text
    NINLIL_IDENTITY_SSO_CLIENT_SECRET=<client-secret>
    ```

    For the Deb and RPM packages, the file is `/etc/ninlil/ninlil.env`. For a container, add
    `-e NINLIL_IDENTITY_SSO_CLIENT_SECRET=<client-secret>`.
2. Restart Ninlil.
3. Open the settings page. It shows that the secret comes from the environment and overrides
   anything saved there. A secret you save on the page has no effect until you unset the variable.

A plaintext secret you type into `appsettings.json` is encrypted at startup and the plaintext is
removed. Do not put a plaintext value into a variable that ends in `_ENCRYPTED`: Ninlil cannot read
it.

In a cluster, the reverse-proxy shared secret and the event broker passwords also travel to the other
nodes, over the encrypted and signed cluster channel. Each node encrypts them with its own key ring.
See [Clustering, events and webhooks](../operations/clustering.md).

## Check that it worked

Read the settings back. They report where the secret comes from and never show it (wait a few
seconds after a save, because Ninlil reloads its configuration file first):

```bash
curl -k https://127.0.0.1:18443/api/sso-settings -H "Authorization: Bearer <token>"
```

With the secret supplied from the environment:

```text
{"enable":false,"displayName":"Single Sign-On","authority":"https://idp.example.test","clientId":"ninlil","clientSecretConfigured":true,"scopes":[ ... ],"clientSecretFromEnvironment":true,"clientSecretUnreadable":false}
```

With a secret saved in the console, `clientSecretFromEnvironment` is `false` and
`clientSecretConfigured` is `true`. The SMTP page reports `passwordConfigured`,
`passwordFromEnvironment` and `passwordUnreadable` the same way
(`GET /api/smtp-settings`).

To test sign-in, open the console's sign-in page in a private browser window. A button with the
display name appears. After the first sign-in, an administrator must grant the new account roles: an
SSO account starts with no permissions. See
[Security](security.md) for roles.

## If the key ring is lost

If the data directory is recreated, or `appsettings.json` is copied to another host without the
`key-*.xml` files, Ninlil cannot decrypt a saved secret. Ninlil keeps running. The settings
page shows the secret as unreadable, and `GET /api/sso-settings` answers
`"clientSecretUnreadable":true`. This is what the check above prints for a copy of the settings file
on a host with a new key ring:

```text
{"enable":false,"displayName":"Single Sign-On","authority":"https://idp.example.test","clientId":"ninlil","clientSecretConfigured":true,"scopes":[ ... ],"clientSecretFromEnvironment":false,"clientSecretUnreadable":true}
```

What each secret does while it is unreadable:

- **SSO client secret.** With SSO enabled, Ninlil logs an Error at startup and sign-in through the
  provider fails at the provider.
- **SMTP password.** Ninlil logs an Error when it sends a mail.
- **Reverse-proxy shared secret.** Ninlil logs an Error at startup and trusts no reverse proxy until
  you enter the secret again and restart.
- **Event broker password.** It becomes an empty password. The broker refuses the login, Ninlil logs
  an Error at startup and keeps running, and the event broker stays disconnected until you enter the
  password again and restart.

To recover:

1. Open the SSO, SMTP, Reverse proxy or Event broker settings page, or set the plaintext variable
   from the table above.
2. Enter the secret again and save, or restart with the variable set.
3. For SSO, the reverse proxy and the event broker, restart Ninlil so it reads the new secret.

To avoid the problem, include the key ring in your backups: a [backup](../operations/backup-and-restore.md)
holds it. If you protect the key ring with a certificate, keep the certificate too
([Security](security.md#encrypt-the-key-ring)).

## When something goes wrong

- *A save through the API shows no change when you read the settings straight away.* Ninlil reloads
  its configuration file a moment after the save. Read again after a few seconds.
- *`clientSecretUnreadable` is `true`.* Follow "If the key ring is lost".
- *A save is rejected for a user name without a password.* Enter the SMTP password, or clear the user
  name.
- *The SSO button does not appear after a save.* SSO settings are read at startup. Restart Ninlil.
- *A user from the provider cannot sign in.* An account with a verified e-mail address that another
  account already uses is refused, and so is a disabled account.

## Next steps

- [Security](security.md)
- [Backup and restore](../operations/backup-and-restore.md)
- [Configuration](../../configuration.md)
