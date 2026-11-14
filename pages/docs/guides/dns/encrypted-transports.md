# Encrypted transports and DNS64

This page shows how to turn on the encrypted DNS transports and DNS64. Plain DNS over UDP and TCP
sends every query in the clear. Ninlil also answers over four encrypted transports. Each is off by
default and has its own switch:

| Transport | Standard | Default port | Notes |
|---|---|---|---|
| DNS-over-TLS (DoT) | RFC 7858 | `853` TCP | A TLS connection that carries DNS messages. |
| DNS-over-HTTPS (DoH) | RFC 8484 | the web HTTPS port (`443`), path `/dns-query` | Served by the same listener as the console and the REST API. |
| DNS-over-QUIC (DoQ) | RFC 9250 | `853` UDP | Needs the native library `libmsquic` 2.4.20 or later on the host. The container image ships it. |
| DNSCrypt v2 | the DNSCrypt v2 specification | `5443` UDP and TCP | Needs a provider name. Clients use a DNS Stamp (`sdns://...`) to find the server. |

**DNS64** helps a client that has only IPv6. For a name with an A record and no AAAA record, Ninlil
makes up an AAAA record by placing the IPv4 address inside an IPv6 prefix that a NAT64 gateway
translates (RFC 6147, RFC 6052). Skip it if you run no NAT64 gateway.

DoT, DoH and DoQ use a TLS certificate. With no setting, Ninlil makes a self-signed certificate for
the name `localhost`, valid for two years, and saves it as `protocol-tls-certificate.pfx` in the data
directory. DoT and DoQ share it. Clients refuse a self-signed certificate unless you tell them to
trust it, so use it to rehearse. For clients on your network, point the PFX path of the transport at
a certificate they trust, for example one from [Certificates (ACME)](certificates-acme.md). DoH uses
the certificate of the web listener, so set
[`NINLIL_DNS_SERVER_WEB_CERTIFICATE_PFX_PATH`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding)
for DoH.

## Before you start

1. A running Ninlil and a signed-in API token, with the Quick start values
   ([Forwarding and recursion](forwarding-and-recursion.md) "Before you start", step 1). `<token>`
   is the `accessToken` value. The examples ask for `www.example.test`, the record from the
   [Quick start](../../getting-started/quick-start.md).
2. A user that holds the `transportsettings:write` permission. DNS64 needs `dns64settings:write` and
   the PROXY step needs `reverseproxy:write`.
3. Free ports. The commands use `18853` for DoT and DoQ (they use TCP and UDP, so they do not
   collide) and `15443` for DNSCrypt. The Quick start ports are taken. Check one:

    ```bash
    ss -ltnu 'sport = :18853'
    ```

    Only the header line means the port is free.
4. `openssl`, `curl` and a `dig` that supports `+tls` (version 9.18 or later). Check them:

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

    Without a line, install it as the
    [installation guide](../../getting-started/installation/index.md) for your install type says.

## Steps

### Turn on DNS-over-TLS

1. Set the port, then switch the transport on. Set the port first: a transport reads its port when
   it starts. The switch takes effect at once, with no restart
   ([`NINLIL_DNS_SERVER_PROTOCOL_DNS_OVER_TLS_ENABLE`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dns-transports)
   is the matching variable):

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dns-over-tls -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"port":18853}'
    curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dns-over-tls -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

    ```text
    {"note":"Written to the configuration file, and the underlying transport listener was started or stopped to match -- this transport's enabled/disabled state took effect immediately, no restart required."}
    ```

    The first call answers with a note that the port is read when the listener starts. In the
    console: **Presets & Settings**, **Transports**.
2. Save Ninlil's self-signed certificate so that a client can trust it. The command connects, reads
   the certificate and writes it to `ninlil-cert.pem`:

    ```bash
    openssl s_client -connect 127.0.0.1:18853 -servername localhost </dev/null 2>/dev/null | openssl x509 -out ninlil-cert.pem
    openssl x509 -in ninlil-cert.pem -noout -subject -dates
    ```

    ```text
    subject=CN=localhost
    notBefore=Nov 13 02:54:00 2026 GMT
    notAfter=Nov 13 02:54:00 2028 GMT
    ```

### Turn on DNS-over-HTTPS

3. DoH needs no port, because the web listener serves it. Switch it on:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dns-over-https -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

    The URL path (`/dns-query`) and the largest request (65535 bytes) are the other two fields. A DoH
    client can add one more path segment, `/dns-query/<client-id>`, to choose a client group (see
    [Filtering](filtering.md), step 10).

### Turn on DNS-over-QUIC

4. Set the port, then switch the transport on:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dns-over-quic -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"port":18853}'
    curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dns-over-quic -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

### Turn on DNSCrypt

