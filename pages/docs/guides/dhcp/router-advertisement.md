# Router Advertisement

This page lets IPv6 devices find the network's prefix, router and DNS server with Ninlil. Router
Advertisement (RA, RFC 4861 and RFC 8106) lets a device build its own address (SLAAC). You set what
Ninlil announces, add the prefix and start the service. The README is the
[complete reference](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#dhcp-dhcpv6-and-router-advertisement).

## Before you start

1. A running Ninlil, a signed-in API token and the placeholder `<token>`, as in
   [DHCPv4 scopes, reservations and leases](dhcp.md) "Before you start". The commands use the
   Quick start's HTTPS port `18443`.
2. A user that holds `dhcpsettings:write` and `dhcpservices:manage`. The
   bootstrap administrator holds every permission.
3. The right to open a raw ICMPv6 socket: `CAP_NET_RAW` or root. In a container, see
   [Docker](../../docker.md).

The examples call the API on `127.0.0.1` with `curl -k`, which skips the certificate check.
That is safe only on the machine that runs Ninlil; see [Self-signed certificate](../../getting-started/quick-start.md#self-signed-certificate).

## Advertise the network
Router Advertisement (RA) lets a device build its own IPv6 address and find its router. Ninlil
advertises nothing while the content is empty.

1. Set the content. This turns on the "O" flag (devices ask DHCPv6 for the rest), makes this host a
   default router for 30 minutes, sets the link MTU and announces the DNS server and search domain
   (RFC 8106):

    ```bash
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/ra -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"managedAddressConfiguration":false,"otherConfiguration":true,"routerLifetime":"00:30:00","linkMtu":1500,"recursiveDnsServers":["fd00:50::2"],"dnsSearchListDomains":["example.test"]}'
    ```

    ```text
    {"note":"Written to the configuration file. This setting is read live, so it takes effect immediately -- no restart required."}
    ```

    In the console: **Presets & Settings**, **DHCP**, the **Router Advertisement Content** card.
2. Add the prefix that devices build their addresses from. The API and the console cannot set
   prefixes: add them to `appsettings.json` ([Configuration](../../configuration.md)), next to any
   `Ra` settings step 1 saved. Ninlil reads the change at once:

    ```json
    {
      "DhcpServer": {
        "Ra": {
          "Prefixes": [ { "Prefix": "fd00:50::", "PrefixLength": 64 } ]
        }
      }
    }
    ```

3. Start the service and save the switch (the variable is `NINLIL_DHCP_SERVER_PROTOCOL_RA_ENABLE`):

    ```bash
    curl -k -X POST https://127.0.0.1:18443/api/dhcp-services/RouterAdvertisement/start -H 'Authorization: Bearer <token>'
    curl -k -X PATCH https://127.0.0.1:18443/api/dhcp-settings/ra-protocol -H 'Authorization: Bearer <token>' -H 'Content-Type: application/json' -d '{"enable":true}'
    ```

4. Read the content back. Wait a second after step 1 if the answer still shows the old values:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-settings/ra -H 'Authorization: Bearer <token>'
    ```

    ```text
    {"managedAddressConfiguration":false,"otherConfiguration":true,"routerLifetime":"00:30:00","currentHopLimit":64,"reachableTime":"00:00:00","retransTimer":"00:00:00","linkMtu":1500,"recursiveDnsServers":["fd00:50::2"], ...
    ```

On a Linux device in the network, `ip -6 address show dev <interface>` then lists an address in the
advertised prefix, and `ip -6 route show default` lists this host as the router when the router
lifetime is above zero. The README covers the advertisement interval, the size limits and the
per-interface overrides.

## Check that it worked

1. Check the service. `RouterAdvertisement` shows `"isRunning":true`:

    ```bash
    curl -k https://127.0.0.1:18443/api/dhcp-services -H 'Authorization: Bearer <token>'
    ```

    ```text
    [ ... ,{"kind":"RouterAdvertisement","enabled":true,"isRunning":true}, ...
    ```

2. On a device in the network, the address and route checks at the end of the steps above show the
   advertised prefix and router.

## When something goes wrong

- *No Router Advertisement arrives.* `GET /api/dhcp-settings/ra` must show content (a prefix, a
  router lifetime, a flag, an MTU, a DNS server or a route), the service must run and the process
  needs `CAP_NET_RAW` or root.
- *The log has `could not bind its socket: Permission denied`.* The process lacks `CAP_NET_RAW`.
  Ninlil keeps running. Fix it, then start the service again with
  `POST /api/dhcp-services/RouterAdvertisement/start`.

Every `NINLIL_DHCP_*` variable is in [ENVIRONMENT.md](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dhcp-dhcpv6-router-advertisement--tftp).

## Next steps

- [DHCPv6 and prefix delegation](dhcpv6.md): the "O" flag sends devices to DHCPv6 for the rest.
- [DHCPv4 scopes, reservations and leases](dhcp.md).
- [README: DHCP, DHCPv6 and Router Advertisement](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#dhcp-dhcpv6-and-router-advertisement): the complete reference.
