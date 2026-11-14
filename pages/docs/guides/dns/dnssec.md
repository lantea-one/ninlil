# DNSSEC

This page shows how to sign a zone that Ninlil hosts, find the DS record that the parent zone needs
and roll a key. DNSSEC signing applies to the zones Ninlil originates (`Primary` and
`CloudPrimary`). Validation of answers from other servers is a separate feature: see
[Forwarding and recursion](forwarding-and-recursion.md).

A signed zone carries its public keys in a DNSKEY record set, an RRSIG record for each record set
and a chain of NSEC or NSEC3 records that proves which names do not exist. A zone uses two roles of
key:

- The **Zone Signing Key (ZSK)** signs the ordinary record sets.
- The **Key Signing Key (KSK)** signs the DNSKEY record set. The parent zone refers to it with a
  **DS record**, a fingerprint of the KSK. A validator finds your keys through that record.

## Before you start

1. A running Ninlil, a signed-in API token and the `example.test` zone with its SOA record, as in
   [Zones and records](zones-and-records.md) "Before you start". `<token>` and `<zone-id>` mean the
   same here.
2. A user that holds the `zones:read` and `zones:write` permissions.
3. `dig`. Check it with `dig -v`.
4. To publish the DS record for a zone that is not a child of a zone Ninlil hosts: a login to your
   registrar or to the parent zone's DNS provider.

## Steps

### Sign a zone

1. Mark the zone as signed:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"isDnssecSigned":true}'
    ```

    The answer is `204`. In the console: edit the zone, open **DNSSEC signing**, tick **Sign this
    zone with DNSSEC** and press **Save**. A zone you create uses NSEC3 with 0 extra iterations
    and no salt, which is the setting RFC 9276 §3.1 asks for.
2. Ninlil never creates keys by itself, so the zone is still served unsigned. Read its
   `dnssecServedUnsigned` field:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones/<zone-id> -H 'Authorization: Bearer <token>' | grep -o '"dnssecServedUnsigned":[a-z]*'
    ```

    ```text
    "dnssecServedUnsigned":true
    ```

3. Create the KSK. The server generates the key pair and never returns the private key. The answer
   holds the key's `id`, its `lifecycleState` (`Active`) and its DS record:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"role":"Ksk","algorithm":"ECDSAP256SHA256"}'
    ```

    ```text
    {"id":"<key-id>","zoneId":"<zone-id>","role":"KSK","algorithm":"ECDSAP256SHA256", ... "lifecycleState":"Active", ... "dsRecord":"example.test. IN DS 17650 13 2 E6B6541C...","dsDigest":"E6B6541C..."}
    ```

4. Create the ZSK. Ninlil signs the zone again at once:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"role":"Zsk","algorithm":"ECDSAP256SHA256"}'
    ```

    In the console, steps 3 and 4 are **Zones**, **Manage DNSSEC keys**, **Generate key**. The
    algorithms Ninlil signs with are `RSASHA256`, `RSASHA512`, `ECDSAP256SHA256`,
    `ECDSAP384SHA384`, `ED25519` and `ED448`. For an RSA key, `keySizeBits` is `0` (2048 bits) or a
    multiple of 64 from 2048 to 4096.

### Publish the DS record at the parent

5. Read the KSK's DS record. A ZSK has none, so its `dsRecord` is `null`:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys -H 'Authorization: Bearer <token>' | grep -o '"dsRecord":[^,]*'
    ```

    ```text
    "dsRecord":null
    "dsRecord":"example.test. IN DS 17650 13 2 E6B6541C372CAA924EDD5EA959A59DDD4A7BF1723BB1C7F57CFC00340405D321"
    ```

    The order of the keys can differ. In the console, the key list has a **DS and DNSKEY records**
    button on each KSK.
6. Enter the key tag, algorithm, digest type and digest at your registrar or at the parent zone's
   provider. Until the parent publishes the DS record, validators treat your zone as unsigned.

If Ninlil hosts the parent zone too, the parent publishes the child's DS record itself. Create the
child zone with its SOA record and keys as above, then add the delegation to the parent: an NS record
at the child's name. This example uses the parent `example.test` and the child `child.example.test`:

```bash
curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"child.example.test","type":"NS","timeToLiveSeconds":300,"data":{"nameServer":"ns.child.example.test"}}'
```

Nothing else is needed. Ninlil re-signs the parent when the child's set of `Active` KSKs changes.

### Roll a key (optional)

A rollover replaces a key without breaking validation. Ninlil follows RFC 6781: Pre-Publish for a ZSK
(§4.1.1.1) and Double-Signature for a KSK (§4.1.2).

7. Start the rollover of an `Active` key. `<key-id>` is the `id` of the key to replace. The answer
   describes the replacement key, which starts in `Published` for a ZSK and in `DoubleSignature`
   for a KSK:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys/<key-id>/roll -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{}'
    ```

    ```text
    {"id":"<replacement-key-id>","zoneId":"<zone-id>","role":"ZSK","algorithm":"ECDSAP256SHA256", ... "lifecycleState":"Published", ...
    ```

    Add `"algorithm":"ED25519"` to the body to change the algorithm in the same rollover. In the
    console, the key list has a **Start a rollover** button.
