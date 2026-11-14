# Secondary zones and transfers

This page shows how to keep a copy of a zone on a second Ninlil. The copy is a **secondary zone**.
It follows the **primary** zone by zone transfer: AXFR (RFC 5936) copies the whole zone and IXFR
(RFC 1995) copies the changes since the serial the secondary holds. A TSIG key (RFC 8945) signs the
transfer, and a NOTIFY message (RFC 1996) tells the secondary about a change, so the copy follows
within seconds.

Ninlil plays both roles. As a primary it serves AXFR and IXFR for its `Primary` zones. As a secondary
it keeps `Secondary`, `SecondaryCatalog`, `SecondaryForwarder` and `Stub` zones.

## Before you start

1. A primary: the Quick start Ninlil with the zone `example.test`
   ([Zones and records](zones-and-records.md) "Before you start" explains `<token>` and `<zone-id>`).
   The commands use the Quick start values: the API on `https://127.0.0.1:18443`, DNS on port
   `15300`.
2. A second Ninlil as the secondary. To rehearse on one machine, copy the executable into a new empty
   folder. Each Ninlil keeps its data in a `data` folder next to its executable, so two Ninlil
   processes started from the same folder share one database. Start the copy on other ports. The
   transfer client reaches the primary on port 53 unless you set another port, so the secondary also
   gets the primary's DNS port:

    ```bash
    NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD='Change-Me-Now1!' \
    NINLIL_DNS_SERVER_WEB_HTTP_PORT=18081 \
    NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18444 \
    NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15301 \
    NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_PRIMARY_PORT=15300 \
    ./ninlil-linux-amd64
    ```

    Stop the primary (Ctrl+C) and start it again with one more variable, so that it sends NOTIFY to
    the secondary's DNS port instead of port 53. It keeps its data:

    ```bash
    NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD='Change-Me-Now1!' \
    NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
    NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
    NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15300 \
    NINLIL_DNS_SERVER_AUTHORITATIVE_NOTIFY_SECONDARY_PORT=15301 \
    ./ninlil-linux-amd64
    ```

    With two machines, skip the port variables: both servers use port 53 and the addresses below are
    the machines' own. Sign in to the secondary as in the Quick start, with port `18444`. Its token
    is `<secondary-token>` below.
3. `dig`, to read the copy and to start a transfer by hand.
4. Clocks on the two servers that differ by less than 300 seconds. A TSIG signature carries the time
   it was made, and a difference above the fudge (300 seconds by default) fails verification.

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Steps

### On the primary

1. Create a TSIG key. Leave out `secret` and Ninlil generates one. The answer shows the secret once;
   copy it as `<secret>`:

    ```bash
    curl -k https://127.0.0.1:18443/api/tsig-keys -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"xfer-key"}'
    ```

    ```text
    {"key":{"id":"...","name":"xfer-key","algorithmName":"hmac-sha256", ...},"secret":"<secret>"}
    ```

