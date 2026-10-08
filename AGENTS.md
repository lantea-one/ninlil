# AGENTS.md

The guide for AI agents (and a fast orientation for people) working in this repository. It covers
how the code is laid out, how to build, test and format it, and the rules a change must follow.
For installation, operation and the release pipeline, see [`README.md`](./README.md).

## What Ninlil is

- A clean-room, MIT-licensed DNS, DHCPv4, DHCPv6 and Router Advertisement server suite on .NET 10.
- One process (`Ninlil.Web`) runs the DNS engine, the DHCP engine, the REST API (OpenAPI), a
  SignalR event hub and the Angular admin console, all on one port.
- Clean-room is binding: never read or port another DNS/DHCP/ad-blocking project's source. Protocol
  details come from IETF RFCs and IANA registries only. Match or exceed other products' features,
  but never copy their UI. See [`NOTICE.md`](./NOTICE.md).

## Repository layout

| Path | Holds |
|---|---|
| `Ninlil.slnx` | The solution (XML `.slnx` format); every .NET project and test project |
| `Directory.Build.props`, `global.json`, `.editorconfig` | Shared MSBuild settings (net10.0, nullable off, XML docs on), SDK pin, code style |
| `.config/dotnet-tools.json` | Local tools: `dotnet-ef`, `csharpier`, `reportgenerator` |
| `src/dotnet/src/` | Production projects, one per concern (see below) |
| `src/dotnet/tests/` | xUnit test projects, named `<Project>.Tests` |
| `src/angular/` | Angular 22 admin console, a separate npm workspace ([its README](./src/angular/README.md)) |
| `src/angular/openapi/swagger.json` | Committed OpenAPI snapshot the API client is generated from |
| `src/angular/src/app/api-client/` | Generated TypeScript client. Never edit by hand |
| `src/archlinux/` | Arch Linux packages ([README](./src/archlinux/README.md)) |
| `src/script/` | Operational scripts: per-engine database helpers, a systemd unit and installer |
| `src/terraform/` | Terraform modules for packaging and publishing |
| `Dockerfile`, `docker/`, `docker-compose.yml` | Container image, CI toolchain images, example deployment |
| `.forgejo/workflows/`, `.forgejo/scripts/` | CI/CD pipeline and the scripts it runs ([README](./README.md#the-pipeline)) |
| `ENVIRONMENT.md` | Registry of every `NINLIL_*` environment variable |

### .NET project families (`src/dotnet/src/`)

| Family | Projects | Role |
|---|---|---|
| Shared | `Ninlil.Common.Configuration`, `.Extension`, `.Serializer`, `.Console` | Env-var mapping, helpers, serialization. `.Console` is unused today and kept for a future API CLI: do not delete it |
| DNS core | `Ninlil.Dns.Common`, `Ninlil.Dns.Identity` | Wire format, records, enums; ASP.NET Core Identity, authentication, SSO, permissions |
| DNS storage | `Ninlil.Dns.Data.Abstractions`, `.EntityFrameworkCore`, `.EntityFrameworkCore.Migrations.{Sqlite,PostgreSql,MySql,SqlServer}`, `.MongoDb` | Entities, repository interfaces, EF Core and MongoDB implementations. The one EF model (`NinlilDnsContext`) also holds the DHCP entities |
| DNS transports | `Ninlil.Dns.Protocol.{Plain,DnsOverTls,DnsOverHttps,DnsOverQuic,DnsCrypt}` | Listeners for UDP/TCP, DoT, DoH, DoQ, DNSCrypt |
| DNS engine | `Ninlil.Dns.Server.Core` (pipeline, transports) and `Ninlil.Dns.Server.*` feature projects | Authoritative, Recursive, Dnssec, Tsig, Filtering, Rewrites, Forwarding, AccessControl, ClientIdentity, RateLimiting, QueryLog, Acme, Cluster, Backup, Webhooks, Events.* and so on |
| DHCP storage | `Ninlil.Dhcp.Data.{Abstractions,EntityFrameworkCore,MongoDb}` | DHCP entities and repositories (EF configurations live in `Ninlil.Dns.Data.EntityFrameworkCore`) |
| DHCP | `Ninlil.Dhcp.Common`, `Ninlil.Dhcp.Protocol.{Dhcpv4,Dhcpv6,Ra}`, `Ninlil.Dhcp.Server.{Core,Relay,DnsIntegration,Tftp,Ra}` | Wire formats and servers for DHCPv4/v6, RA, relay, DDNS, TFTP |
| Host | `Ninlil.Web` | `Program.cs` composition root, controllers, models, Swagger examples, SignalR hubs, `wwwroot/` |

Each runtime project wires itself into DI through a `ServiceCollection*Extension` class in its
`Extension/` folder. Every library except `Ninlil.Web` is also published as a NuGet package.

## Build

```bash
dotnet tool restore                                    # once: dotnet-ef, csharpier, reportgenerator
dotnet build Ninlil.slnx -p:NinlilSkipAngularBuild=true  # must report 0 warnings
dotnet run --project src/dotnet/src/Ninlil.Web           # SQLite by default, created and migrated on start
```

- Building `Ninlil.Web` normally also builds the Angular workspace (MSBuild target
  `NinlilBuildAngularAdminSpa`) and copies it into `wwwroot/`. That needs Node.js, npm and a Java
  runtime (the OpenAPI generator is a Java tool). Without npm on `PATH` it only warns.
- `-p:NinlilSkipAngularBuild=true` skips that target. Use it for backend-only work, and always when
  the API changes: the Angular build fails while the snapshot and the API disagree, so build with
  the flag first, then regenerate the snapshot and the client (below).

## Test

```bash
dotnet test src/dotnet/tests/Ninlil.Dns.Server.Core.Tests            # one project, explicit path
dotnet test src/dotnet/tests/Ninlil.Web.Tests --filter "FullyQualifiedName~SpaFallbackTests"
dotnet test Ninlil.slnx                                               # everything (slow)
```

- Name test projects by explicit path. In zsh an unmatched glob makes the whole command fail, so
  do not rely on wildcards such as `src/dotnet/tests/*Dhcp*`.
- `Ninlil.Dns.Server.QueryLog.Tests` starts PostgreSQL through Testcontainers, so it needs Docker.
- Tests use OS-assigned free ports, never fixed ones, and need no root.
- `Ninlil.Web.Tests`:
  - Every class joins `[Collection(WebTestCollection.Name)]` and shares one
    `NinlilDnsWebApplicationFactory` host. Never create a second host with `WithWebHostBuilder`: it
    throws `ObjectDisposedException`.
  - Data is shared across tests: users, zones and other rows created by other tests are visible.
    Never assume an empty table or an exact count; look up what you created (for example, page
    through a list until you find your own user).
  - `SpaFallbackTests` needs a built admin console in `wwwroot/`
    (`.forgejo/scripts/build-admin-console.sh`).
- IPv6 tests guard with `if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback)) return;` (the
  `Fakes/LocalAddresses.cs` helper in each transport test project).
- QUIC tests check `QuicListener.IsSupported` / `QuicConnection.IsSupported` and return early when
  QUIC (libmsquic) is missing, so they report as passed, not skipped.
- CI runs the timing-sensitive `DnssecKeyRolloverZskPrePublishLiveTests` alone, after the rest.

## Format and lint

| Side | Format | Check (CI gate) |
|---|---|---|
| C# | `dotnet csharpier format <changed files or folders>` | `dotnet csharpier check .` |
| Angular | `npm run format` (in `src/angular/`) | `npm run format:check`, `npm run lint` |

- Format only the C# files you changed. Running CSharpier on the whole tree reformats the
  generated EF migration files, which must stay exactly as `dotnet ef` wrote them.
- `.editorconfig` enforces file-scoped namespaces and `using` directives outside the namespace as
  errors.

## Frontend (`src/angular/`)

```bash
cd src/angular
npm ci
npm run build                                   # prebuild runs generate:api, then ng build
npm run lint && npm run format:check
npm test -- --watch=false --browsers=ChromeHeadlessNoSandbox   # set CHROME_BIN if needed
```

Standalone components, signals, zoneless, CoreUI. Feature pages live under
`src/app/features/<name>/` as list + form subfolders. See [README: adding a feature
page](./README.md#frontend-adding-a-feature-page).

## Changing the REST API

1. Change controllers/models under `src/dotnet/src/Ninlil.Web/` (with Swashbuckle examples under
   `Examples/`).
2. `dotnet build Ninlil.slnx -p:NinlilSkipAngularBuild=true`
3. Regenerate the snapshot (`OpenApiSnapshotTests` fails until you do):

   ```bash
   NINLIL_UPDATE_OPENAPI_SNAPSHOT=true dotnet test src/dotnet/tests/Ninlil.Web.Tests \
     --filter FullyQualifiedName~OpenApiSnapshotTests
   ```

4. Regenerate the client: `cd src/angular && npm run generate:api`.
5. Commit the snapshot and client together, separately from the backend change, then build the
   Angular side normally.

## Storage

| Engine | `Storage:Engine` | Schema |
|---|---|---|
| SQLite (default) | `Sqlite` | EF Core migrations in `Ninlil.Dns.Data.EntityFrameworkCore.Migrations.Sqlite` |
| PostgreSQL | `PostgreSql` | `...Migrations.PostgreSql` |
| MySQL | `MySql` | `...Migrations.MySql` |
| SQL Server | `SqlServer` | `...Migrations.SqlServer` |
| MongoDB | `MongoDb` | Schemaless (`Ninlil.Dns.Data.MongoDb`, `Ninlil.Dhcp.Data.MongoDb`), no migrations |

- Relational migrations are applied automatically at startup (`Database.MigrateAsync()` in
  `src/dotnet/src/Ninlil.Web/Program.cs`).
- Each provider keeps its migrations in its own assembly; never mix two providers in one.
- Adding a migration: change the entity and its `IEntityTypeConfiguration` in
  `Ninlil.Dns.Data.EntityFrameworkCore/Configuration/`, then run the same name once per provider
  from inside each migrations project (each has its own `DesignTimeDbContextFactory`, so no
  `--startup-project` is needed):

  ```bash
  for p in Sqlite PostgreSql MySql SqlServer; do
    (cd src/dotnet/src/Ninlil.Dns.Data.EntityFrameworkCore.Migrations.$p && dotnet ef migrations add AddYourChange)
  done
  ```

- Do not hand-edit or reformat generated migrations, `*.Designer.cs` or `NinlilDnsContextModelSnapshot.cs`.
- MongoDB: add or update the repository and class map; `AutoMap()` picks up new properties (do not
  call `MapIdProperty` on `Id`, it throws).
- Lists that can grow use a database-side `SearchAsync`/`CountAsync` pair, never `ListAsync()`
  sliced in memory. Worked end-to-end example: [README: adding a new
  entity](./README.md#backend-adding-a-new-entity-end-to-end).

## Configuration

- Options classes bind sections of `appsettings.json` (each has a `SectionName` constant, e.g.
  `DnsServer:QueryLogging`).
- Every setting is also a `NINLIL_*` environment variable derived from its config path: split
  PascalCase into words, uppercase, join with `_`, prefix `NINLIL_` (`IPv4`/`IPv6` stay one word).
  `DnsServer:Web:HttpPort` becomes `NINLIL_DNS_SERVER_WEB_HTTP_PORT`. The `Section__Key` form is
  disabled. List-valued settings have no variable.
- A new top-level options class must be added to
  `src/dotnet/src/Ninlil.Web/EnvironmentVariables/OptionsEnvironmentVariableRegistry.cs`.
- [`ENVIRONMENT.md`](./ENVIRONMENT.md) is the single registry of variables. Update it (row, default,
  and the totals at the top) whenever you add, rename or remove an option.

## Code conventions

- C#: file-scoped namespace preceded by the line `// Define our namespace.`; `using` directives at
  the top, outside it; nullable reference types off.
- Cancellation tokens are named `stoppingToken`.
- Private fields are camelCase without a leading underscore; explicit types unless the type is
  apparent (`.editorconfig`).
- Options classes, controllers and tests follow the shape of an existing sibling; copy it rather
  than invent a new one.
- Every type and public member has an XML doc comment (`///`); TypeScript uses TSDoc/JSDoc
  (`/** */`). The build has `CS1591` muted, so this is enforced by review, not the compiler.
- Test names read as sentences: `Method_Condition_ExpectedResult`, e.g.
  `Fallback_ForADeepLinkClientRoute_ServesIndexHtml`.

## Comment standard

Every comment is for a reader opening the file cold, today.

- Every type (class, record, struct, interface, enum, Angular component/service) has a summary:
  what it is, why it exists and how it fits (what calls it, what it calls, which setting or
  endpoint it serves).
- Public and protected members get a summary when the name does not say it all; document
  non-obvious parameters, return values and exceptions.
- Inline and private-member comments only where the code is not self-explanatory: the reason, an
  RFC rule (keep references like "RFC 2308 §5"), a platform quirk, a race, a security decision.
- Explain why, not what. Do not restate the code.
- Present tense, describing the system as it is now. No history: no "previously", "used to",
  "originally", dates or work-item names. A regression guard is a present-tense rule ("X must stay
  Y because Z").
- Plain English, short sentences, active voice, said once. No filler ("genuinely", "actually",
  "real" when not contrasting with a fake, emphatic "own"); no long em-dash chains.
- Every reference must be correct: each `<see cref>`, file, setting, route and permission named
  must exist and behave as described. A wrong comment is worse than none.
- Test classes say what they cover and how (real sockets, fake upstream, shared host). Comment
  surprising setup.
- Scripts (bash, YAML, Dockerfile, PKGBUILD) start with a header: what the file does, its inputs
  (arguments, environment) and outputs.
- No commented-out code. Generated code (migrations, `*.Designer.cs`, `api-client/`) is exempt.

## Commits and branches

- Commit message: one plain-English sentence, present tense, describing the behaviour change as a
  user or caller sees it. No prefixes (`feat:`, `fix:`), no scope tags. Examples from history:
  - `Zone transfers over TCP, TLS and QUIC are now rate limited and reach the query log`
  - `The OpenAPI snapshot and Angular API client now include maxConcurrentQueries`
- Small logical commits: backend change, snapshot + client, and UI change are separate commits.
- Branches: `feature/*` from `develop` → PR into `develop` → PR into `main` → release branch
  `v<yyyy>.<mm>.<dd>.<increment>`, per sub-phase; `hotfix/*` from `main`. Details: [README:
  branches](./README.md#branches) and [cutting a release](./README.md#cutting-a-release).
- Never push, and never commit secrets, unless the user explicitly asks.

## Before you finish

- [ ] `dotnet build Ninlil.slnx -p:NinlilSkipAngularBuild=true` reports 0 warnings, 0 errors.
- [ ] Tests for every project you touched pass (explicit `dotnet test <path>`), plus
      `Ninlil.Web.Tests` if the host, API or DI wiring changed.
- [ ] `dotnet csharpier format` run on the C# files you changed only; `npm run lint` and
      `npm run format:check` clean for Angular changes, and `npm test` for UI logic.
- [ ] API changed: OpenAPI snapshot regenerated and `npm run generate:api` run, both committed.
- [ ] Schema changed: one migration per relational provider, MongoDB repository updated.
- [ ] Option added or changed: `OptionsEnvironmentVariableRegistry` (new class) and
      `ENVIRONMENT.md` updated.
- [ ] Behaviour visible to users changed: `README.md` updated.
- [ ] Comments follow the comment standard and every reference in them is still true.
- [ ] Small, logical commits with one-sentence messages.
