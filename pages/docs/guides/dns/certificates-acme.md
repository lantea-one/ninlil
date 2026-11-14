# Certificates (ACME)

This page shows how to get and renew TLS certificates with Ninlil's ACME client (RFC 8555). A
certificate authority (CA) such as Let's Encrypt issues the certificate. Ninlil proves control of
your domain with the DNS-01 challenge: it publishes a TXT record at `_acme-challenge.<domain>` in a
zone that Ninlil hosts (RFC 8555 §8.4), waits until the zone's servers serve it and asks the CA to
check. Ninlil stores the certificate as a PKCS#12 file (`.pfx`) and renews it before it expires.

## Before you start

1. A running Ninlil, a signed-in API token and the placeholders `<token>` and `<zone-id>`, as in
   [Zones and records](zones-and-records.md) "Before you start". The commands use the API on
   `https://127.0.0.1:18443`.
2. A user that holds `acmesettings:read` and `acmesettings:write` to change the settings, and
   `acmesettings:manage` to issue a certificate on demand.
3. A public domain whose zone Ninlil hosts as `Primary` or `CloudPrimary`, and whose name servers at
   the registrar answer for that zone. The CA asks the public DNS, so the TXT record must be visible
   there. Ninlil refuses any other zone type, because it is not the source of truth for it.
4. A choice of CA. The default is the Let's Encrypt production directory, which limits how often you
   may request certificates. Use the staging directory
   `https://acme-staging-v02.api.letsencrypt.org/directory` until the setup works.
5. Optional: an e-mail address for the CA to reach you. Ninlil registers every address you list as a
   `mailto:` contact (RFC 8555 §7.3). With none, the account has no contact.

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Steps

1. Change the ACME settings. `domains` lists every name a certificate is wanted for, one certificate
   per entry. A leading `*.` asks for a wildcard certificate. Replace `example.com` with your
   domain:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/acme-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"directoryUri":"https://acme-staging-v02.api.letsencrypt.org/directory","domains":["www.example.com","*.example.com"],"contactEmails":["admin@example.com"],"enable":true}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read live, so it takes effect immediately -- no restart required."}
    ```

    The change shows in a read within a few seconds. In the console: **Presets & Settings**,
    **Integrations & Delivery**, **ACME / Certificates**. The page turns **Save** on only when you
    change something. The `domains` and `contactEmails` lists have no `NINLIL_*` variable. The other
    settings do, such as `NINLIL_DNS_SERVER_ACME_ENABLE` and `NINLIL_DNS_SERVER_ACME_DIRECTORY_URI`
    ([DNSSEC, ACME & CAA](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dnssec-acme--caa)).
2. List the domains and their certificates:

    ```bash
    curl -k https://127.0.0.1:18443/api/acme-settings/certificates -H 'Authorization: Bearer <token>'
    ```

    ```text
    [{"domain":"www.example.com","hasCertificate":false,"issuedAtUtc":null,"expiresAtUtc":null,"isDueForRenewal":true,"lastFailureAtUtc":null,"lastFailureMessage":null}, ...]
    ```

3. Ask for the first certificate now instead of waiting for the next check (Ninlil checks every 12
   hours). The call returns the domain's entry. A failure is not an HTTP error: it shows in the entry:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/acme-settings/certificates/www.example.com/issue -H 'Authorization: Bearer <token>'
    ```

    ```text
    {"domain":"www.example.com","hasCertificate":true,"issuedAtUtc":"...","expiresAtUtc":"...","isDueForRenewal":false,"lastFailureAtUtc":null,"lastFailureMessage":null}
    ```

    In the console, the page lists the domains with an **Issue/renew now** button. For a wildcard
    entry, quote the URL so that the shell does not expand the `*`:
    `'https://127.0.0.1:18443/api/acme-settings/certificates/*.example.com/issue'`.