8. For a KSK whose parent Ninlil does not host, publish the replacement key's DS record at the
   parent (steps 5 and 6), then tell Ninlil. Ninlil cannot look at a registrar, so the rollover
   waits for this call:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys/<replacement-key-id>/confirm-parent-ds-updated -H 'Authorization: Bearer <token>'
    ```

    The answer is `204`. In the console, press **Confirm parent DS updated** on the replacement key.
9. Watch the states. Ninlil checks every 15 minutes and waits for the key's TTLs plus a one-hour
   propagation delay before each move:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones/<zone-id>/dnssec-keys -H 'Authorization: Bearer <token>' | grep -o -E '"role":"[A-Za-z]*"|"lifecycleState":"[A-Za-z]*"'
    ```

    ```text
    "role":"ZSK"
    "lifecycleState":"Active"
    "role":"ZSK"
    "lifecycleState":"Published"
    "role":"KSK"
    "lifecycleState":"Active"
    ```

    Each `role` line is followed by the `lifecycleState` of the same key. This is the list after a ZSK
    rollover began.

## Check that it worked

1. Ask for a record with the DO bit set. The answer carries an RRSIG record (your values differ):

    ```bash
    dig @127.0.0.1 -p 15300 www.example.test A +dnssec +noall +answer
    ```

    ```text
    www.example.test.	3600	IN	A	192.0.2.10
    www.example.test.	3600	IN	RRSIG	A 13 3 3600 20261214025207 20261114015207 18844 example.test. 4gudy2G9...
    ```

2. The zone publishes its keys: flag `257` marks a KSK and flag `256` a ZSK.

    ```bash
    dig @127.0.0.1 -p 15300 example.test DNSKEY +noall +answer
    ```

    ```text
    example.test.		3600	IN	DNSKEY	257 3 13 f2Z9PGTgQBVBKe0dwSOS52HWhGNVLIijLqV3uuBKERhyqOX3Y7MHuyb2 QNV1j8Y7QHBn+qNqPruZNj3Kz0Mosg==
    example.test.		3600	IN	DNSKEY	256 3 13 oyBF7Oa4W8lpAFizYUE7wjJVAETuRXZwqykodkiW98MRK4mKtVAa6Iuo iiZiFgDqZusLOTdbP8vS/RBN9aNqBw==
    ```

3. `dnssecServedUnsigned` is `false` (the command of step 2):

    ```text
    "dnssecServedUnsigned":false
    ```

4. With a parent that Ninlil hosts, the parent answers the DS question:

    ```bash
    dig @127.0.0.1 -p 15300 child.example.test DS +noall +answer
    ```

    ```text
    child.example.test.	3600	IN	DS	34631 13 2 529FE6C219854C802885EE8500C6BF3526D1550A4E6726DB65E00103 7A12DC6B
    ```

## Good to know

- A zone marked signed that has no key able to sign is served unsigned, and the log holds one
  warning. Ninlil signs it again as soon as it gets a usable key.
- Signatures are valid for 30 days. Ninlil signs again when a record or key changes and, in the
  background, 5 days before the earliest signature expires.
- A zone has one rollover per role at a time.
- A secondary zone holds no keys. It serves the DNSSEC records it received once you mark the
  secondary zone itself as signed. See [Secondary zones and transfers](secondary-zones.md).
- The timing settings and the full rules (algorithms Ninlil refuses, delegations, forcing a key
  state) are in the README:
  [DNSSEC](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#dnssec). The variables
  are in the
  [Authoritative DNS engine](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#authoritative-dns-engine)
  table.

## When something goes wrong

- *The answer has no RRSIG although the zone is marked signed.* Read `dnssecServedUnsigned`. When it
  is `true`, create a key (steps 3 and 4) or turn signing off.
- *`400` "Ninlil does not sign with 'RSASHA1' (RFC 9904 §3 marks it NOT RECOMMENDED for signing)."*
  Choose one of the six algorithms in step 4.
- *`400` "RSA key size must be a multiple of 64 between 2048 and 4096 bits; 0 means 2048."* Use a
  permitted size.
- *`400` "'151' is not a valid NSEC3 iteration count -- it must be between 0 and 150; RFC 9276
  recommends 0."* Use 0 to 150, and 0 unless you need more.
- *`409` "A Zsk rollover is already in progress for zone ..."* Wait for the running rollover.
- *A KSK stays in `DoubleSignature` for days.* The parent is not hosted here and nobody confirmed
  the DS record. Publish the DS record and call `confirm-parent-ds-updated` (step 8).
- *Validators call the zone bogus after a change at the registrar.* The parent's DS record does not
  match a published KSK. Compare the registrar's DS with the `dsRecord` of the `Active` KSK.

## Next

- [Secondary zones and transfers](secondary-zones.md): serve the signed zone from a second server.
- [Forwarding and recursion](forwarding-and-recursion.md): how Ninlil validates other zones.
