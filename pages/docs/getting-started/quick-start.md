# Quick start

This walk-through starts Ninlil on unprivileged ports, signs in, creates one zone with one record
and asks the server for that record. It changes nothing outside the folder you run it in.

## Before you start

1. A Linux x86-64 host. On another platform, install Ninlil first
   ([Installation](installation/index.md)) and use the same ports and commands from step 2 on.
2. `curl` and `dig` (your distribution's DNS utilities package provides `dig`). Check both:

    ```bash
    curl --version
    dig -v
    ```

    Each prints a version line. `command not found` means the tool is missing.
3. Three free ports. This page uses `18080` (web, HTTP), `18443` (web, HTTPS) and `15300` (DNS).
   Check one of them. The command prints only a header line when the port is free:

    ```bash
    ss -ltnu 'sport = :15300'
    ```

    ```text
    Netid State  Recv-Q Send-Q Local Address:Port Peer Address:Port
    ```

## Self-signed certificate

Ninlil generates a self-signed TLS certificate, so `curl` cannot check it. `-k` turns the
certificate check off for that one command. That is safe only for a call to `127.0.0.1` from the
machine that runs Ninlil. On a network, `-k` lets anyone in the path pose as the server and read the
token. For a call from another machine, serve a trusted certificate
([Certificates (ACME)](../guides/dns/certificates-acme.md)) and leave `-k` out.

## Steps

1. Get the executable: follow steps 1 to 3 of
   [Download a binary](installation/binary.md). Stay in the folder that holds
   `ninlil-linux-amd64`.
2. Start Ninlil. The password is the bootstrap administrator's password (at least 12 characters).
   The `NINLIL_DNS_SERVER_*` variables move the ports off the privileged defaults:

    ```bash
    NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD='Change-Me-Now1!' \
    NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
    NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
    NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15300 \
    ./ninlil-linux-amd64
    ```

    Leave it running and open a second terminal for the next steps. To use the container image
    instead, follow [Docker](../docker.md) and publish `18080`, `18443` and `15300` as shown there.
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
    Copy it once and reuse it: every sign-in counts toward the sign-in limit, and the sixth from
    one address within 5 minutes answers `429` (see [Security](../guides/security/security.md)).
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

## Check that it worked

Ask the server for the record:

```bash
dig @127.0.0.1 -p 15300 www.example.test A +noall +answer
```

```text
www.example.test.	3600	IN	A	192.0.2.10
```

Then open `https://localhost:18443/` in a browser, accept the certificate warning that the
self-signed certificate causes, sign in as `admin` with the password from step 2 and open
**Zones**: `example.test` is listed.

Press Ctrl+C in the first terminal to stop Ninlil. To start over, delete the `data` folder next to
the executable.

## When something goes wrong

- *Step 7 or 6 answers `A zone's very first record must be its own SOA record.`* Run step 6 first.
- *The `dig` answer has status `NXDOMAIN`.* The record is missing or has another name. List the
  zone's records with `GET /api/zones/<zone-id>/records` and compare.
- *Ninlil does not start.* Read the log lines in the first terminal. A web port that is in use is
  the usual cause: choose another port number. A DNS port that is in use does not stop Ninlil; the
  log and `GET /status` show it.

## Next

- [First run](first-run.md): what Ninlil created in the `data` folder.
- [Installation](installation/index.md): a permanent setup.
- [Guides](../guides/index.md): one page for each feature.
