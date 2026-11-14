# Zones and records

This page shows how to add and change the records of a primary zone, accept dynamic updates from a
client such as `nsupdate`, and let Ninlil write the matching PTR records. A **zone** is one part
of the DNS name space that Ninlil answers for, stored as a list of **records**.

## Before you start

1. A running Ninlil and the `example.test` zone from the [Quick start](../../getting-started/quick-start.md),
   with its SOA record and the `www` A record. The commands use the Quick start values: the API on
   `https://127.0.0.1:18443` and DNS on port `15300`. Replace them if you chose other ports.
2. A signed-in API token. Step 4 of the Quick start prints it. A token is valid for 15 minutes by
   default
   ([`NINLIL_JWT_ACCESS_TOKEN_LIFETIME`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#identity-authentication--sso)).
   A request with an expired token answers `401` with no body: sign in again. `<token>` stands for
   the `accessToken` value in every command. `-k` accepts Ninlil's self-signed certificate.
3. A user that holds the `zones:read` and `zones:write` permissions. The bootstrap administrator
   holds every permission.
4. `dig` and `nsupdate` (your distribution's DNS utilities package provides both). Check them:

    ```bash
    dig -v
    nsupdate -V
    ```

5. Look up the zone's `id`. The commands call it `<zone-id>`:

    ```bash
    curl -k 'https://127.0.0.1:18443/api/zones?skip=0&take=100' -H 'Authorization: Bearer <token>'
    ```

    ```text
    {"items":[{"id":"<zone-id>","name":"example.test","type":"Primary", ...
    ```

    A page holds 50 zones unless you set `take` (500 at most). Raise `skip` to reach the next page.

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Steps

### Add and change records

1. Add an MX record and a CNAME record. A record's `name` is its full owner name. The name `@`
   stands for the zone's own name. Each record's `data` object has the fields of its type
   (`preference` and `exchange` for MX, `target` for CNAME, `values` for TXT). The console form
   shows the fields of the type you pick, and `/swagger` on your Ninlil lists them for every type.

    ```bash
    curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"example.test","type":"MX","timeToLiveSeconds":300,"data":{"preference":10,"exchange":"mail.example.test"}}'
    curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"web.example.test","type":"CNAME","timeToLiveSeconds":300,"data":{"target":"www.example.test"}}'
    ```

    ```text
    {"id":"<mx-record-id>","zoneId":"<zone-id>","name":"example.test","type":"MX","class":"IN", ...
    {"id":"...","zoneId":"<zone-id>","name":"web.example.test","type":"CNAME","class":"IN", ...
    ```

    Copy the MX record's `id` as `<mx-record-id>`. In the console: open **Zones**, press **Manage
    records** on the zone's row, press **Add record**, fill in the form and press **Save**.
2. To find the `id` of a record you did not just add, list the zone's records:

    ```bash
    curl -k 'https://127.0.0.1:18443/api/zones/<zone-id>/records?skip=0&take=100' -H 'Authorization: Bearer <token>'
    ```

3. Change the MX preference. A `PATCH` changes only the fields you send, but it replaces `data` as a
   whole, so send every field of the type:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id>/records/<mx-record-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"data":{"preference":20,"exchange":"mail.example.test"}}'
    ```

    The answer is `204` with no body.
4. To take a record out of service without deleting it, send `{"isDisabled":true}` in the same kind
   of `PATCH`. To delete it, send `DELETE` to the same address.

### Accept dynamic updates (optional)

A client can change a zone's records with an RFC 2136 dynamic update. Ninlil accepts an update only
for a `Primary` or `CloudPrimary` zone that opts in, and only when the update is signed with a TSIG
key that the zone lists.

1. Create a TSIG key. Leave out `secret` and Ninlil generates one. The answer shows the secret
   once; copy it as `<secret>`:

    ```bash
    curl -k https://127.0.0.1:18443/api/tsig-keys -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"update-key"}'
    ```

    ```text
    {"key":{"id":"...","name":"update-key","algorithmName":"hmac-sha256", ...},"secret":"<secret>"}
    ```

    In the console: **TSIG Keys**, **New key**. That page needs the `tsigkeys:write` permission.
2. Turn updates on for the zone and list the key. An empty list accepts no key:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"dynamicUpdateEnabled":true,"updateTsigKeyNames":["update-key"]}'
    ```

    In the console: edit the zone, open **Dynamic updates & transfers**, tick **Accept RFC 2136
    dynamic updates** and tick the key under **Keys allowed to send dynamic updates**.
3. Write an update file and send it, signed with the key:

    ```bash
    printf 'server 127.0.0.1 15300\nzone example.test\nupdate add dyn.example.test. 300 A 192.0.2.99\nsend\n' > update.txt
    nsupdate -y 'hmac-sha256:update-key:<secret>' update.txt
    ```

    `nsupdate` prints nothing when the update succeeds.

### Keep PTR records in step (optional)

Ninlil can write a PTR record for every A and AAAA record of a zone into the matching reverse zone.

1. Create the reverse zone and its SOA record. The reverse zone for `192.0.2.0/24` is
   `2.0.192.in-addr.arpa`. Copy the first answer's `id` as `<reverse-zone-id>`:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"2.0.192.in-addr.arpa","type":"Primary"}'
    curl -k https://127.0.0.1:18443/api/zones/<reverse-zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"2.0.192.in-addr.arpa","type":"SOA","timeToLiveSeconds":3600,"data":{"primaryNameServer":"ns1.example.test","responsiblePersonMailbox":"admin.example.test","serial":1,"refreshSeconds":3600,"retrySeconds":900,"expireSeconds":1209600,"minimumTtlSeconds":3600}}'
    ```

2. Turn PTR management on for `example.test`. A pass in the background writes the records within a
   few seconds:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"autoManagePtrRecords":true}'
    ```

    The answer is `204`. `POST /api/zones/<zone-id>/reconcile-ptr` runs one pass on demand.

## Check that it worked

1. Ask for the records you added:

    ```bash
    dig @127.0.0.1 -p 15300 example.test MX +noall +answer
    dig @127.0.0.1 -p 15300 web.example.test A +noall +answer
    ```

    ```text
    example.test.		300	IN	MX	20 mail.example.test.
    web.example.test.	300	IN	CNAME	www.example.test.
    www.example.test.	3600	IN	A	192.0.2.10
    ```

    The CNAME answer also carries the A record it points at.
2. The SOA serial rises by one for every accepted change, so you never edit it. Read it (your
   values differ):

    ```bash
    dig @127.0.0.1 -p 15300 example.test SOA +noall +answer
    ```

    ```text
    example.test.		3600	IN	SOA	ns1.example.test. admin.example.test. 5 3600 900 1209600 3600
    ```

3. After the dynamic update:

    ```bash
    dig @127.0.0.1 -p 15300 dyn.example.test A +noall +answer
    ```

    ```text
    dyn.example.test.	300	IN	A	192.0.2.99
    ```

4. After the PTR pass:

    ```bash
    dig @127.0.0.1 -p 15300 -x 192.0.2.10 +noall +answer
    ```

    ```text
    10.2.0.192.in-addr.arpa. 3600	IN	PTR	www.example.test.
    ```

## Good to know

- A zone you create has no records. Its first record must be its SOA record. Every accepted change
  afterwards advances the serial.
- A disabled record is kept but never answered. A disabled zone answers queries with `REFUSED` and updates with `NOTAUTH`.
- A record with `excludeFromSync` set stays on this server: zone transfers and cloud pushes never
  send it.
- Each zone lists its own TSIG keys: `updateTsigKeyNames` for updates and `transferTsigKeyNames` for
  transfers. A key is not valid for a zone that does not list it.
- PTR management works for `Primary` and `CloudPrimary` zones. `ptrIpv4PrefixLength` (default 24)
  sets the size of each reverse zone. The README lists the other zone types and the full rules:
  [Zones and records](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#zones-and-records).
- Settings: the PTR and NOTIFY defaults are in the
  [Authoritative DNS engine](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#authoritative-dns-engine)
  table and the TSIG clock tolerance is in the
  [TSIG](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#tsig) table.

## When something goes wrong

- *`401` with no body.* The token expired. Sign in again.
- *`400` "A zone's very first record must be its own SOA record."* Add the SOA record first. The
  Quick start shows the command.
- *`400` "'www' is not in zone 'example.test'; did you mean 'www.example.test'?"* A record name must
  be the zone's name or a name below it. Write the full name, or `@` for the zone's own name.
- *`nsupdate` prints `update failed: REFUSED`.* Ninlil verified nothing that lets the update in.
  Check, in this order: the zone has `dynamicUpdateEnabled` set to `true`; you passed a key with
  `nsupdate -y`; the key's name is in the zone's `updateTsigKeyNames`. A key that exists but is not
  listed for the zone gets `REFUSED`.
- *`nsupdate` prints `update failed: NOTAUTH(BADSIG)`.* The secret is wrong. Use the secret that the
  key creation answered.
- *`nsupdate` prints `update failed: NOTAUTH(BADKEY)`.* Ninlil has no key of that name. Check the
  name you pass to `nsupdate -y`.
- *`nsupdate` prints `update failed: NOTAUTH`.* The zone name in the update file is not a zone that
  Ninlil hosts (a disabled zone counts as not hosted), or the update names a class other than `IN`.

## Next

- [DNSSEC](dnssec.md): sign the zone.
- [Secondary zones and transfers](secondary-zones.md): copy the zone to another server.
- [Cloud DNS sync](cloud-dns-sync.md): keep the zone in step with a provider.
