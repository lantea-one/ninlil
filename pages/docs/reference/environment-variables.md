# Environment variables

[`ENVIRONMENT.md`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md) is the
only list of the `NINLIL_*` environment variables. It has one table row per variable with what it
controls and its default. This page tells you how to find a row and how to read it.

## Before you start

You know which behavior you want to change. If you only know the setting's name in
`appsettings.json`, such as `DnsServer:Web:HttpPort`, the [naming rule](#how-a-name-is-built) below
gives the variable.

## Steps

1. Pick the section of `ENVIRONMENT.md` that matches the subsystem:

    | Subsystem | Section |
    |---|---|
    | Storage engine and connection | [Storage engine](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#storage-engine) |
    | Sign-in, tokens, SSO, e-mail | [Identity, authentication & SSO](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#identity-authentication--sso) |
    | DNS transports and their ports | [DNS transports](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dns-transports) |
    | Zones, transfers, NOTIFY | [Authoritative DNS engine](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#authoritative-dns-engine) |
    | Recursion and validation | [Recursive resolver](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#recursive-resolver) |
    | Filtering, rate limiting, query log | [Filtering, rewrites, rate limiting & query log](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#filtering-rewrites-rate-limiting--query-log) |
    | DHCP, Router Advertisement, TFTP | [DHCP, DHCPv6, Router Advertisement & TFTP](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#dhcp-dhcpv6-router-advertisement--tftp) |
    | Web ports, reverse proxy, branding | [Web host](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#web-host-kestrel-reverse-proxy-branding) |
    | The key ring | [Data Protection key ring](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#data-protection-key-ring) |
    | Anything else | The [table of contents](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md#table-of-contents) |

2. Read the Default column. A value in code font is what the setting has when nothing sets it.
   `*(empty)*` is an empty string and `*(none)*` is no value at all. A `TimeSpan` default uses
   .NET's `[d.]hh:mm:ss` format: `1.00:00:00` is one day and `00:05:00` is five minutes.
3. Check the row for a startup rule. A few defaults are changed or filled in while Ninlil starts,
   for example the Identity password rules, the cluster node id and the JWT signing key. Those
   rows say so.
4. Set the variable in the environment of the Ninlil process
   ([Configuration](../configuration.md#change-a-setting-with-an-environment-variable)).
   A path shown as `data/...` is inside the `data` folder next to the Ninlil program files.

## How a name is built

A configuration path is a run of `:`-separated PascalCase segments. The variable name is `NINLIL_`
followed by every PascalCase word of every segment, uppercased and joined with `_`.
`DnsServer:Web:HttpPort` becomes `NINLIL_DNS_SERVER_WEB_HTTP_PORT`. `IPv4` and `IPv6` each count as
one word. ASP.NET Core's double-underscore form (`Section__Key`) is disabled and does nothing.
A list-valued setting has no variable. Reach it through `appsettings.json`, the REST API or the
admin console.

## Check that it worked

Start Ninlil with the variable set and look at the effect the row names. For a port, the log
lists it:

```bash
NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 ./ninlil-linux-amd64
```

```text
      Now listening on: http://[::]:18080
```

## Related

- [Ports](ports.md)
- [Configuration](../configuration.md)
