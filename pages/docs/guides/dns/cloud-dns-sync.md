# Cloud DNS sync

This page shows how to keep a zone in step with a zone at a cloud DNS provider: Cloudflare, AWS
Route 53 or DigitalOcean. The zone type picks the direction:

- A `CloudPrimary` zone is the source of truth. Ninlil serves its records and pushes them to the
  provider.
- A `CloudSecondary` zone is a read-only copy. Ninlil pulls the provider's records and serves them.

The sync matches records by content, because the providers number their records themselves. It
handles the record types A, AAAA, CNAME, MX, NS, TXT, SRV and CAA. A **credential set** holds the
secrets for one provider account, and several zones can share it.

## Before you start

1. A running Ninlil, a signed-in API token and the placeholder `<token>`, as in
   [Zones and records](zones-and-records.md) "Before you start". The commands use the API on
   `https://127.0.0.1:18443`.
2. A user that holds the `cloudprovidercredentials:write` and `zones:write` permissions.
3. At the provider, a zone that already exists (Ninlil does not create it) and the zone's
   identifier. What counts as the identifier depends on the provider:

    | Provider (`providerType`) | `credentials` fields | `cloudProviderZoneId` |
    |---|---|---|
    | `Cloudflare` | `ApiToken` | the zone ID |
    | `Route53` | `AccessKeyId` and `SecretAccessKey` | the hosted zone ID |
    | `DigitalOcean` | `ApiToken` | the domain name |

4. Credentials that can list, create and delete DNS records in that zone. For a `CloudSecondary`
   zone, listing is enough.
5. A test zone at the provider first. Ninlil's tests use stand-in servers that follow each provider's
   documented API; the connectors are untested against a live account. A push also
   deletes the supported records at the provider that the Ninlil zone does not hold, so do not point
   a `CloudPrimary` zone at a production zone until you have read "Good to know".

## Steps

### Push a zone to a provider (`CloudPrimary`)

1. Create a credential set. The answer shows the secrets once. Copy the set's `id` as
   `<credential-id>`:

    ```bash
    curl -k https://127.0.0.1:18443/api/cloud-provider-credentials -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"my-cloudflare","providerType":"Cloudflare","credentials":{"ApiToken":"<provider-api-token>"}}'
    ```

    ```text
    {"credential":{"id":"<credential-id>","name":"my-cloudflare","providerType":"Cloudflare","isDisabled":false, ...},"secrets":{"ApiToken":"..."}}
    ```

    `<provider-api-token>` is the token that you create at the provider. In the console: **Cloud
    Credentials**, **New credential set**.
2. Create the zone. `cloudSyncInterval` is how often Ninlil syncs (`hh:mm:ss`; the default is five
   minutes). Replace `example.com` and `<provider-zone-id>` with your zone's name and the
   identifier from the table:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"example.com","type":"CloudPrimary","cloudProviderCredentialId":"<credential-id>","cloudProviderZoneId":"<provider-zone-id>","cloudSyncInterval":"00:05:00"}'
    ```

    ```text
    {"id":"<zone-id>","name":"example.com","type":"CloudPrimary", ...
    ```

    In the console: **Zones**, **New zone**, type **CloudPrimary**, then pick the credential under
    **Cloud provider credential** and type the provider's zone ID under **Cloud provider's own zone
    ID**.
3. Add the zone's SOA record, then its other records, as in [Zones and records](zones-and-records.md).
   The SOA record stays local: the provider keeps its own.
4. Wait for the next sync. Ninlil looks for due zones every 30 seconds, so a zone you just created
   syncs within about half a minute. Records that you add later wait for the zone's next interval.

### Copy a provider's zone (`CloudSecondary`)

5. Create the zone with the type `CloudSecondary` and the same two provider fields as in step 2.
6. Add an SOA record. Ninlil never copies the provider's SOA, so the zone needs its own before the
   first copy. Without it, the log says `seed it with its own SOA record`:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones/<zone-id>/records -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"example.com","type":"SOA","timeToLiveSeconds":3600,"data":{"primaryNameServer":"ns1.example.com","responsiblePersonMailbox":"admin.example.com","serial":1,"refreshSeconds":3600,"retrySeconds":900,"expireSeconds":1209600,"minimumTtlSeconds":3600}}'
    ```

7. Wait for the next sync. The zone's records then match the provider's.

A `CloudPrimary` zone also accepts RFC 2136 dynamic updates, with the same rules as a `Primary` zone.
Follow "Accept dynamic updates" in [Zones and records](zones-and-records.md).

## Check that it worked

Not run: the checks below need a provider account.

1. In the provider's dashboard, or with the provider's own API, the zone shows the records that you
   added to Ninlil (`CloudPrimary`), apart from the SOA and the apex NS records.
2. Ask one of the provider's name servers for a record that you added:

    ```bash
    dig @<provider-name-server> www.example.com A +noall +answer
    ```

3. For a `CloudSecondary` zone, ask Ninlil for a record that exists only at the provider. The command
   uses the Quick start DNS port:

    ```bash
    dig @127.0.0.1 -p 15300 <provider-only-name> A +noall +answer
    ```

## Good to know

- A push creates the supported local records that the provider lacks and deletes the supported
  records at the provider that the zone does not hold. A record you add directly at the provider is
  therefore removed by the next push.
- The sync never creates or deletes the SOA record or the apex NS records, on either side. It never
  touches a record marked `excludeFromSync`. Records of a type outside the eight above are served
  locally and never pushed.
- Secrets are encrypted at rest and shown only when you create a set. A zone whose credential set is
  disabled or missing is skipped with a warning.
- One failing zone does not stop the others. A failure raises a `CloudDnsZoneSyncFailed` event that
  a webhook can deliver.
- Ninlil keeps the time of each zone's last sync in memory, so after a restart every zone syncs once at
  once.
- There are no `NINLIL_*` variables for cloud sync. The interval, the credential and the provider zone
  ID are fields of each zone. The README holds the full rules, including Route 53 record sets:
  [Cloud DNS sync](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#cloud-dns-sync).

## When something goes wrong

- *The log says `Zone '<zone>' references a missing or disabled cloud DNS provider credential;
  skipping.`* Enable the credential set or pick another.
- *The log says `Zone '<zone>' is cloud-synced but has no credential or provider zone ID
  configured; skipping.`* Set `cloudProviderCredentialId` and `cloudProviderZoneId` on the zone.
- *The log says `Cloud DNS sync failed for zone '<zone>'.` followed by an HTTP error.* Read the last
  line of the error. A made-up provider zone ID gives `404 (Not Found)`. Check the credential, its
  permissions and the provider zone ID. The sync tries again at the next interval.
- *The log says `Zone '<zone>' is a new CloudSecondary zone with no local records yet`.* Add the zone's
  SOA record (step 6).
- *Records at the provider keep disappearing.* The zone is a `CloudPrimary` zone, so Ninlil is the
  source of truth. Add the record in Ninlil, or use a `CloudSecondary` zone if the provider is the
  source.

## Next

- [Certificates (ACME)](certificates-acme.md): a challenge record in a cloud zone is pushed at once.
- [Secondary zones and transfers](secondary-zones.md): serve the zone from more servers.
