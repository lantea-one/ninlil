# Relay agent

This page lets Ninlil forward the DHCP requests of one network to a DHCP server on another
network. Each protocol runs either as Ninlil's server or as a relay, never both. The README is the
[complete reference](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#dhcp-dhcpv6-and-router-advertisement).

## Before you start

1. A running Ninlil, a signed-in API token and the placeholder `<token>`, as in
   [DHCPv4 scopes, reservations and leases](dhcp.md) "Before you start". The commands use the
   Quick start's HTTPS port `18443`.
2. A user that holds `dhcpsettings:write` and `dhcpservices:manage`. The
   bootstrap administrator holds every permission.
3. A DHCP server on another network, with an enabled scope that covers the relay's address on the
   clients' network.

## Forward requests to an upstream server
A relay agent forwards the requests of its network to a DHCP server somewhere else. Each protocol
runs either as Ninlil's server or as a relay, chosen at start, never both.

1. On the host that acts as the relay, start Ninlil with `NINLIL_DHCP_SERVER_PROTOCOL_DHCPV4_ENABLE=true`
   and `NINLIL_DHCP_SERVER_RELAY_DHCPV4_ENABLE=true`.
2. Save the relay settings: the upstream servers, the relay's address on the clients' network
   (`giaddr`), and an optional circuit ID that the server can match:

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/relay -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"upstreamServerAddresses":["192.168.50.2"],"relayAgentAddress":"192.168.50.3","relayAgentCircuitId":"port-7"}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read once, at startup, so a restart is required before this change takes effect."}
    ```

3. Restart Ninlil. On the upstream server, an enabled scope must cover the relay address
   (`192.168.50.3` falls in the `lan` scope of the [DHCPv4 page](dhcp.md)).

The DHCPv6 relay works the same way with the `dhcpv6-relay` section
(`NINLIL_DHCP_SERVER_RELAY_DHCPV6_ENABLE`, the upstream addresses and `relayLinkAddress`). The relay
relays a reply only when its source address is one of the listed upstream addresses. With no
upstream address, it drops every request.

The script `src/script/development/dhcp-relay-two-link-test.sh` checks a relay with two real links.
It needs root, `tcpdump` and DHCP clients, and CI does not run it. See the README for its use.

## Check that it worked

1. Check that the service runs. `Dhcpv4` shows `"isRunning":true`:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-services -H 'Authorization: Bearer <token>'
    ```

2. On the upstream server, a device behind the relay appears in the lease list (see "See the
   leases" in [DHCPv4 scopes, reservations and leases](dhcp.md)) after it renews.

## When something goes wrong

- *The relay drops everything.* The log has `Dropping a DHCPv4 request this relay would otherwise
  forward: no upstream server addresses are configured`. Save `upstreamServerAddresses` and restart.
- *A relayed device gets no reply.* No enabled scope on the upstream server covers `giaddr`, or the
  upstream server answers from an address that is not in the list.

Every `NINLIL_DHCP_*` variable is in [ENVIRONMENT.md](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dhcp-dhcpv6-router-advertisement--tftp).

## Next steps

- [DHCPv4 scopes, reservations and leases](dhcp.md): the scope on the upstream server.
- [DHCPv6 and prefix delegation](dhcpv6.md).
- [README: DHCP, DHCPv6 and Router Advertisement](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#dhcp-dhcpv6-and-router-advertisement): the complete reference.
