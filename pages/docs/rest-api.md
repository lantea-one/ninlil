# REST API

Everything the [admin console](admin-console.md) does is a REST call, and the API covers more than
the console shows. This page shows you how to find the API documents, sign in, call the API from a
script, page through a list, read an error and connect to the live event hub.

The API speaks JSON over HTTPS. The commands on this page use the Quick start ports (`18443` for
HTTPS) and the Quick start administrator. The default HTTPS port is `443`. They use `-k` because the
self-signed certificate that Ninlil makes is not trusted by `curl`.

## Before you start

1. A running Ninlil with a known administrator ([Quick start](getting-started/quick-start.md)).
2. `curl`. Check it:

    ```bash
    curl --version
    ```

    It prints a version line. `command not found` means `curl` is missing.
3. Know the routes you need:

    | What | Route |
    |---|---|
    | Swagger UI, to read and try every call | `/swagger` (redirects to `/swagger/index.html`) |
    | The OpenAPI document (JSON) | `/swagger/v1/swagger.json` |
    | ReDoc reference | `/documentation` (the page loads its viewer script from `cdn.jsdelivr.net`, so the browser needs internet access) |
    | Health | `GET /status` |
    | Sign in | `POST /api/auth/login` |
    | Live events | `/hubs/events` (SignalR) |

    The API answers on both web ports. Use the HTTPS port: the plain-HTTP port sends tokens in the
    clear. There is no API version in the address and no CORS, so a browser page from another origin
    cannot call the API. A `curl` call is not affected.

Two kinds of credential work, and both carry the permissions of the account behind them:

- An **access token** is a JSON Web Token (JWT) from signing in. It lasts 15 minutes
  ([`NINLIL_JWT_ACCESS_TOKEN_LIFETIME`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#identity-authentication--sso)).
- An **API token** is a long-lived secret you create for a program that runs unattended.

Each call needs the permission its route names, such as `zones:write`.

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](getting-started/quick-start.md#self-signed-certificate).

## Steps

### Sign in

1. Send the user name and password. The commands keep the two tokens in the shell variables
   `TOKEN` and `REFRESH`. The `sed` commands cut the values out of the answer. You can also copy
   the values by hand:

    ```bash
    LOGIN=$(curl -k -s https://127.0.0.1:18443/api/auth/login -H 'Content-Type: application/json' -d '{"userName":"admin","password":"Change-Me-Now1!"}')
    echo "$LOGIN"
    TOKEN=$(echo "$LOGIN" | sed -n 's/.*"accessToken":"\([^"]*\)".*/\1/p')
    REFRESH=$(echo "$LOGIN" | sed -n 's/.*"refreshToken":"\([^"]*\)".*/\1/p')
    ```

    ```text
    {"requiresTwoFactor":false,"mfaToken":null,"accessToken":"eyJhbGciOi...","expiresAtUtc":"2026-11-14T03:09:55.8648095+00:00","refreshToken":"a0iJqn-Vzk..."}
    ```

    (Your values differ.) The request body is `userName`, `password` and, optionally, `rememberMe`
    and `refreshTokenDelivery`.
2. If the answer says `"requiresTwoFactor":true`, the account uses two-factor sign-in. Send the
   `mfaToken` from the answer with a code to `POST /api/auth/login/two-factor`. `provider` is
   `Authenticator`, `Email` or `RecoveryCode`. The answer has the same shape as step 1:

    ```bash
    curl -k -s https://127.0.0.1:18443/api/auth/login/two-factor -H 'Content-Type: application/json' -d '{"mfaToken":"<mfa-token>","code":"123456","provider":"Authenticator"}'
    ```

3. Call the API with the access token in an `Authorization: Bearer` header. List the zones:

    ```bash
    curl -k -s https://127.0.0.1:18443/api/zones -H "Authorization: Bearer $TOKEN"
    ```

    ```text
    {"items":[],"skip":0,"take":50,"totalCount":0}
    ```

### Create and change things

1. Create an object with `POST` and a JSON body. Send `Content-Type: application/json`. A success
   answers `201` and the created object:

    ```bash
    curl -k -s -w '\n[HTTP %{http_code}]\n' https://127.0.0.1:18443/api/zones -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"name":"one.test","type":"Primary"}'
    ```

    ```text
    {"id":"84c60fae-3468-48f1-b363-c7b9f191a3da","name":"one.test","type":"Primary","isDnssecSigned":false, ...}
    [HTTP 201]
    ```

    Copy the `id`. The next commands call it `<zone-id>`.
2. Change part of an object with `PATCH`. The body is an
   [RFC 7396](https://www.rfc-editor.org/rfc/rfc7396) JSON merge patch: a field you leave out
   stays, a field you set replaces the old value, and `null` clears a field that may be empty:

    ```bash
    curl -k -s -w '[HTTP %{http_code}]\n' -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"isDisabled":true,"comments":null}'
    ```

    ```text
    [HTTP 204]
    ```

    `204` has no body. Read the zone again to see the result. A `PATCH` ignores a field it does not
    know, so a misspelled name still answers `204`.
3. A settings `PATCH` answers `200` with a note that says whether the change is live or needs a
   restart, as in the console:

    ```bash
    curl -k -s -X PATCH https://127.0.0.1:18443/api/forwarding-settings -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"enable":false}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read live, so it takes effect immediately -- no restart required."}
    ```

    A live change reaches the running server within about a second. A `GET` straight after the
    `PATCH` can still return the old values, so read again after a moment.

An enum field, such as `type` here, takes one of the names the OpenAPI document lists. Case does not
matter. A number, a digit string or an unknown name answers `400` and the message lists the valid
names.

### Page through a list

Every list route takes `skip` (how many items to leave out) and `take` (how many to return). The
answer repeats both and adds `totalCount`:

```bash
curl -k -s "https://127.0.0.1:18443/api/zones?skip=1&take=1" -H "Authorization: Bearer $TOKEN"
```

```text
{"items":[{"id":"...","name":"two.test", ...}],"skip":1,"take":1,"totalCount":3}
```

- `take` defaults to 50. A value above 500 becomes 500. A `take` of 0 or less becomes 50.
- A negative `skip` becomes 0. A `skip` that is not a number answers `400`.
- There is no general `search` parameter. A list route that can be filtered has its own named
  parameters. The query log takes `clientAddress`, `domain`, `queriedType`, `responseCode`,
  `fromUtc`, `toUtc`, `clientIdentifier` and `clientProtocolVersion`:

    ```bash
    curl -k -s "https://127.0.0.1:18443/api/query-log?domain=one.test&take=2" -H "Authorization: Bearer $TOKEN"
    ```

  Swagger UI shows the parameters of every route.

### Renew the access token

Renew the access token before it expires, with the refresh token from the sign-in. Each refresh
token works once, and the answer holds the next one. Put it in `REFRESH` for the next renewal:

```bash
curl -k -s https://127.0.0.1:18443/api/auth/refresh -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$REFRESH\"}"
```

```text
{"accessToken":"eyJhbGciOi...","expiresAtUtc":"2026-11-14T03:24:55.1234567+00:00","refreshToken":"Trqd_-lmMt..."}
```

The refresh token lasts 30 days
([`NINLIL_JWT_REFRESH_TOKEN_LIFETIME`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#identity-authentication--sso)).
A used, revoked or expired refresh token answers `401`.

### Use an API token for a script

1. Create the token. The answer shows its `rawToken` once. Copy it:

    ```bash
    curl -k -s https://127.0.0.1:18443/api/api-tokens -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json' -d '{"name":"backup script","oneTime":false}'
    ```

    ```text
    {"rawToken":"KpkPNpTpp1Wg...","token":{"id":"a07a9db8-5ff0-4e69-bb73-3ab221b8202a","name":"backup script","type":"Static","createdAtUtc":"2026-11-14T02:52:48.1494056+00:00","expiresAtUtc":null,"consumedAtUtc":null,"revokedAtUtc":null}}
    ```

    `"oneTime":true` makes a token that works for one call. Add
    `"expiresAtUtc":"2027-08-31T00:00:00+00:00"` to give it an end date. In the console:
    **API Tokens**, **New**. A token without an end date stays valid until you revoke it, so give a
    script's token an end date or revoke it when you stop using it.
2. Use it with the header `Authorization: ApiToken <raw-token>`. The word is `ApiToken`, not
   `Bearer`:

    ```bash
    curl -k -s https://127.0.0.1:18443/api/zones -H 'Authorization: ApiToken <raw-token>'
    ```

3. Revoke it with its `id`. A revoked token answers `401`:

    ```bash
    curl -k -s -X DELETE -w '[HTTP %{http_code}]\n' https://127.0.0.1:18443/api/api-tokens/<token-id> -H "Authorization: Bearer $TOKEN"
    ```

    ```text
    [HTTP 204]
    ```

### Check that Ninlil is healthy

`GET /status` needs no credential and returns the overall state:

```bash
curl -k -s https://127.0.0.1:18443/status
```

```text
{"status":"Healthy","components":null}
```

With an access token whose account holds `status:read`, it adds one entry for each subsystem:

```bash
curl -k -s https://127.0.0.1:18443/status -H "Authorization: Bearer $TOKEN"
```

```text
{"status":"Healthy","components":[{"name":"storage","status":"Healthy","description":"Sqlite storage is reachable.","error":null},{"name":"tlsCertificate", ...
```

The status is `Healthy`, `Degraded` or `Unhealthy`. [Troubleshooting](troubleshooting.md) explains
what to do with a component that is not `Healthy`.

### Sign out

Revoke the refresh token. No access token is needed, and the answer is `204` even for an unknown
token:

```bash
curl -k -s -o /dev/null -w '%{http_code}\n' https://127.0.0.1:18443/api/auth/logout -H 'Content-Type: application/json' -d "{\"refreshToken\":\"$REFRESH\"}"
```

```text
204
```

## Errors

Error answers are JSON with a `status` and a `traceId` that you can find in the log.

| Status | Meaning | Example body |
|---|---|---|
| `400` | The request is wrong. `errors` names each field. | `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.1","title":"One or more validation errors occurred.","status":400,"errors":{"$.type":["'Bogus' is not a valid value for ZoneTypeEnum. Valid values: Primary, Secondary, Stub, ..."]},"traceId":"..."}` |
| `401` | No valid credential. The header `www-authenticate: Bearer` is set and the body is empty or `{"title":"Unauthorized", ...}`. | |
| `403` | The account lacks the permission. The body is empty. | |
| `404` | Unknown id. `entityType` and `entityId` name it. | `{"type":"https://httpstatuses.io/404","title":"Not found.","status":404,"detail":"Zone '<id>' could not be found.","entityType":"Zone","entityId":"<id>","traceId":"..."}` |
| `409` | The name is taken. | `{"type":"https://httpstatuses.io/409","title":"Conflict.","status":409,"detail":"A zone named 'one.test' already exists.","traceId":"..."}` |
| `415` | The `Content-Type: application/json` header is missing. | `{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.16","title":"Unsupported Media Type","status":415,"traceId":"..."}` |
| `429` | Too many sign-in attempts. The `Retry-After` header holds the seconds to wait. | |

## The event hub

The console's live feed uses a [SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/introduction)
hub at `/hubs/events`. Your own program can connect with any SignalR client. The hub only pushes
events. It has no methods to call.

1. Sign in and keep the access token ([Sign in](#sign-in)). A JWT and an API token both work.
2. Connect to `https://<host>:<port>/hubs/events` and send the token in the `Authorization` header
   of the negotiate request. A browser cannot set a header on a WebSocket request, so the hub also
   accepts the token as the `access_token` query parameter. Only the routes under `/hubs` accept it
   that way. Check the connection door with `curl`:

    ```bash
    curl -k -s -X POST "https://127.0.0.1:18443/hubs/events/negotiate?negotiateVersion=1" -H "Authorization: Bearer $TOKEN"
    ```

    ```text
    {"negotiateVersion":1,"connectionId":"65_frgKZRyGIOJoOzNLnTQ","connectionToken":"XjjM-SDBTNc_meI0cF-vuw","availableTransports":[{"transport":"WebSockets","transferFormats":["Text","Binary"]},{"transport":"ServerSentEvents", ...
    ```

    Without a credential the answer is `401`.
3. Listen for these four methods. Each carries one JSON object with camelCase fields:

    | Method | Fields | Needs |
    |---|---|---|
    | `DnsQueryReceived` | `occurredAtUtc`, `protocol`, `remoteAddress`, `queriedName`, `queriedType` | `querylog:read` |
    | `DnsQueryAnswered` | the fields above, plus `responseCode`, `answerCount` | `querylog:read` |
    | `DnsZoneChanged` | `occurredAtUtc`, `zoneId`, `zoneName` | `zones:read` |
    | `DnsCacheEvicted` | `occurredAtUtc`, `reason`, `entryCount` | `recursivesettings:read` |

The server decides which methods a connection receives when it opens, from the permissions in its
token. A permission you gain or lose applies after the next reconnect. The server closes a
connection when its access token expires, so a client must reconnect with a fresh token. The
browser client does so automatically.

## Check that it worked

A call without a token is refused:

```bash
curl -k -s -o /dev/null -w '%{http_code}\n' https://127.0.0.1:18443/api/zones
```

```text
401
```

Open `https://127.0.0.1:18443/swagger` in a browser, choose **Authorize**, paste the access token
without the word `Bearer`, and try a call. The Authorize button supports the access token only. To
test an API token, use `curl`.

## Common mistakes

- **`curl` prints `SSL certificate problem: self-signed certificate`.** Add `-k`, or install a
  trusted certificate ([Security](guides/security/security.md)).
- **Every call answers `401`.** The access token expired after 15 minutes or is missing. Sign in
  again or refresh it. With an API token, check that the header starts with `ApiToken ` and not
  `Bearer `. A token in the `access_token` query parameter works only on `/hubs` routes.
- **A call answers `403`.** The account lacks the permission the route needs. Swagger UI shows it in
  the call's description.
- **A call answers `415`.** Add `-H 'Content-Type: application/json'`.
- **A call answers `400` with `is not a valid value for ...`.** Use one of the names the message
  lists.
- **The sign-in answers `429`.** The address reached the sign-in limit (5 attempts in 5 minutes by
  default). Wait for `Retry-After` seconds.

## Next steps

- [Using the admin console](admin-console.md): the same operations in the browser.
- [Troubleshooting](troubleshooting.md): sign-in problems and health components.
- [Security](guides/security/security.md): roles, permissions and the sign-in limit.
- The [README](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#rest-api) for the
  complete reference.