5. A DNSCrypt server needs a **provider name**, the DNS name under which clients find its
   certificates. Choose a name below a domain you control, for example `dnscrypt.example.net`. Ninlil
   publishes the certificates as TXT records at `2.dnscrypt-cert.<provider-name>`. You can write the
   name with or without the `2.dnscrypt-cert.` prefix. Set the name and the port, then switch the
   transport on:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dnscrypt -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"providerName":"dnscrypt.example.net","port":15443}'
    curl -k -X PATCH https://127.0.0.1:18443/api/transport-settings/dnscrypt -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

    With no provider name the switch still answers `200`, but the listeners do not start and the log
    says `DNSCrypt is enabled, but no ProviderName is configured`. If you set the name after you
    switched the transport on, restart the two listeners:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/services/dnscrypt-udp/restart -H 'Authorization: Bearer <token>'
    curl -k -X POST https://127.0.0.1:18443/api/services/dnscrypt-tcp/restart -H 'Authorization: Bearer <token>'
    ```

    Enabling DNSCrypt creates the server's identity, a key pair, in `dnscrypt-certificates.json` in
    the data directory. Back it up: clients pin the public key, so a different identity means every
    client must learn a different stamp. The variables are the `NINLIL_DNS_SERVER_PROTOCOL_DNS_CRYPT_*`
    rows of the
    [DNS transports](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dns-transports)
    table.
6. Make the DNS Stamp your clients import. This command builds one from the identity file. Replace
   `<data-directory>` with your data directory and `<server-address>` with the address and port that
   clients use, such as `192.0.2.1:5443`. The last argument is the provider name with its prefix:

    ```bash
    python3 -c 'import base64,json,sys; d,a,n=sys.argv[1:4]; k=base64.b64decode(json.load(open(d+"/dnscrypt-certificates.json"))["ProviderPublicKey"]); lp=lambda b:bytes([len(b)])+b; r=b"\x01"+bytes(8)+lp(a.encode())+lp(k)+lp(n.encode()); print("sdns://"+base64.urlsafe_b64encode(r).decode().rstrip("="))' <data-directory> <server-address> 2.dnscrypt-cert.dnscrypt.example.net
    ```

    ```text
    sdns://AQAAAAAAAAAADzEyNy4wLjAuMToyNTQ0MyDNs8vGASZOdinC8fpe_JNzOwESjMfYeKpSmdfLNF70iSQyLmRuc2NyeXB0LWNlcnQuZG5zY3J5cHQuZXhhbXBsZS5uZXQ
    ```

    Your stamp differs. The stamp has the layout of the DNS Stamps specification for a DNSCrypt
    server: the byte `0x01`, eight bytes of properties, then the address, the public key and the
    provider name, each with a length byte, in unpadded base64url.

### Put a PROXY-protocol load balancer in front of DNS-over-TLS (optional)

7. A TCP proxy in front of Ninlil hides the client's address. The proxy can send a PROXY protocol
   header (version 1 text or version 2 binary) at the start of each connection, so Ninlil sees the
   client address in access rules, filters, rate limits and the query log. Ninlil trusts the header
   only from the addresses you list. This example trusts one proxy at `10.0.0.5`. The setting needs
   a restart of Ninlil:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/reverse-proxy-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"dnsOverTls":{"enable":true,"knownProxies":["10.0.0.5"]}}'
    ```

    Use `knownNetworks` (for example `["10.0.0.0/24"]`) for a proxy whose address changes. In the
    console: **Presets & Settings**, **Reverse proxy**.

### Synthesize AAAA records (DNS64)

