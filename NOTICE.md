# Notices

Ninlil is an independent, from-scratch implementation of a DNS, DHCPv4, and DHCPv6
server suite with a REST API and Angular administration console. It is released in its
entirety under the [MIT license](./LICENSE).

## Relationship to Technitium DNS Server

Ninlil's feature set was scoped by looking at the publicly documented capabilities of
[Technitium DNS Server](https://technitium.com/dns/) -- for example, "supports primary,
secondary, and stub zones," "offers DNS-over-HTTPS," "signs zones with DNSSEC" -- as a
checklist of functionality worth supporting. It was **not** produced by copying, porting,
or otherwise deriving code from Technitium DNS Server or TechnitiumLibrary. Those projects
are licensed under GPL-3.0, a copyleft license that would require any derivative work to
also be distributed under GPL-3.0; because Ninlil is a clean-room implementation built
against public RFCs and independently designed data models, protocols, and APIs, no such
obligation applies, and Ninlil is free to use the permissive MIT license instead.

Technitium DNS Server and its authors are acknowledged here with respect and gratitude for
having built and open-sourced a full-featured DNS server that made a project like this one
worth attempting.

## Third-Party Components

Ninlil depends on the following open-source packages. Each remains under its own
license; none of them impose copyleft obligations on Ninlil itself.

| Package                                          | License              |
| ------------------------------------------------- | -------------------- |
| Microsoft.EntityFrameworkCore.\*                   | MIT                   |
| Npgsql.EntityFrameworkCore.PostgreSQL              | PostgreSQL License (MIT-style) |
| Pomelo.EntityFrameworkCore.MySql                   | MIT                   |
| MongoDB.Driver                                     | Apache-2.0            |
| YamlDotNet                                         | MIT                   |
| MimeKit                                            | MIT                   |
| Swashbuckle.AspNetCore.\*                          | MIT                   |
| IdentityModel                                      | Apache-2.0            |
| BouncyCastle.Cryptography                          | MIT                   |
| Sodium.Core (wraps the native libsodium library)   | MIT (libsodium itself: ISC) |
| AWSSDK.Route53                                     | Apache-2.0            |
| Certes (transitively: Portable.BouncyCastle, Newtonsoft.Json) | MIT (all three) |

This table is maintained by hand and grows alongside the solution's dependencies; see each
project's `.csproj` for the authoritative, exact set of package references in use.

## Third-Party Data Sources

Ninlil's curated "Blocked Services" catalog (`Ninlil.Dns.Server.Filtering.BlockedServices.BlockedServiceCatalog`)
fetches its own per-service domain lists at runtime, via this project's existing subscribed
filter-list mechanism, from:

| Source                                                          | License               |
| ----------------------------------------------------------------- | ---------------------- |
| [The Block List Project](https://github.com/blocklistproject/Lists) | The Unlicense (public domain); each individual list additionally states `License: MIT` |

The Block List Project is a community-maintained collection of domain blocklists, wholly
independent of Technitium DNS Server, AdGuard Home, and Pi-hole (no shared authorship, no
upstream/fork relationship with any of the three) -- verified directly against its own
repository before Ninlil's catalog was built, per this project's clean-room policy above.
Neither license requires attribution for this project's use (fetching the list from its own
origin, exactly as published), but it is credited here anyway because doing so honestly
reflects where the data actually comes from.

Ninlil's bundled Safe Search / parental-control catalog
(`Ninlil.Dns.Server.Rewrites.SafeSearch.SafeSearchCatalog`) is not a third-party data source
in this sense: every mapping it uses comes directly from each search engine's own public,
official DNS-based safe-search documentation (Google, Bing, DuckDuckGo, YouTube), not from
any third party's curated list -- see that class's own doc comment for the exact source page
cited for each entry.

Ninlil's MAC vendor lookup (`Ninlil.Dns.Server.ClientIdentity.Vendor`) uses the IEEE Registration
Authority's own public MAC address block registries, fetched directly from the IEEE:

| Source                                                                      | Registry |
| ----------------------------------------------------------------------------- | -------- |
| [IEEE MA-L (OUI) registry](https://standards-oui.ieee.org/oui/oui.csv)          | 24-bit   |
| [IEEE MA-M registry](https://standards-oui.ieee.org/oui28/mam.csv)              | 28-bit   |
| [IEEE MA-S registry](https://standards-oui.ieee.org/oui36/oui36.csv)            | 36-bit   |

A compact snapshot derived from these files (assignment prefixes and organization names only --
the published postal addresses are dropped) is embedded in the build
(`Vendor/Resources/mac-vendors.snapshot.br`, generated 2026-09-23), and running servers refresh it
from the same URLs. The IEEE publishes these registries freely for public lookup; the data is the
IEEE's, credited here as its source. The parser was written from the IEEE files' own format alone
-- no other project's OUI database or parsing code was consulted, per this project's clean-room
policy above.