4. Point the TLS listeners at the certificate file. Ninlil writes each certificate as
   `<domain>.pfx` in `data/acme-certificates` next to the executable, with an empty password. The
   name is lowercase, and a wildcard's `*.` becomes `_wildcard.`, so `*.example.com` gives
   `_wildcard.example.com.pfx`. Set the path for the web listener (the console, the API and
   DNS-over-HTTPS) and restart Ninlil once. Start it as in the Quick start, with the one extra
   variable. Replace `/path/to` with the folder that holds the executable:

    ```bash
    NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD='Change-Me-Now1!' \
    NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
    NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
    NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15300 \
    NINLIL_DNS_SERVER_WEB_CERTIFICATE_PFX_PATH='/path/to/data/acme-certificates/www.example.com.pfx' \
    ./ninlil-linux-amd64
    ```

    DNS-over-TLS and DNS-over-QUIC take their own `..._CERTIFICATE_PFX_PATH` variables, listed in the
    [DNS transports](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dns-transports)
    table.

## Check that it worked

Issuing a certificate needs a public domain and a reachable CA, so the checks below need both.

1. The domain's entry shows `"hasCertificate":true` and an `expiresAtUtc` in the future (the command
   of step 2).
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

## Good to know

- Before it asks the CA to check, Ninlil waits until its own zone and every server that may answer for
  the zone serve the challenge record. The wait is two minutes at most. On a timeout, Ninlil asks the
  CA anyway.
- A wildcard such as `*.example.com` is validated at its base name, so its challenge record is
  `_acme-challenge.example.com`.
- A background pass requests a certificate for each domain that has none or whose certificate expires
  within 30 days. A failed attempt shows in `lastFailureMessage` and is retried at the next pass.
- A TLS listener checks its certificate file every 5 minutes and loads a replaced file without a
  restart, but only while its path points at the ACME file.
- Ninlil keeps the account key and the state of each domain, encrypted, in `data/acme-account.json`.
- CAA records tell a CA whether it may issue for a name (RFC 8659). Ninlil can keep one in each
  `Primary` or `CloudPrimary` zone, and only when an issuer domain is set. Set it on a zone and ask
  for the record a few seconds later:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"caaIssuerDomain":"letsencrypt.org"}'
    dig @127.0.0.1 -p 15300 example.test CAA +noall +answer
    ```

    ```text
    example.test.		3600	IN	CAA	0 issue "letsencrypt.org"
    ```

    `POST /api/zones/<zone-id>/reconcile-caa` runs one pass on demand.
- The README holds the full rules:
  [Certificates (ACME)](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#certificates-acme).

## When something goes wrong

- *The entry shows `lastFailureMessage`.* Read it. With a made-up `.test` name the CA refuses the
  identifier. Use a domain that the CA can issue for.
- *The log says the domain is not a zone this server hosts.* Add the zone to Ninlil first. The domain
  must lie inside a `Primary` or `CloudPrimary` zone.
- *The log names servers that never served the challenge record.* A secondary or a provider did not
  receive the record in two minutes. Check the zone's `notifyAddresses`, the secondary's transfer and
  the cloud sync.
- *`404` from the issue call, with `AcmeDomain '<domain>' could not be found.`* The domain is not in
  the `domains` list. A change shows after a few seconds.
- *`400` "'bad_name' is not a valid domain name: it needs at least two labels."* An entry is a host
  name with at least two labels, optionally with a leading `*.`.
- *The entry shows `urn:ietf:params:acme:error:invalidContact`.* The CA refused a contact address.
  Fix `contactEmails`.
- *The CA rate limit refuses the request.* Use the staging directory while you test.

## Next

- [Encrypted transports and DNS64](encrypted-transports.md): use the certificate for DNS-over-TLS,
  DNS-over-HTTPS and DNS-over-QUIC.
- [Cloud DNS sync](cloud-dns-sync.md): a challenge record in a `CloudPrimary` zone reaches the
  provider at once.