2. Allow that key to transfer the zone and list the secondary for NOTIFY. `notifyAddresses` holds
   the secondary's IP address, without a port:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"transferTsigKeyNames":["xfer-key"],"notifyAddresses":["127.0.0.1"]}'
    ```

    The answer is `204`. In the console: edit the zone, open **Dynamic updates & transfers**, type
    the address under **Notify addresses** and tick the key under **Keys allowed to transfer this
    zone (AXFR and IXFR)**.

### On the secondary

3. Create the same key: the same name, the same secret and the same algorithm (`hmac-sha256` is the
   default on both):

    ```bash
    curl -k https://127.0.0.1:18444/api/tsig-keys -H 'Authorization: Bearer <secondary-token>' -H 'Content-Type: application/json' -d '{"name":"xfer-key","secret":"<secret>"}'
    ```

4. Create the secondary zone. `primaryServerAddresses` lists the primaries in the order to try them.
   `tsigKeyName` is the key that signs the requests. `transferTransport` is `Plain` (the default),
   `Tls` or `Quic`:

    ```bash
    curl -k https://127.0.0.1:18444/api/zones -H 'Authorization: Bearer <secondary-token>' -H 'Content-Type: application/json' -d '{"name":"example.test","type":"Secondary","primaryServerAddresses":["127.0.0.1"],"tsigKeyName":"xfer-key"}'
    ```

    ```text
    {"id":"<secondary-zone-id>","name":"example.test","type":"Secondary", ...
    ```

    In the console: **Zones**, **New zone**, type **Secondary**, then fill **Transfer source** and the
    **Security** section's **TSIG key**.
5. Wait for the first transfer. The secondary looks for due zones every 15 seconds
   ([`NINLIL_DNS_SERVER_AUTHORITATIVE_SECONDARY_ZONE_REFRESH_POLL_INTERVAL`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#authoritative-dns-engine)),
   so wait up to half a minute.

## Check that it worked

1. The secondary answers from the copy:

    ```bash
    dig @127.0.0.1 -p 15301 www.example.test A +noall +answer
    ```

    ```text
    www.example.test.	3600	IN	A	192.0.2.10
    ```

2. The primary and the secondary report the same SOA serial (your values differ):

    ```bash
    dig @127.0.0.1 -p 15300 example.test SOA +short
    dig @127.0.0.1 -p 15301 example.test SOA +short
    ```

    ```text
    ns1.example.test. admin.example.test. 6 3600 900 1209600 3600
    ns1.example.test. admin.example.test. 6 3600 900 1209600 3600
    ```

3. A change on the primary reaches the secondary within seconds, through NOTIFY:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"notify.example.test","type":"A","timeToLiveSeconds":300,"data":{"address":"192.0.2.77"}}'
    dig @127.0.0.1 -p 15301 notify.example.test A +noall +answer
    ```

    ```text
    notify.example.test.	300	IN	A	192.0.2.77
    ```

4. Start a transfer by hand. `dig -y` signs the request:

    ```bash
    dig @127.0.0.1 -p 15300 example.test AXFR -y 'hmac-sha256:xfer-key:<secret>' +noall +answer
    ```

    ```text
    example.test.		3600	IN	SOA	ns1.example.test. admin.example.test. 7 3600 900 1209600 3600
    dyn.example.test.	300	IN	A	192.0.2.99
    example.test.		300	IN	MX	20 mail.example.test.
    ...
    example.test.		3600	IN	SOA	ns1.example.test. admin.example.test. 7 3600 900 1209600 3600
    ```

    The first and the last record are the zone's SOA record (your records and serial differ).
5. Without the key, or with a key the zone does not list, the primary refuses:

    ```bash
    dig @127.0.0.1 -p 15300 example.test AXFR +noall +comments
    ```

    ```text
    ;; Got answer:
    ;; ->>HEADER<<- opcode: QUERY, status: REFUSED, id: 35626
    ;; flags: qr; QUERY: 1, ANSWER: 0, AUTHORITY: 0, ADDITIONAL: 0

    ; Transfer failed.
    ```

## Good to know

- The primary accepts a transfer only over TCP, TLS or QUIC, with a TSIG signature that verifies,
  for the zone's apex, and with a key from the zone's `transferTsigKeyNames`. Anything else gets
  `REFUSED`.
- The secondary waits the SOA Refresh time of its copy, asks for the serial over UDP and transfers
  when the primary's serial is ahead. A NOTIFY from a configured primary makes it check at once.
  After a failure it retries after the SOA Retry time, and after the SOA Expire time without a
  success it answers `SERVFAIL` for the zone.
- A transfer applies only when it ends with the zone's closing SOA record, so a transfer that stops
  halfway changes nothing.
- A transfer over TLS (port 853) or QUIC (port 853) needs the primary's DNS-over-TLS or DNS-over-QUIC
  transport turned on ([Encrypted transports and DNS64](encrypted-transports.md)). The secondary
  checks the primary's certificate against the operating system's trust store, so a Ninlil primary
  with its self-signed certificate fails that check unless you set
  `NINLIL_DNS_SERVER_AUTHORITATIVE_ZONE_TRANSFER_CLIENT_VALIDATE_SERVER_CERTIFICATE=false`. With
  the check off, only TSIG protects the content.
- A secondary of a signed zone serves the DNSSEC records it received only after you mark the secondary
  zone itself as signed. The console does not show that switch for a secondary zone, so use the API:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18444/api/zones/<secondary-zone-id> -H 'Authorization: Bearer <secondary-token>' -H 'Content-Type: application/json' -d '{"isDnssecSigned":true}'
    ```

- A record with `excludeFromSync` set is left out of every transfer, and a transfer never deletes one
  on the secondary.
- The README holds the full rules, including the retry and expire details:
  [Secondary zones and transfers](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#secondary-zones-and-transfers).
  The settings are in the
  [Authoritative DNS engine](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#authoritative-dns-engine)
  and
  [TSIG](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#tsig) tables.

## When something goes wrong

- *`dig ... AXFR` answers `REFUSED`.* The request was unsigned, signed with a key that the zone does
  not list in `transferTsigKeyNames`, or sent over UDP.
- *The secondary log says `The SOA answer from <address> for zone <zone>. was rejected: it failed
  TSIG verification with key xfer-key`.* The two keys differ. The name, the secret and the algorithm
  must be identical on both servers. A key's secret cannot be changed: delete the key and create it
  again with the same secret.
- *A change on the primary reaches the secondary only after the Refresh time.* The NOTIFY did not
  arrive or was refused. The secondary accepts a NOTIFY only from an address in the zone's
  `primaryServerAddresses`, and the primary sends it only to addresses in `notifyAddresses`.
- *The secondary answers `SERVFAIL`.* The zone expired: no primary confirmed the copy for the SOA
  Expire time. Restore the link to a primary.
- *The secondary has the records but no RRSIG records.* Mark the secondary zone as signed (the command
  under "Good to know").

## Next

- [DNSSEC](dnssec.md): sign the zone on the primary.
- [Encrypted transports and DNS64](encrypted-transports.md): transfer over TLS or QUIC.
