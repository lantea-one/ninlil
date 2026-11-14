# Filtering, rewrites and parental controls

This page shows how to block names with filter lists, answer a name with an address you choose and
give one device or group its own rules. All three apply to every transport.

- **Filtering** blocks or allows names. You collect names in **filter lists**. A list is a block
  list or an allow list. Lists come from you (**local** lists) or from a web address that Ninlil
  fetches on a schedule (**subscribed** lists). A list does nothing until you assign it to a
  **client group** or to a single **client**.
- **Rewrites** answer a name with an address or another name that you choose, instead of the
  answer the name would get.
- **Parental controls** are per-client and per-group pauses and time-of-day schedules, a catalog
  of blocked services and Safe Search. They sit on top of lists and rewrites.

A **client** is a device Ninlil knows by a MAC address (IPv4) or a DHCP unique identifier (IPv6),
not by an address that can change. A **client group** is a set of clients and networks that share
lists, schedules and rewrites.

## Before you start

1. A running Ninlil and a signed-in API token, with the Quick start values
   ([Forwarding and recursion](forwarding-and-recursion.md) "Before you start", step 1). `<token>`
   is the `accessToken` value.
2. A user that holds the `filteringsettings:write`, `filterlists:write`, `clientgroups:write`,
   `clients:write`, `rewritesettings:write` and `dnsrewrites:write` permissions.
3. Recursive service for your test client, so that names outside your zones resolve. Follow
   [Forwarding and recursion](forwarding-and-recursion.md), steps 1 to 4.
4. `dig`. Every check asks `dig @127.0.0.1 -p 15300`.

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Steps

### Block names with a list

