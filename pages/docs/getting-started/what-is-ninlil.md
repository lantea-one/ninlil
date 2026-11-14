# What Ninlil is

Ninlil is a DNS, DHCPv4, DHCPv6 and Router Advertisement server suite on .NET 10, published under
the MIT license. It is built against public IETF RFCs and IANA registries, not against another
project's source code.

## One process, five parts

One program, `Ninlil.Web`, serves all of these. There are no separate services to connect.

| Part | What it does |
|---|---|
| DNS engine | Authoritative zones (primary, secondary, stub, forwarder, catalog), a recursive and validating resolver, forwarding with failover, filtering, rewrites and a query log |
| DHCP engine | DHCPv4 and DHCPv6 servers, relay-agent mode, Router Advertisement and a read-only TFTP server for PXE boot |
| REST API | Full create, read, update and delete for every resource, documented as OpenAPI |
| Event feed | A SignalR hub that pushes events to the console in real time |
| Admin console | An Angular web application served from the same port as the API |

DNS over plain UDP and TCP is on by default. Every other DNS transport (DNS-over-TLS,
DNS-over-HTTPS, DNS-over-QUIC, DNSCrypt) and both DHCP protocols are off until you turn them on.
The [ports table](../reference/ports.md) lists them all.

## What it is for

Ninlil combines work that is often split across three kinds of tool:

- an authoritative and recursive DNS server;
- a network-wide ad and tracker blocker with per-client filtering and parental controls;
- a DNS and DHCP forwarder with address management.

Other tools cover these categories too. Choose by the features you need.

## Where to go next

1. [Quick start](quick-start.md): run it on one machine in a few minutes.
2. [Installation](installation/index.md): install it for good.
3. The README lists every feature under
   [What's in the box](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#whats-in-the-box).
