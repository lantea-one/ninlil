# Forwarding and recursion

This page shows how to let Ninlil answer queries for names it does not host. It can find the answer
in two ways:

- **Forwarding.** Ninlil sends the query to one or more **forwarders**, DNS servers you name, and
  relays the answer.
- **Recursion.** Ninlil asks the servers of the DNS tree itself, starting at the root servers and
  following the delegations down to the server that holds the answer.

Both are off by default, because a server that answers anyone can be misused in an attack. A query
reaches them in this order: a name inside a zone Ninlil hosts is answered from the zone, otherwise
Ninlil tries forwarding if it is on, and if no forwarder answered it tries recursion if that is on.
Ninlil validates DNSSEC for forwarded and recursive answers.

## Before you start

1. A running Ninlil and a signed-in API token, as in [Zones and records](zones-and-records.md)
   "Before you start". The commands use the Quick start values: the API on
   `https://127.0.0.1:18443` and DNS on port `15300`. `<token>` is the `accessToken` value.
2. A user that holds the `recursivesettings:write`, `forwardingsettings:write`, `forwarders:write`,
   `zones:write` and `accesscontrol:write` permissions.
3. `dig`, and an upstream server that Ninlil can reach on UDP and TCP port 53. This page uses the
   public resolver Quad9 (`9.9.9.9` and `149.112.112.112`). Check that it answers from this host:

    ```bash
    dig @9.9.9.9 example.com A +short
    ```

    ```text
    104.20.23.154
    172.66.147.243
    ```

    Your addresses differ. No output and a `timed out` message mean a firewall blocks the path.

## Steps

### Allow recursive service

A client gets recursive service when its query has the RD (recursion desired) bit set, which every
stub resolver sets, and the client is **permitted recursion**. Ninlil answers any other client's
query for a name it does not host with `REFUSED`. Choose one of two ways.