1. Turn filtering on. It is off by default
   ([`NINLIL_DNS_SERVER_FILTERING_ENABLE`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#filtering-rewrites-rate-limiting--query-log)):

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/filtering-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read live, so it takes effect immediately -- no restart required."}
    ```

    In the console: **Presets & Settings**, **Filtering**.
2. Create a client group and make it the **default group**. A client that matches no other group
   gets the lists of the default group. Copy the answer's `id` as `<group-id>`:

    ```bash
    curl -k https://127.0.0.1:18443/api/client-groups -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"Everyone","isDefault":true}'
    ```

    ```text
    {"id":"<group-id>","name":"Everyone","isDefault":true,"isDisabled":false, ...
    ```

    Only one group is the default. In the console: **Client Groups**, **New client group**.
3. Create a local block list. Copy the `id` as `<list-id>`:

    ```bash
    curl -k https://127.0.0.1:18443/api/filter-lists -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"My blocks","kind":"Domain","action":"Block","source":"Local"}'
    ```

    ```text
    {"id":"<list-id>","name":"My blocks","kind":"Domain","action":"Block","source":"Local", ...
    ```

4. Add a name to the list. A plain name blocks the name and every name below it:

    ```bash
    curl -k https://127.0.0.1:18443/api/filter-lists/<list-id>/entries -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"value":"ads.example.org"}'
    ```

    ```text
    {"id":"...","listId":"<list-id>","value":"ads.example.org","comment":null,"kind":"Domain", ...
    ```

    Two other entry kinds exist, set with `"kind"`: `Wildcard`, a glob where `*` stands for any
    characters and `?` for one, and `Regex`, a .NET regular expression. Both match the whole queried
    name. Try a pattern before you save it:

    ```bash
    curl -k https://127.0.0.1:18443/api/filter-pattern-preview -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"kind":"Wildcard","value":"ads*.example.org","sampleDomains":["ads1.example.org","www.example.org"]}'
    ```

    ```text
    {"isValid":true,"error":null,"matches":[{"domain":"ads1.example.org","isMatch":true},{"domain":"www.example.org","isMatch":false}]}
    ```

5. Assign the list to the group:

    ```bash
    curl -k https://127.0.0.1:18443/api/client-groups/<group-id>/list-assignments -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"listId":"<list-id>"}'
    ```

    ```text
    {"id":"...","clientGroupId":"<group-id>","listId":"<list-id>", ...
    ```

    In the console: **Filter Lists**, **New filter list**, then add entries on the list. Open
    **Client Groups** and use the group's list assignments.

### Subscribe to a list that someone else maintains

6. A subscribed list needs the web address of a list file. Ninlil reads three formats: a hosts file
   (`0.0.0.0 tracker.example.org`), one domain per line, and the ad-blocking syntax
   (`||adblock.example.org^`, with `@@||allowed.adblock.example.org^` as an exception that cancels
   matches inside the same list). To try it without a third party, serve a small file from your own
   machine. Run these in a second terminal and leave the server running:

    ```bash
    mkdir -p lists
    printf '# a test list\n0.0.0.0 tracker.example.org\n||adblock.example.org^\n@@||allowed.adblock.example.org^\n' > lists/list.txt
    python3 -m http.server 18999 --bind 127.0.0.1 --directory lists
    ```

7. Create the subscribed list and assign it. `refreshInterval` is `hh:mm:ss` and at least one hour;
   leave it out for 24 hours. Copy the `id` as `<subscribed-list-id>`:

    ```bash
    curl -k https://127.0.0.1:18443/api/filter-lists -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"My subscription","kind":"Domain","action":"Block","source":"Subscribed","subscriptionUrl":"http://127.0.0.1:18999/list.txt","refreshInterval":"01:00:00"}'
    curl -k https://127.0.0.1:18443/api/client-groups/<group-id>/list-assignments -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"listId":"<subscribed-list-id>"}'
    ```

    Ninlil looks for due lists once a minute, so the first fetch happens within about a minute.

### Allow a name that a block list catches, and decide who wins

8. Create a local allow list (`"action":"Allow"`), add the name and assign it to the group, with the
   same three calls as steps 3 to 5. When a name is in a block list and an allow list for the same
   client, the `precedence` setting decides. The default is `AllowWins`. To let the block list win:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/filtering-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"precedence":"BlockWins"}'
    ```

### Choose what a blocked client sees

9. By default a blocked name answers `NXDOMAIN`. To answer with an address that goes nowhere instead
   (`0.0.0.0` for A, `::` for AAAA, with a TTL of 60 seconds), use sinkhole mode:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/filtering-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"blockResponseMode":"SinkholeAddress"}'
    ```

    To use other addresses, set `sinkholeAddressV4`, `sinkholeAddressV6` and
    `sinkholeTimeToLiveSeconds` in the same call.

### Give one group or one device its own rules

10. Add a second group and put networks in it. A `cidr` is a network (`192.168.1.0/24`) or one
    address. A `clientId` is a label that a DNS-over-HTTPS client puts in the URL,
    `https://<host>/dns-query/<client-id>`. A row sets either `cidr` or `clientId`, never both.
    Copy the first answer's `id` as `<kids-group-id>`:

    ```bash
    curl -k https://127.0.0.1:18443/api/client-groups -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"Kids"}'
    curl -k https://127.0.0.1:18443/api/client-groups/<kids-group-id>/networks -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"cidr":"192.168.1.0/24"}'
    curl -k https://127.0.0.1:18443/api/client-groups/<kids-group-id>/networks -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"clientId":"kids-tablet"}'
    ```

    Assign lists to this group as in step 5. A client that matches this group gets its lists, not
    those of the default group.
11. Register a device as a client, so that you can pause it, give it a schedule or assign lists to it
    alone. First tell Ninlil which MAC address belongs to the device's address, unless Ninlil's DHCP
    server or the ARP table already knows it. This example maps `127.0.0.1`, the address of your own
    test queries. The second answer starts with the client's `id`; copy it as `<client-id>`:

    ```bash
    curl -k https://127.0.0.1:18443/api/client-static-address-mappings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"cidr":"127.0.0.1","clientIdentifier":"aa:bb:cc:00:00:01","protocolVersion":"IPv4"}'
    curl -k https://127.0.0.1:18443/api/clients -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"clientIdentifier":"aa:bb:cc:00:00:01","protocolVersion":"IPv4","displayName":"Test laptop"}'
    ```

    In the console: **Clients**, **Static Address Mappings**.
12. Pause the client for 10 minutes. A pause blocks all of the client's queries, whatever lists,
    schedules and rewrites say. Calling it again replaces the pause:

    ```bash
    curl -k https://127.0.0.1:18443/api/clients/<client-id>/pause -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"durationMinutes":10}'
    ```

    `POST /api/clients/<client-id>/resume` ends the pause early (check item 5 below). A group has
    the same two calls under `/api/client-groups/<group-id>/`.
13. Add a time-of-day schedule. The window blocks queries from the start time to the end time in the
    named time zone, and an end time before the start time crosses midnight. `daysOfWeek` is a
    comma-separated list of day names:

    ```bash
    curl -k https://127.0.0.1:18443/api/clients/<client-id>/schedules -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"daysOfWeek":"Monday, Tuesday, Wednesday, Thursday, Sunday","startTime":"21:00:00","endTime":"07:00:00","timeZoneId":"Europe/Berlin"}'
    ```

    A group has the same call under `/api/client-groups/<group-id>/schedules`.

### Block a service or force Safe Search

14. The blocked services catalog turns a named service on or off with one call. Enabling it creates
    a subscribed list from a public list file. A call without `clientGroupId` or `clientId` blocks
    the service for every client. A call with one of them blocks it for that group or client only:

    ```bash
    curl -k https://127.0.0.1:18443/api/blocked-services -H 'Authorization: Bearer <token>'
    curl -k -X POST "https://127.0.0.1:18443/api/blocked-services/tiktok" -H 'Authorization: Bearer <token>'
    curl -k -X POST "https://127.0.0.1:18443/api/blocked-services/tiktok?clientGroupId=<group-id>" -H 'Authorization: Bearer <token>'
    ```

    The first call lists the catalog keys. The second blocks TikTok for every client, the third for
    one group. `DELETE` with the same query turns the same toggle off. The list file comes from a
    public web address, so Ninlil fetches it from the internet. In the console: **Presets &
    Settings**, **Blocked Services**.
15. Safe Search makes a search engine's own safe-search address the answer for its search host
    names. Enabling an entry turns rewrites on if they were off:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/safe-search/google -H 'Authorization: Bearer <token>'
    ```

    Add `?clientGroupId=<group-id>` or `?clientId=<client-id>` to limit it to one group or client. In
    the console: **Presets & Settings**, **Safe Search**.

### Answer a name with an address you choose (rewrite)

16. Turn rewrites on
    ([`NINLIL_DNS_SERVER_REWRITES_ENABLE`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#filtering-rewrites-rate-limiting--query-log)),
    then add rewrites. A target that is an IP address answers as an A or AAAA record. A target that
    is a name answers as a CNAME:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/rewrite-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    curl -k https://127.0.0.1:18443/api/dns-rewrites -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"domain":"nas.home.test","target":"192.168.1.20"}'
    curl -k https://127.0.0.1:18443/api/dns-rewrites -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"domain":"alias.home.test","target":"example.com"}'
    curl -k https://127.0.0.1:18443/api/dns-rewrites -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"domain":"*.dev.home.test","target":"192.168.1.30"}'
    ```

    ```text
    {"id":"...","domain":"nas.home.test","target":"192.168.1.20","targetKind":"A","isDisabled":false, ...
    ```

    In the console: **DNS Rewrites**, **New rewrite**.

    Turning rewrites on writes the setting to the configuration file, and Ninlil applies it when it
    reloads that file. A query sent in the first fraction of a second after the `PATCH` is not
    rewritten yet (it takes about 0.3 seconds). A rewrite saved while rewrites are on applies to the
    next query on the same server. A change made on another cluster node shows within 60 seconds.

## Check that it worked

1. Ask for the blocked name from step 4. In the default mode the answer is `NXDOMAIN`. After step 9
   it is an address that goes nowhere:

    ```bash
    dig @127.0.0.1 -p 15300 ads.example.org A +noall +comments +answer
    ```

    ```text
    ;; ->>HEADER<<- opcode: QUERY, status: NXDOMAIN, id: 45712
    ```

    ```text
    ;; ->>HEADER<<- opcode: QUERY, status: NOERROR, id: 1972
    ads.example.org.	60	IN	A	0.0.0.0
    ```

    A name on no list resolves as usual. Choose a name in a domain that you do not host: the Quick
    start zone `example.test` answers every name below it itself.
2. Ask for the subscribed list's names after the first fetch (about a minute):

    ```bash
    dig @127.0.0.1 -p 15300 tracker.example.org A +noall +answer
    dig @127.0.0.1 -p 15300 adblock.example.org A +noall +answer
    dig @127.0.0.1 -p 15300 allowed.adblock.example.org A +noall +comments
    ```

    In sinkhole mode the first two answer `0.0.0.0`. The third is not blocked, because the list's
    `@@` exception cancels the block. The Ninlil log shows how the fetch went:

    ```text
    Refreshed block/allow list 'My subscription' (3 entries, 1 of them exceptions; 0 unsupported rules skipped) from 'http://127.0.0.1:18999/list.txt'.
    ```

3. Ask for the rewrites from step 16:

    ```bash
    dig @127.0.0.1 -p 15300 nas.home.test A +noall +answer
    dig @127.0.0.1 -p 15300 alias.home.test A +noall +answer
    dig @127.0.0.1 -p 15300 x.dev.home.test A +noall +answer
    ```

    ```text
    nas.home.test.		300	IN	A	192.168.1.20
    alias.home.test.	300	IN	CNAME	example.com.
    example.com.		220	IN	A	104.20.23.154
    example.com.		220	IN	A	172.66.147.243
    x.dev.home.test.	300	IN	A	192.168.1.30
    ```

    The `example.com` addresses and TTL differ. The `alias.home.test` answer holds the CNAME and the
    addresses of its target.
4. After step 12, a query from the paused client is blocked. In sinkhole mode it answers
   `0.0.0.0`:

    ```bash
    dig @127.0.0.1 -p 15300 example.net A +noall +answer
    ```

    ```text
    example.net.		60	IN	A	0.0.0.0
    ```

5. Resume the client, so its queries are answered again:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/clients/<client-id>/resume -H 'Authorization: Bearer <token>'
    ```

6. Ask the API for the lists that apply to a group:

    ```bash
    curl -k https://127.0.0.1:18443/api/client-groups/<group-id>/list-assignments -H 'Authorization: Bearer <token>'
    ```

## Good to know

- A client gets the lists of every non-default group it matches. The default group applies only when
  no other group matches. The lists assigned to the registered client itself come on top.
- A plain domain entry blocks the name and every name below it. A rewrite matches one exact name:
  `nas.home.test` does not match `sub.nas.home.test`, and `*.dev.home.test` does not match
  `dev.home.test`.
- A subscribed list refreshes every 24 hours unless you set an interval, which is one hour at the
  least. A fetch that fails keeps the old entries.
- With `blockCnameCloaking` on (the default), an answer is also blocked when a CNAME target in it is
  on one of the client's block lists.
- Deleting a filter list removes its entries and every assignment of it. Deleting a client group or a
  client removes the networks, assignments, schedules and scoped rewrites that belong to it.
- The README holds the full rules, including the list kinds and what replicates in a cluster:
  [Filtering, rewrites and parental controls](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#filtering-rewrites-and-parental-controls).
  The variables are in the
  [Filtering, rewrites, rate limiting & query log](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#filtering-rewrites-rate-limiting--query-log)
  table. The lists, groups, schedules and rewrites themselves are data and have no variable.

## When something goes wrong

- *A list does not block.* A list does nothing until it is assigned. Check the assignment with
  `GET /api/client-groups/<group-id>/list-assignments`. Check that filtering is on and that the
  client matches the group you assigned it to. A client that matches a non-default group does not get
  the default group's lists.
- *`400` "'refreshInterval' must be at least 01:00:00, or null for the default of 24 hours."* Use one
  hour or more.
- *`409` "'My subscription' is a subscribed list; its entries are periodically replaced wholesale
  from ... and cannot be edited manually."* Edit a local list instead, or change the list's `source`
  to `Local` first.
- *A subscribed list shows no `lastFetchedAtUtc`.* Wait a minute and read the log. A fetch warning
  names the reason.
- *`400` on an entry.* The pattern does not compile, or the kind does not fit the list. Use the pattern
  preview in step 4.
- *A rewrite is ignored.* Rewrites are off (`GET /api/rewrite-settings`), a more specific rewrite
  wins, or the query type is not one the target answers.
- *A client keeps being blocked after you removed its list.* A pause or schedule still applies. Resume
  the client and list its schedules.

## Next

- [Forwarding and recursion](forwarding-and-recursion.md): the upstream behind your filters.
- [Encrypted transports and DNS64](encrypted-transports.md): give a DNS-over-HTTPS client its own
  `/dns-query/<client-id>` path.
