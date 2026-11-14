# DHCPv6 and prefix delegation

This page lets you hand out IPv6 addresses, and blocks of addresses (prefixes) for routers behind
the network, with Ninlil. You turn on DHCPv6, create a scope for the addresses and optionally a
scope for prefix delegation. DHCPv6 answers on UDP port `547` (RFC 9915). The README is the
[complete reference](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#dhcp-dhcpv6-and-router-advertisement).

## Before you start

1. A running Ninlil, a signed-in API token and the placeholder `<token>`, as in
   [DHCPv4 scopes, reservations and leases](dhcp.md) "Before you start". The commands use the
   Quick start's HTTPS port `18443`.
2. A user that holds `dhcpscopes:write`, `dhcpservices:manage` and `dhcpsettings:write`. The
   bootstrap administrator holds every permission.
3. A network where Ninlil may answer DHCPv6, and the right to open UDP port `547` (root or
   `CAP_NET_BIND_SERVICE`, see [Ports](../../reference/ports.md)). The scope and reservation rules
   in [DHCPv4 scopes, reservations and leases](dhcp.md) apply to DHCPv6 scopes too.

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Turn on DHCPv6 and create scopes

1. Start the service, then save the switch with the `dhcpv6-protocol` section (the variable is
   `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV6_ENABLE`):

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/dhcp-services/Dhcpv6/start -H 'Authorization: Bearer <token>'
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/dhcpv6-protocol -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

2. Create a scope for the addresses. A DHCPv6 scope has no gateway, because IPv6 routers announce
   themselves (see [Router Advertisement](router-advertisement.md)):

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-scopes -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"lan6","protocolVersion":"IPv6","rangeStartAddress":"fd00:50::1000","rangeEndAddress":"fd00:50::10ff","subnetPrefixLength":64,"defaultLeaseDuration":"08:00:00","maxLeaseDuration":"1.00:00:00","dnsServerAddresses":["fd00:50::2"]}'
    ```

3. Optional: delegate blocks of addresses to routers (prefix delegation). Create a scope of kind
   `PrefixDelegation`. The range is the pool of prefixes, `subnetPrefixLength` is the size of the
   whole pool and `delegatedPrefixLength` is the size of each block. This pool is
   `2001:db8:100::/48` and delegates `/56` blocks, 256 of them:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-scopes -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"name":"delegation","protocolVersion":"IPv6","kind":"PrefixDelegation","rangeStartAddress":"2001:db8:100::","rangeEndAddress":"2001:db8:100:ff00::","subnetPrefixLength":48,"delegatedPrefixLength":56,"defaultLeaseDuration":"08:00:00","maxLeaseDuration":"1.00:00:00"}'
    ```

A DHCPv6 reservation uses the device's DUID as `clientIdentifier`, for example
`00:03:00:01:aa:bb:cc:00:06:01`. A `PrefixDelegation` scope takes no reservations. DHCPv6 ignores
the lease times a device suggests: the lifetime is `defaultLeaseDuration` capped at
`maxLeaseDuration`. DHCPv6 Rapid Commit is off; read the README before you turn it on.

## Check that it worked

1. Check the service. `Dhcpv6` shows `"isRunning":true`:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-services -H 'Authorization: Bearer <token>'
    ```

    ```text
    [{"kind":"Dhcpv4", ... },{"kind":"Dhcpv6","enabled":true,"isRunning":true}, ...
    ```

2. List the leases (see "See the leases" in [DHCPv4 scopes, reservations and leases](dhcp.md)). A
   DHCPv6 device appears with `"protocolVersion":"IPv6"` and `"state":"Active"` after its request.

## When something goes wrong

- *A device gets no address.* The service is `isRunning` in `GET /api/dhcp-services`, the scope is
  enabled and has `availableSlots`, and with two or more enabled scopes a new local device needs a
  reservation or a relay ([DHCPv4 page](dhcp.md)).
- *The log has `could not bind its socket: Permission denied` or `Address already in use`.* See the
  bind-failure entry on the [DHCPv4 page](dhcp.md); start the service again with
  `POST /api/dhcp-services/Dhcpv6/start`.

Every `NINLIL_DHCP_*` variable is in [ENVIRONMENT.md](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dhcp-dhcpv6-router-advertisement--tftp).

## Next steps

- [Router Advertisement](router-advertisement.md): let devices find the router and build addresses.
- [Relay agent](relay.md): serve a network that Ninlil does not sit on.
- [DHCPv4 scopes, reservations and leases](dhcp.md).
- [README: DHCP, DHCPv6 and Router Advertisement](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#dhcp-dhcpv6-and-router-advertisement): the complete reference.