8. DNS64 works on the recursive path only. A forwarder's answer is relayed unchanged, so turn
   forwarding off while you use DNS64 (`{"enable":false}` to `/api/forwarding-settings`). Recursion
   must be on (see [Forwarding and recursion](forwarding-and-recursion.md)). Then switch DNS64 on
   ([`NINLIL_DNS_SERVER_DNS64_ENABLE`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#query-pipeline--cross-cutting-dns-behavior)):

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/dns64-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

    The default prefix is the Well-Known Prefix `64:ff9b::/96`. If your NAT64 gateway uses its own
    prefix, set it (`NINLIL_DNS_SERVER_DNS64_SYNTHESIS_PREFIX`):

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/dns64-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"synthesisPrefix":"2001:db8:64::/96"}'
    ```

    Ninlil accepts the RFC 6052 lengths `/32`, `/40`, `/48`, `/56`, `/64` and `/96` only. In the
    console: **Presets & Settings**, **DNS64**.

## Check that it worked

1. DNS-over-TLS. `+tls-ca` names the saved certificate and `+tls-hostname` the name inside it:

    ```bash
    dig @127.0.0.1 -p 18853 +tls +tls-ca=ninlil-cert.pem +tls-hostname=localhost www.example.test A +noall +answer
    ```

    ```text
    www.example.test.	3600	IN	A	192.0.2.10
    ```

    Always pass `+tls-ca` for a check. `dig` 9.20.30 answered without it and did not verify the
    certificate, so an answer without `+tls-ca` proves nothing about the certificate.
2. DNS-over-HTTPS. The `dns` parameter is the query in base64url. This one asks for `www.example.com`
   type A. The status line and the media type show a DNS answer:

    ```bash
    curl -s -i --cacert ninlil-cert.pem --resolve localhost:18443:127.0.0.1 'https://localhost:18443/dns-query?dns=AAABAAABAAAAAAAAA3d3dwdleGFtcGxlA2NvbQAAAQAB' | head -6
    ```

    ```text
    HTTP/2 200 
    content-type: application/dns-message
    date: Sat, 14 Nov 2026 02:58:21 GMT
    server: Kestrel
    cache-control: private, max-age=148
    content-length: 65
    ```

    The name `www.example.com` needs recursion or forwarding. `max-age` is the smallest TTL in the
    answer (RFC 8484 §5.1).
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
    `isRunning` is `false` the log says why, for example that `libmsquic` is missing. Test a query
    with a DoQ client of your choice.
4. DNSCrypt. A client finds the server by asking for the TXT record of the certificates. Two records
   answer, one for each cipher:

    ```bash
    dig @127.0.0.1 -p 15443 2.dnscrypt-cert.dnscrypt.example.net TXT +noall +comments | grep -E 'status|ANSWER:'
    ```

    ```text
    ;; ->>HEADER<<- opcode: QUERY, status: NOERROR, id: 42290
    ;; flags: qr; QUERY: 1, ANSWER: 2, AUTHORITY: 0, ADDITIONAL: 0
    ```

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
    ipv4.l.google.com.	60	IN	AAAA	64:ff9b::8efb:a366
    ipv4.l.google.com.	60	IN	AAAA	64:ff9b::8efb:a371
    ```

    Trimmed; your addresses differ. The last 32 bits of each synthesized address are the IPv4
    address. With a prefix of your own, the addresses start with it. The reply has no `ad` flag,
    because nobody signed the made-up records.

## Good to know

- Every transport beyond plain DNS is off by default, and each switch applies at once. A port, a
  certificate or a limit is read when the transport's listener starts: change it, then restart the
  listener with `POST /api/services/<name>/restart` (`dns-over-tls`, `dns-over-quic`,
  `dnscrypt-udp`, `dnscrypt-tcp`) or restart Ninlil. The [ports](../../reference/ports.md) page lists
  the defaults.
- Each transport can listen on chosen addresses with the `ListenAddresses` setting, for example to
  keep it off a WAN interface. DoH follows the web listener's addresses.
- DoQ requires message ID 0 (RFC 9250 §4.2.1). Ninlil closes the connection for another ID.
- A `libmsquic` older than 2.4.20 can abort the whole process when a peer advertises a 1200-byte UDP
  payload size over IPv4. The container image ships a fixed version.
- A DoH GET carries the query in the `dns` parameter and a POST carries it in the body with the media
  type `application/dns-message`. Another type answers `415`, and a bad `dns` parameter answers
  `400`.
- A DNSCrypt certificate is valid for 28 hours. Ninlil issues a replacement every 24 hours.
- DNS64 under the Well-Known Prefix leaves out IPv4 addresses that are not globally reachable (RFC
  6052 §3.1). It never synthesizes for `NXDOMAIN` or authoritative data, or when the A answer fails
  DNSSEC validation.
- The README holds the full rules:
  [Encrypted transports and DNS64](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#encrypted-transports-and-dns64).

## When something goes wrong

- *A client says `self-signed certificate`.* Pass `+tls-ca=ninlil-cert.pem` as in the check, or use a
  certificate your clients trust.
- *A client says the certificate name does not match.* The self-signed certificate names only
  `localhost`. Set `certificate.selfSignedSubjectAlternativeNames` to the name clients use, delete
  `protocol-tls-certificate.pfx` from the data directory and restart: a saved certificate is not
  made again when the names change. Or use a certificate for your name.
- *The switch answers `200` but nothing listens.* The log has the reason. For DoQ it names
  `libmsquic`. For DNSCrypt it names the missing provider name.
- *The log has an Error such as `DNS-over-TLS could not listen on :: port 853: Permission denied`.* An
  enabled transport could not bind its port. Ninlil keeps running, and `GET /status` shows the
  `transports` check as `Unhealthy`. Change the port with `PATCH /api/transport-settings/<transport>`
  or free it (`ss -lntup | grep ':<port>'` shows the program that holds it), then restart Ninlil.
- *A DNSCrypt client cannot find the server.* Check that the stamp's provider name has the
  `2.dnscrypt-cert.` prefix and that the address in the stamp reaches the DNSCrypt port.
- *The client address in the query log is the proxy.* The PROXY setting needs a restart, and the
  proxy's address must be in `knownProxies` or `knownNetworks`.
- *`400` "'2001:db8:64::/80' is not a usable DNS64 prefix ..."* Use one of the lengths in step 8.
- *DNS64 gives no AAAA record.* Forwarding is on and answered the query, the name has a real AAAA
  record, or the only A records are non-global addresses under `64:ff9b::/96`.

## Next

- [Certificates (ACME)](certificates-acme.md): a certificate your clients trust.
- [Forwarding and recursion](forwarding-and-recursion.md): forward to an upstream over TLS, HTTPS,
  QUIC or DNSCrypt.
- [Secondary zones and transfers](secondary-zones.md): transfer zones over TLS or QUIC.
