# Ports

Ninlil serves several protocols from one process. Each has a default port and a setting that
changes it. Every setting is a row in the [environment variables](environment-variables.md).

## Default ports

| Port | Purpose | Setting | Default state |
|---|---|---|---|
| `80` | REST API, SignalR hub and admin console over HTTP | `DnsServer:Web:HttpPort` | Always on |
| `443` | The same over HTTPS | `DnsServer:Web:HttpsPort` | Always on |
| `443`, path `/dns-query` | DNS-over-HTTPS, on the web HTTPS port | `DnsServer:Protocol:DnsOverHttps:Enable` and `...:Path` | Off |
| `53` UDP and TCP | Plain DNS | `DnsServer:Protocol:Plain:Port` | On, the only transport that is |
| `853` TCP | DNS-over-TLS | `DnsServer:Protocol:DnsOverTls:Port` | Off |
| `853` UDP | DNS-over-QUIC | `DnsServer:Protocol:DnsOverQuic:Port` | Off. Needs `libmsquic` 2.4.20 or later on the host. The container image includes it |
| `5443` UDP and TCP | DNSCrypt v2 | `DnsServer:Protocol:DnsCrypt:Port` | Off |
| `67` and `68` UDP | DHCPv4 (server and client side) | `DhcpServer:Protocol:Dhcpv4:ServerPort` and `...:ClientPort` | Off |
| `547` and `546` UDP | DHCPv6 (server and client side) | `DhcpServer:Protocol:Dhcpv6:ServerPort` and `...:ClientPort` | Off |
| `69` UDP | TFTP, for PXE boot files | `DhcpServer:Tftp:Port` | Off |

The web and DNS listeners bind every address unless their `ListenAddresses` setting, for example
`DnsServer:Protocol:Plain:ListenAddresses`, names specific ones.

## Before you start

You know which of these transports you turned on. See
[Configuration](../configuration.md#turn-on-an-extra-transport) to turn one on.

## Steps: check which ports are free

1. On Linux, list the listeners on the ports you need:

    ```bash
    ss -lntu 'sport = :53'
    ```

    The command prints only a header line when the port is free. A listener means another program
    holds the port: stop it or give Ninlil another port. On a Linux desktop it is usually the
    `systemd-resolved` stub listener.
2. On Windows, find the holder with `netstat -ano | findstr :53` and look the process up with
   `Get-Process -Id <pid>`.

## Privileged ports

Ports below 1024 are privileged on Linux and macOS. Either run Ninlil with elevated privileges (on
Linux you can grant the process `CAP_NET_BIND_SERVICE` instead), or set unprivileged ports:

```bash
NINLIL_DNS_SERVER_WEB_HTTP_PORT=8080 \
NINLIL_DNS_SERVER_WEB_HTTPS_PORT=8443 \
NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=5300 \
dotnet run --project src/dotnet/src/Ninlil.Web
```

## Check that it worked

After the start, `ss -lntu` lists the ports you set. A DNS transport whose port is in use does not
stop Ninlil: the log names the transport and `GET /status` reports `Unhealthy`.

## Windows

On Windows, Ninlil claims each port exclusively, so no other program can share it. What Ninlil does
when a bind fails depends on the listener. The README describes each case under
[Ports](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#ports).
