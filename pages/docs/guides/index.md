# Guides

One page for each task area. A page that is not written yet holds one line that says what it will
cover. The README holds the complete step-by-step text of every guide until the page is written:
[Guides](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#guides).

## DNS

| Guide | Use it to |
|---|---|
| [Zones and records](dns/zones-and-records.md) | Create zones and records |
| [DNSSEC](dns/dnssec.md) | Sign a zone and roll its keys |
| [Secondary zones and transfers](dns/secondary-zones.md) | Copy zones between servers |
| [Cloud DNS sync](dns/cloud-dns-sync.md) | Keep zones in step with a cloud provider |
| [Certificates (ACME)](dns/certificates-acme.md) | Get and renew TLS certificates |
| [Forwarding and recursion](dns/forwarding-and-recursion.md) | Answer queries for other zones |
| [Filtering, rewrites and parental controls](dns/filtering.md) | Block names and answer with your own |
| [Encrypted transports and DNS64](dns/encrypted-transports.md) | Serve DNS over TLS, HTTPS, QUIC or DNSCrypt |

## DHCP

| Guide | Use it to |
|---|---|
| [DHCPv4 scopes, reservations and leases](dhcp/dhcp.md) | Hand out IPv4 addresses, reserve one for a device, send options, read leases, name leased devices in DNS |
| [DHCPv6 and prefix delegation](dhcp/dhcpv6.md) | Hand out IPv6 addresses and prefixes |
| [Router Advertisement](dhcp/router-advertisement.md) | Let IPv6 devices find the prefix, router and DNS server |
| [Relay agent](dhcp/relay.md) | Forward the requests of one network to a DHCP server on another |
| [TFTP and PXE](dhcp/tftp-and-pxe.md) | Serve boot files |

## Security

| Guide | Use it to |
|---|---|
| [Security](security/security.md) | Protect the data directory and control access |
| [Single sign-on and e-mail secrets](security/sso-and-email-secrets.md) | Supply secrets from the environment |

## Operations

| Guide | Use it to |
|---|---|
| [Clustering, events and webhooks](operations/clustering.md) | Join servers and send events out |
| [Storage](operations/storage.md) | Choose or switch the database |
| [Backup and restore](operations/backup-and-restore.md) | Keep and recover the whole system |
| [Self-update](operations/self-update.md) | Learn about newer releases |

## More

[Using the admin console](../admin-console.md), the [REST API](../rest-api.md),
[Docker](../docker.md), [Upgrades](../upgrades.md) and [Troubleshooting](../troubleshooting.md)
have their own pages.
