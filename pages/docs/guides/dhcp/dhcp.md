# DHCPv4 scopes, reservations and leases

This page lets you hand out IPv4 addresses, routers and DNS servers on a network with Ninlil. You
turn on DHCPv4 and create a scope, reserve an address for one device, send extra options, read the
leases, and register the names of leased devices in DNS. DHCPv6, Router Advertisement and the relay
agent have their own pages: [DHCPv6 and prefix delegation](dhcpv6.md),
[Router Advertisement](router-advertisement.md) and [Relay agent](relay.md). The
[README section](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#dhcp-dhcpv6-and-router-advertisement)
is the complete reference; these pages carry each task from start to finish.

Words this page uses:

- A **scope** is one pool of addresses plus the settings that go with it: router, DNS servers and
  lease times. A scope is for IPv4 or for IPv6. A scope of kind `PrefixDelegation` hands out blocks
  of IPv6 addresses (prefixes) instead of single addresses.
- A **lease** is an address that Ninlil has granted to one device for a limited time.
- A **reservation** ties one address to one device.
- A **client identifier** is how Ninlil recognises a device: its hardware (MAC) address for
  DHCPv4, or its DHCP unique identifier (DUID) for DHCPv6.
- A **relay agent** forwards the requests of one network to a DHCP server on another network.

Four services take part, and each is off until you turn it on:

| Service | Answers | Default port |
|---|---|---|
| DHCPv4 | A device that asks for an IPv4 address, router and DNS server | UDP `67` |
| DHCPv6 | A device that asks for an IPv6 address or a block of addresses | UDP `547` |
| Router Advertisement (RA) | A device that builds an IPv6 address itself (SLAAC) | none, raw ICMPv6 |
| TFTP | A device that fetches a boot file ([TFTP and PXE](tftp-and-pxe.md)) | UDP `69` |

## Before you start

1. A running Ninlil and a signed-in API token, as in the [Quick start](../../getting-started/quick-start.md).
   The commands use the Quick start's HTTPS port `18443`. `<token>` is the `accessToken` that
   `POST /api/auth/login` returns.
2. A user that holds `dhcpscopes:write`, `dhcpscopes:read`, `dhcpleases:read`, `dhcpsettings:write`,
   `dhcpservices:manage`, `dhcptagrules:write` and `dhcpleases:manage`. The bootstrap administrator holds every
   permission.
3. A Linux host and a network where Ninlil may answer DHCP. Turn off any other DHCP server on that
   network first: two servers answer the same request, and a device takes either answer.
4. The numbers for your network. The commands use `192.168.50.0/24`: Ninlil on `192.168.50.2`, the
   router on `192.168.50.1`, and the pool `192.168.50.100` to `192.168.50.150`. Replace them with
   yours. Check the host's address:

    ```bash
    ip -4 -brief address
    ```

    ```text
    lo               UNKNOWN        127.0.0.1/8
    eth0             UP             192.168.50.2/24
    ```

    (Your values differ.) Ninlil's address on that network is the **Server Identifier** that a
    device names when it accepts an offer.
5. The right to open the ports. UDP port `67` is below `1024`: run Ninlil as root or grant
   `CAP_NET_BIND_SERVICE` (see [Ports](../../reference/ports.md)). Router Advertisement opens a raw
   socket and needs `CAP_NET_RAW` or root. In a container, DHCP needs host networking
   ([Docker](../../docker.md)).

You can create scopes, reservations and options without opening any port: the REST API only
stores them. A device gets an answer once the service for its protocol runs.

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Create a scope and turn on DHCPv4

1. Create the scope for your network. A duration is written `hours:minutes:seconds`, or
   `days.hours:minutes:seconds`, so `08:00:00` is eight hours and `1.00:00:00` is one day:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-scopes -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"lan","protocolVersion":"IPv4","rangeStartAddress":"192.168.50.100","rangeEndAddress":"192.168.50.150","subnetPrefixLength":24,"defaultLeaseDuration":"08:00:00","maxLeaseDuration":"1.00:00:00","gatewayAddresses":["192.168.50.1"],"dnsServerAddresses":["192.168.50.2"]}'
    ```

    ```text
    {"id":"<scope-id>","name":"lan","protocolVersion":"IPv4","rangeStartAddress":"192.168.50.100","rangeEndAddress":"192.168.50.150", ...
    ```

    Copy the `id`. The next commands call it `<scope-id>`. `subnetPrefixLength` `24` gives the
    subnet mask `255.255.255.0`. `defaultLeaseDuration` is the lease for a device that asks for
    none, and `maxLeaseDuration` caps what a device may ask for. In the console: **DHCP Scopes**.
2. Start the service. It runs until you stop it or restart Ninlil:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/dhcp-services/Dhcpv4/start -H 'Authorization: Bearer <token>'
    ```

    ```text
    {"kind":"Dhcpv4","enabled":false,"isRunning":true}
    ```

    `enabled` is the saved switch and `isRunning` is the running state. In the console:
    **DHCP Services**.
3. Save the switch, so the service starts with Ninlil. Either start Ninlil with
   `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE=true`, or save it through the API and restart:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/protocol -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read once, at startup, so a restart is required before this change takes effect."}
    ```

    In the console: **Presets & Settings**, **DHCP**, the **DHCPv4 Protocol** card.

The scope rules in short (the API answers `400` when one breaks):

- Both ends of the range lie inside one subnet of `subnetPrefixLength`.
- An IPv4 range does not include the network or the broadcast address of its subnet.
- Two enabled scopes of the same IP version and kind share no address. A disabled scope is not
  compared, so to move a range between two scopes, disable the old one first.

!!! warning "Two enabled scopes need a reservation or a relay"
    Ninlil listens on one socket for all networks and does not know which network a request came
    from. With two or more enabled scopes, a new device on the local network that has no
    reservation gets no reply. Put a reservation on the device, or serve the other network through
    a relay (below).

## Reserve an address for one device

1. Give a device with the hardware address `aa:bb:cc:dd:ee:10` the fixed address `192.168.50.20`.
   Ninlil accepts the hardware address as `aa:bb:cc:dd:ee:10`, `aa-bb-cc-dd-ee-10`,
   `aabb.ccdd.ee10` or `aabbccddee10` and stores it as `aa:bb:cc:dd:ee:10`:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-scopes/<scope-id>/reservations -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"clientIdentifier":"AA-BB-CC-DD-EE-10","ipAddress":"192.168.50.20","hostName":"printer"}'
    ```

    ```text
    {"id":"...","scopeId":"<scope-id>","clientIdentifier":"aa:bb:cc:dd:ee:10","ipAddress":"192.168.50.20","hostName":"printer", ...
    ```

    In the console: **DHCP Scopes**, the **Manage reservations** button of the scope.
2. Check that the scope holds it:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-scopes/<scope-id>/reservations -H 'Authorization: Bearer <token>'
    ```

    ```text
    {"items":[{"id":"...","scopeId":"<scope-id>","clientIdentifier":"aa:bb:cc:dd:ee:10","ipAddress":"192.168.50.20","hostName":"printer", ...}],"skip":0,"take":50,"totalCount":1}
    ```

A device with a reservation gets only its reserved address, and no other device gets that
address. The address may lie outside the pool, but its IP version must match the scope's.
`hostName` is a note for you: DNS registration uses the host name the device sends.

!!! note "Many devices send the identifier with a leading `01:`"
    A DHCPv4 device is known by the Client-identifier option (option `61`) when it sends one, and
    by its hardware address otherwise. Many devices send option `61` as the type byte `01` followed
    by the hardware address. Ninlil then keeps `01:aa:bb:cc:dd:ee:10`, and a reservation written
    with the plain hardware address does not match. Read the identifier in the lease list and use
    that.

## Send an option to every device of a scope

A custom option has a tag (the DHCP option number) and a value written in hexadecimal.

1. This sends the domain name (option `15`, plain text). `6C616E2E6578616D706C652E74657374` is
   `lan.example.test`:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-scopes/<scope-id>/custom-options -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"optionTag":15,"valueHex":"6C616E2E6578616D706C652E74657374"}'
    ```

    ```text
    {"id":"...","scopeId":"<scope-id>","optionTag":15,"valueHex":"6C616E2E6578616D706C652E74657374","isDisabled":false,"valueSource":"Custom", ...
    ```

    A `"valueSource":"ServerAddress"` (and no `valueHex`) sends Ninlil's address instead, for
    example as the time server (option `42`). In the console: **DHCP Scopes**, edit the scope,
    custom options.

DHCPv4 accepts tags `1` to `254` except `52`, `53` and `82`, which Ninlil or a relay sets. Option
`53` answers `400` with `Option tag 53 is reserved because the server or relay agent sets it itself;
it cannot be a custom option.` Tags `66` and `67` are text. The README lists the DHCPv6 limits.

## Send an option to some devices only

A **tag rule** sends options to the devices that match what they send, such as a vendor class.

1. This rule matches every device whose vendor class starts with `printer-`:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-tag-rules -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"printers","vendorClassIdentifierMatch":"printer-","vendorClassIdentifierMatchMode":"Prefix"}'
    ```

    ```text
    {"id":"<rule-id>","name":"printers","scopeId":null,"priority":0, ...
    ```

2. Add the option the rule sends. `C0A83201` is `192.168.50.1`:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-tag-rules/<rule-id>/options -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"optionTag":42,"valueHex":"C0A83201"}'
    ```

    ```text
    {"id":"...","tagRuleId":"<rule-id>","optionTag":42,"valueHex":"C0A83201","valueSource":"Custom", ...
    ```

    In the console: **DHCP Tag Rules**.

A rule with no `scopeId` applies in every DHCPv4 scope. Rules apply in ascending `priority`; the
last rule that sets a tag wins, and a rule's option beats a custom option with the same tag.

## See the leases

1. List the leases:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-leases -H 'Authorization: Bearer <token>'
    ```

    ```text
    {"items":[{"id":"...","protocolVersion":"IPv4","scopeName":"lan","clientIdentifier":"aa:bb:cc:00:00:01","ipAddress":"192.168.50.100","hostName":"laptop","state":"Active", ...
    ```

    (Your values differ.) A device appears with `"state":"Active"` after its request. With no
    request yet the answer is `{"items":[],"skip":0,"take":50,"totalCount":0}`. In the console:
    **DHCP Leases**.
2. See how full a scope is:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-scopes/<scope-id>/statistics -H 'Authorization: Bearer <token>'
    ```

    ```text
    {"scopeId":"<scope-id>","scopeName":"lan","protocolVersion":"IPv4","kind":"AddressAssignment","totalSlots":"51","leasedCount":0,"offeredCount":0,"reservedCount":1,"availableSlots":"51"}
    ```

    `totalSlots` and `availableSlots` are strings, because an IPv6 pool can hold more slots than a
    JSON number carries.
3. To end a lease early, or to end the quarantine of a declined address (the lease list shows it
   with the client identifier `declined:<address>`), release the row. The console button is **End
   the quarantine early**:

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/dhcp-leases/<lease-id>/release -H 'Authorization: Bearer <token>'
    ```

A device renews its lease before it ends. A lease that nobody renews expires within one minute of
its end (`NINLIL_DHCP_SERVER_CORE_LEASE_EXPIRY_SWEEP_INTERVAL`).

## Register leased names in DNS

A scope with a DNS zone gets an `A` record (DHCPv6: `AAAA`) for each leased device that sends a
host name. The zone must be a `Primary` or `CloudPrimary` zone ([Zones and
records](../dns/zones-and-records.md)) and you need `zones:write`.

1. Name the zone for the scope. `<zone-id>` is the zone's `id`:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-scopes/<scope-id> -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"dnsZoneId":"<zone-id>"}'
    ```

    The answer is `204` with no body.
2. After a device with the host name `laptop` holds a lease in the `example.test` zone, ask Ninlil
   for it (use your DNS port; `15300` is the Quick start port):

    ```bash
    dig @127.0.0.1 -p 15300 laptop.example.test A +noall +answer
    ```

    ```text
    laptop.example.test.	300	IN	A	192.168.50.100
    ```

The record has a TTL of 300 seconds. Ninlil removes it when the lease is released, declined or
expired. If the name holds a record that you entered, or belongs to another device's active lease,
Ninlil writes no record and logs a Warning on every renewal: `DHCP->DNS registration refused for
client <identifier> with host name <name>: owner name <fqdn> is unavailable because ...`. The
device keeps its lease.

## Check that it worked

1. Check the services:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-services -H 'Authorization: Bearer <token>'
    ```

    ```text
    [{"kind":"Dhcpv4","enabled":true,"isRunning":true},{"kind":"Dhcpv6","enabled":true,"isRunning":true},{"kind":"RouterAdvertisement","enabled":true,"isRunning":true}, ...
    ```

    A service that is off shows `"isRunning":false`.
2. On a device on the network, renew the lease the way the operating system does it, for example
   `sudo dhclient -v <interface>` on Linux or `ipconfig /renew` on Windows, then list the leases
   (see "See the leases" above): the device is `Active`.

## When something goes wrong

- *A device gets no address.* Work through the list: the service is `isRunning` in
  `GET /api/dhcp-services`; the scope is enabled (`"isDisabled":false`) and has `availableSlots`;
  with two or more enabled scopes, a new local device has a reservation or comes through a relay;
  the log has the reason when Ninlil refused the request, such as `No address is currently
  available in scope '<name>' to offer client <identifier>.`
- *The API answers `400` with `The range 192.168.50.120 to 192.168.50.130 overlaps scope 'lan'
  (192.168.50.100 to 192.168.50.150). Two enabled scopes of the same IP version and kind must not
  share an address; change the range or disable the other scope first.`* Change the range or
  disable the other scope.
- *The API answers `400` with `'fd00::1' is an IPv6 address, but scope 'lan' is a DHCPv4 scope and
  needs an IPv4 address.`* The reservation's address must match the scope's IP version.
- *Ninlil runs, but a DHCP service does not answer, and the log has an Error such as `The DHCPv4
  listener could not bind its socket: Permission denied`.* `Permission denied` means the process
  may not open the port: use root or `CAP_NET_BIND_SERVICE` (Router Advertisement needs
  `CAP_NET_RAW`). `Address already in use` means another program holds it
  (`ss -lnup | grep ':67 '` shows which). Ninlil keeps running and reports the `dhcpServices` check
  of `GET /status` as `Unhealthy` with the reason. Fix the cause, then start the service again with
  `POST /api/dhcp-services/Dhcpv4/start`. That call answers `500` while the cause remains.
- *A reservation is ignored.* The identifier does not match. Compare it with the one in the lease
  list, and look for the `01:` prefix of option `61`.
- *No DNS record for a device.* The scope needs a zone of type `Primary` or `CloudPrimary`, and the
  device must send a host name.
- *The relay or Router Advertisement does not answer.* See [Relay agent](relay.md) and
  [Router Advertisement](router-advertisement.md).

Which settings need a restart: the switches, ports and relay settings are read once at start. The
Router Advertisement content and the lease sweep interval apply without one. Every `NINLIL_DHCP_*`
variable is in [ENVIRONMENT.md](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dhcp-dhcpv6-router-advertisement--tftp).

## Next steps

- [DHCPv6 and prefix delegation](dhcpv6.md): addresses and prefixes for IPv6 devices.
- [Router Advertisement](router-advertisement.md): let IPv6 devices find the router.
- [Relay agent](relay.md): serve a network that Ninlil does not sit on.
- [TFTP and PXE](tftp-and-pxe.md): serve boot files and point a scope at them.
- [Zones and records](../dns/zones-and-records.md): the zone that holds the names of leased devices.
- [README: DHCP, DHCPv6 and Router Advertisement](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#dhcp-dhcpv6-and-router-advertisement):
  which scope serves a device, tag rule criteria and the full limits.