1. To serve every client, turn recursion on
   ([`NINLIL_DNS_SERVER_RECURSIVE_ENABLE_RECURSION`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#recursive-resolver)).
   The change takes effect at once, but the configuration reload can take about a second:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/recursive-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enableRecursion":true}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read live, so it takes effect immediately -- no restart required."}
    ```

    In the console: **Presets & Settings**, **Recursive Resolver**, tick **Recurse for a query this
    server does not itself host** and save.
2. To serve only some clients, leave recursion off and add an access rule instead. The `cidr` is an
   address or a network such as `192.168.1.0/24`:

    ```bash
    curl -k https://127.0.0.1:18443/api/access-control-rules -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"Local network","cidr":"127.0.0.1","capabilities":["RecursionAccess"]}'
    ```

    ```text
    {"id":"...","name":"Local network","cidr":"127.0.0.1","capabilities":["RecursionAccess"],"isDisabled":false, ...
    ```

    In the console: **Access Control Rules**, **New rule**. The `dig` commands on this page come from
    `127.0.0.1`, so this rule serves them.

### Forward to global forwarders

**Global forwarders** are a list that Ninlil tries in order for every name.

3. Turn forwarding on:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/forwarding-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

    In the console: **Presets & Settings**, **Forwarding**. The variable is
    [`NINLIL_DNS_SERVER_FORWARDING_ENABLE`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#forwarding).
4. Add a forwarder. `definitionString` says where to send queries. `sortOrder` puts the forwarder in
   line: lower values go first:

    ```bash
    curl -k https://127.0.0.1:18443/api/forwarders -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"quad9","definitionString":"9.9.9.9","sortOrder":0}'
    curl -k https://127.0.0.1:18443/api/forwarders -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"quad9-secondary","definitionString":"149.112.112.112","sortOrder":1}'
    ```

    ```text
    {"id":"...","name":"quad9","definitionString":"9.9.9.9","protocol":"Plain","sortOrder":0,"isDisabled":false, ...
    ```

    In the console: **Forwarders**, **New forwarder**. The form offers well-known public resolvers in
    a list and a field for a definition you type. These are the forms of `definitionString`:

    | `definitionString` | Meaning |
    |---|---|
    | `9.9.9.9` or `9.9.9.9:53` | Plain DNS over UDP, with TCP when an answer is truncated. `udp://` and `tcp://` in front mean the same. |
    | `[2620:fe::fe]:53` | Plain DNS over IPv6. A port after an IPv6 address needs brackets. |
    | `tls://dns.quad9.net` | DNS-over-TLS, port 853 unless you add `:<port>`. The host name is also the name Ninlil expects in the server's certificate. |
    | `https://dns.quad9.net/dns-query` | DNS-over-HTTPS. |
    | `quic://dns.example.net` | DNS-over-QUIC, port 853 unless you add `:<port>`. Needs `libmsquic` 2.4.20 or later on this host. |
    | `sdns://...` | A DNSCrypt stamp. See [Encrypted transports and DNS64](encrypted-transports.md). |

    Ninlil checks the syntax when you save and never looks a host name up at that point. A definition
    it cannot parse answers `400` with the reason.

### Forward one domain to its own servers

A zone of the type `Forwarder` sends the names at and below its own name to its own list of servers
instead of the global list. Use one to send a private domain such as `corp.example` to the servers
that know it. The closest enabled zone above the queried name wins.

5. Create the zone. This example sends `corp.example` and every name below it to `192.0.2.53`. Use
   the address of a server you run. The forms are the ones in the table above:

    ```bash
    curl -k https://127.0.0.1:18443/api/zones -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"corp.example","type":"Forwarder","forwarderAddresses":["192.0.2.53"]}'
    ```

    ```text
    {"id":"<forwarder-zone-id>","name":"corp.example","type":"Forwarder", ... "forwarderAddresses":["192.0.2.53"], ...
    ```

    Copy the `id` as `<forwarder-zone-id>`. In the console: **Zones**, **New zone**, type
    **Forwarder**.
6. The zone has `validateDnssec` off, which is right for a private domain. Turn it on only for a
   domain that is signed under the public root:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/zones/<forwarder-zone-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"validateDnssec":true}'
    ```

### Let a private domain pass DNSSEC validation

7. DNSSEC validation is on by default. A private domain has no chain of trust from the public root,
   so validation fails for it. Put the domain on the Insecure domains list. A name at or below an
   entry skips validation and is answered without the AD bit. Saving a list replaces the built-in
   one, so repeat the built-in names you still need (`home.arpa` and `internal` here):

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/recursive-settings -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"insecureDomains":["corp.example","home.arpa","internal"]}'
    ```

    To go back to the built-in list, send `{"insecureDomains":null}`. The console has the list under
    **Presets & Settings**, **Recursive Resolver**, with a **Restore defaults** button.

### Look at the caches and flush them

8. List this node's DNS caches, with sizes and hit counts:

    ```bash
    curl -k https://127.0.0.1:18443/api/dns-cache -H 'Authorization: Bearer <token>'
    ```

    ```text
    [{"name":"dnssec","entryCount":5,"maxEntries":10000,"hits":2,"misses":5},{"name":"forwarded","entryCount":5,"maxEntries":10000,"hits":1,"misses":6},{"name":"recursive","entryCount":84,"maxEntries":100000,"hits":276,"misses":119}]
    ```

9. Forget one name in every cache, or empty every cache. Both need the `recursivesettings:write`
   permission and neither touches another cluster node. The answer is the number of entries removed
   (your counts differ):

    ```bash
    curl -k -X DELETE https://127.0.0.1:18443/api/dns-cache/entries/example.com -H 'Authorization: Bearer <token>'
    curl -k -X DELETE https://127.0.0.1:18443/api/dns-cache -H 'Authorization: Bearer <token>'
    ```

    ```text
    {"removedEntryCount":5}
    {"removedEntryCount":89}
    ```

## Check that it worked

1. Ask Ninlil for a name it does not host:

    ```bash
    dig @127.0.0.1 -p 15300 example.com A +noall +comments +answer
    ```

    ```text
    ;; ->>HEADER<<- opcode: QUERY, status: NOERROR, id: 22877
    ;; flags: qr rd ra ad; QUERY: 1, ANSWER: 2, AUTHORITY: 0, ADDITIONAL: 1
    example.com.		300	IN	A	104.20.23.154
    example.com.		300	IN	A	172.66.147.243
    ```

    (Trimmed to the lines that matter. Your numbers and addresses differ.) `NOERROR` and an answer
    mean Ninlil found the name. The flag `ra` (recursion available) shows the client is served.
    `ad` (authentic data) shows Ninlil validated the answer with DNSSEC. A name in an unsigned zone
    has no `ad`.
2. Ask again after a few seconds. The TTL counts down, because Ninlil answers from its cache.
3. For a conditional forwarder zone, ask for a name below it. A name that the zone's servers know
   gets their answer, without `ad` while `validateDnssec` is off:

    ```bash
    dig @127.0.0.1 -p 15300 intranet.corp.example A +noall +comments +answer
    ```

    ```text
    ;; flags: qr rd ra; QUERY: 1, ANSWER: 1, AUTHORITY: 0, ADDITIONAL: 1
    intranet.corp.example.	300	IN	A	192.0.2.53
    ```

4. A client that is not permitted recursion gets `status: REFUSED`. To see that, turn
   `enableRecursion` off, remove the access rules and ask again.

## Good to know

- Forwarders are tried in order. A forwarder has failed when it does not answer within 3 seconds or
  answers with any code except `NOERROR` and `NXDOMAIN`. Then Ninlil asks the next one. When every
  forwarder has failed, Ninlil falls back to recursion if the client may use it.
- The default failover strategy, `HealthCheckedRoundRobin`, takes turns among the healthy
  forwarders and leaves out one that failed three queries or probes in a row. `TryInOrder` always
  starts with the first.
- A forwarder given as a host name is looked up in the background, by the operating system's
  resolver, and a query never waits for it. On a server whose own system resolver is Ninlil, give
  the forwarders as addresses.
- A conditional forwarder zone replaces the global list for the names below it.
- With validation on, a forwarded answer that fails DNSSEC counts as a failure of that forwarder. A
  private name behind an unsigned conditional forwarder zone then gets the public DNS's `NXDOMAIN`
  unless the name is on the Insecure domains list.
- Recursion uses QNAME minimization (RFC 9156), serves an expired answer when servers are unreachable
  (RFC 8767) and keeps the root trust anchors up to date (RFC 5011). A network that blocks DNSSEC
  data needs `NINLIL_DNS_SERVER_RECURSIVE_ENABLE_DNSSEC_VALIDATION=false`, or clients get `SERVFAIL`.
- The README holds the full rules:
  [Forwarding and recursion](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#forwarding-and-recursion).

## When something goes wrong

- *Every query answers `REFUSED`.* The client is not permitted recursion. Turn on `enableRecursion`
  or add an access rule for the client's address, and wait a few seconds.
- *`dig` says `recursion requested but not available`.* The same cause: the reply carries no `ra`
  flag.
- *The answer is `SERVFAIL`.* Validation failed or no server answered. Read the Ninlil log for
  `Bogus` or `Forwarder` lines. If a private zone is the cause, add it to the Insecure domains list.
  If your network blocks DNSSEC data, turn validation off.
- *`400` "'[::1' is not a valid forwarder definition: '[::1' carries a malformed bracketed IPv6
  address."* Close the bracket: `[::1]:53`.
- *A forwarder you added seems ignored.* A forwarder earlier in the order, or a conditional zone,
  answers first. List the forwarders with `GET /api/forwarders` and look at `sortOrder`.
- *A cached answer is stale after you changed upstream data.* Forget the name with the commands in
  step 9.

## Next

- [Filtering, rewrites and parental controls](filtering.md): block or rewrite what clients ask for.
- [Encrypted transports and DNS64](encrypted-transports.md): forward over TLS, HTTPS, QUIC or
  DNSCrypt, and synthesize AAAA records.
