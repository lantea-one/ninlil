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
| `src/dotnet/tests/` | xUnit test projects, named `<Project>.Tests`, plus `Ninlil.Dns.Data.EngineHosts`, a library (no test SDK) that starts the database containers the integration tests and the benchmarks share |
| `src/dotnet/benchmarks/` | `Ninlil.Benchmarks`, the BenchmarkDotNet project. CI builds it and never runs it (see [Benchmark](#benchmark)) |
| `src/angular/` | Angular 22 admin console, a separate npm workspace ([its README](./src/angular/README.md)) |
| `src/angular/openapi/swagger.json` | Committed OpenAPI snapshot the API client is generated from |
| `src/angular/src/app/api-client/` | Generated TypeScript client. Never edit by hand |
| `src/archlinux/` | Arch Linux packages ([README](./src/archlinux/README.md)) |
| `src/linux-packages/` | Deb and RPM package definition for nFPM and its install scripts (README: [Debian, Ubuntu, Fedora, RHEL and Rocky Linux](./README.md#debian-ubuntu-fedora-rhel-and-rocky-linux)) |
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
  Tests that need Docker carry `[Trait("Category", "RequiresDocker")]`. Pull-request CI has no
  Docker socket and runs with `--filter "Category!=RequiresDocker"`; add the trait to any new
  test that starts a container.
- `Ninlil.Dns.Data.Integration.Tests` also needs Docker: it runs the storage layer against real
  PostgreSQL, MySQL, SQL Server and MongoDB containers (one fixture per engine, a fresh database
  per test). Every class there that needs a container carries the `RequiresDocker` trait. The
  first run pulls the SQL Server image (about 1.5 GB, amd64 only). Add multi-engine storage tests
  there. Its SQLite tests need no Docker and carry no trait. Replication tests start primary and
  replica containers from `Ninlil.Dns.Data.EngineHosts`.
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

## Benchmark

`src/dotnet/benchmarks/Ninlil.Benchmarks` measures the database layer with BenchmarkDotNet. It is a
console program, not a test project: `dotnet test` skips it and the pipeline only builds it. Its
benchmarks live under `Storage/` (data-layer queries), `Dns/` (what the filter, rewrite, schedule and
forwarder lookups read from the database per query), `Dhcp/` (the DHCPv4 offer path), `Pipeline/`
(whole DNS queries through `DnsQueryProcessingPipeline`, plus the data set, host and fake upstream
they share), `Wire/` (the DNS wire format and `DnsName`), `Events/` (the event bus) and `Transport/`
(real UDP and TCP listeners on loopback).

```bash
dotnet run -c Release --project src/dotnet/benchmarks/Ninlil.Benchmarks -- \
  --filter '*QueryLog*' --engines Sqlite,PostgreSql --scale Large
```

- `--engines` picks from `Sqlite,PostgreSql,MySql,SqlServer,MongoDb` (default: all). The program
  starts a Docker container per engine (pinned images from `Ninlil.Dns.Data.EngineHosts`) and skips,
  with a message, an engine whose container cannot start. SQLite needs no Docker.
- `--connection PostgreSql="Host=...;Username=...;Password=..."` uses an existing server instead of a
  container (repeat for other engines; not for SQLite). The database name is replaced by the data
  set's (`ninlil_bench_<scale>_v<N>`). A database that already holds the data set is not seeded
  again, so a long-lived server pays the seed once.
- The `Pipeline/`, `Events/` and `Transport/` classes run on SQLite only, on a purpose-built data set
  of their own (`ninlil_bench_pipeline_<scale>_v1.sqlite3` in `--data-directory`, seeded on first
  use and reused). After warm-up the caches hold everything a query needs, so the engine does not
  matter, and each class proves it in its setup (the replies are the expected ones and 100 warm
  queries run zero database commands). They still need `--engines Sqlite` (the program refuses to
  start with no engine) and use a fake upstream resolver on loopback, never the internet:

  ```bash
  dotnet run -c Release --project src/dotnet/benchmarks/Ninlil.Benchmarks -- \
    --filter '*PipelineLatency*' --engines Sqlite --scale Small
  ```

  `Wire/` needs no database but is started the same way.
- `--scale Small|Large` (default `Small`). `Small` is about 1/100 of `Large` and proves that every
  benchmark runs; only `Large` numbers mean something. `Large` seeds 1,000,000 query-log entries,
  1,000 zones plus one of 50,000 records, three 100,000-entry filter lists and more, which takes
  minutes on a relational engine and longer on MongoDB.
- `--job short|default` (default `short`: one launch, 3 warm-ups, 10 iterations). `--data-directory`
  sets where the SQLite files go (default `BenchmarkDotNet.Artifacts/data`, git-ignored; a seeded
  file is reused). Every other argument (`--filter`, `--list flat`) goes to BenchmarkDotNet.
- Containers are removed when the run ends, so the next run seeds again. Copy results into the
  work-package notes; do not commit `BenchmarkDotNet.Artifacts/`.
- Changing the data-set generator (`Seeding/BenchmarkDataSet.cs`) means bumping its `Version`; the
  same holds for `Pipeline/PipelineDataSet.cs`.

## Format and lint

| Side | Format | Check (CI gate) |
|---|---|---|
| C# | `dotnet csharpier format <changed files or folders>`, or `dotnet csharpier format .` | `dotnet csharpier check .` |
| Angular | `npm run format` (in `src/angular/`) | `npm run format:check`, `npm run lint` |

- Formatting the whole tree is safe: `.csharpierignore` skips the generated EF migration folders
  (`Migrations/` in each `Ninlil.Dns.Data.EntityFrameworkCore.Migrations.*` project), which must
  stay as `dotnet ef` wrote them apart from the namespace conversion below. Never remove that
  entry or format those files by hand.
- Every C# file uses a file-scoped namespace (`namespace X;`), never a block (`namespace X { }`),
  generated migrations included. `.editorconfig` enforces it, and `using` directives outside the
  namespace, as errors in hand-written code; the lint job (`.forgejo/scripts/lint.sh`) fails on
  any tracked block-scoped namespace.

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

- Relational migrations are applied at startup by `RelationalStartupMigrator`
  (`Ninlil.Dns.Data.EntityFrameworkCore/Maintenance/`), which `Program.cs` calls; never call
  `Database.MigrateAsync()` from `Program.cs` or a new host. The migrator refuses a database that a
  newer version upgraded, takes a snapshot first (`Storage:MigrationSafety`) and exits with code
  78 through `DatabaseStartupRefusedException`, which `Program.cs` catches. Throw that exception for
  any other reason a start must stop on purpose. Operator view:
  [README: upgrades and migration snapshots](./README.md#upgrades-and-migration-snapshots).
- Each provider keeps its migrations in its own assembly; never mix two providers in one.
- Adding a migration: change the entity and its `IEntityTypeConfiguration` in
  `Ninlil.Dns.Data.EntityFrameworkCore/Configuration/`, then run the same name once per provider
  from inside each migrations project (each has its own `DesignTimeDbContextFactory`, so no
  `--startup-project` is needed):

  ```bash
  for p in Sqlite PostgreSql MySql SqlServer; do
    (cd src/dotnet/src/Ninlil.Dns.Data.EntityFrameworkCore.Migrations.$p && dotnet ef migrations add AddYourChange)
  done
  src/script/development/file-scoped-namespaces.sh src/dotnet/src/Ninlil.Dns.Data.EntityFrameworkCore.Migrations.*/Migrations
  ```

  `dotnet ef` writes block-scoped namespaces; the script converts the new migration, Designer and
  snapshot files to file-scoped ones and leaves already converted files alone.
- Do not hand-edit or reformat generated migrations, `*.Designer.cs` or
  `NinlilDnsContextModelSnapshot.cs`. The one exception: when a change alters how existing rows are
  stored (for example a date column moves to ticks), add the data-conversion
  `migrationBuilder.Sql(...)` to `Up` and `Down` of the generated migration file, for every
  provider that needs it (see `*ConvertDhcpLeaseTimesToTicks.cs`). Never touch the Designer or the
  snapshot.
- MongoDB: add or update the repository and class map; `AutoMap()` picks up new properties (do not
  call `MapIdProperty` on `Id`, it throws).
- MongoDB has no migrations; `MongoStartupGuard` records one document per applied step in
  `NinlilDnsSchemaSteps` and refuses a database that holds a step it does not know. Add a step to
  `MongoSchemaSteps.Known` whenever you add a backfill or change a document shape in a way an older
  build would misread. Never rename or remove a step.
- Read replicas: a controller action reads from a replica only through `[ReadFromReplica]` on a GET
  action; background code that only reads uses `CreateReplicaReadScope()`. Anything that writes, or
  reads and then writes, stays on the primary. A cache loader built on `RefreshingSnapshot` reads
  the replica only for the `TimeToLive` reason. Operator view:
  [README: read replicas](./README.md#read-replicas-and-database-clusters).
- Lists that can grow use a database-side `SearchAsync`/`CountAsync` pair, never `ListAsync()`
  sliced in memory. Worked end-to-end example: [README: adding a new
  entity](./README.md#backend-adding-a-new-entity-end-to-end).

## Configuration

- Options classes bind sections of `appsettings.json` (each has a `SectionName` constant, e.g.
  `DnsServer:QueryLogging`).
- Every setting is also a `NINLIL_*` environment variable derived from its config path: split
  PascalCase into words, uppercase, join with `_`, prefix `NINLIL_` (`IPv4`/`IPv6` stay one word).
  `DnsServer:Web:HttpPort` becomes `NINLIL_DNS_SERVER_WEB_HTTP_PORT`. The `Section__Key` form is
  disabled. List-valued settings have no variable. A class nested inside another options class
  (such as `StorageOptions.MigrationSafety`) must not have its own `SectionName` constant: the
  registry test fails if it does.
- A new top-level options class must be added to
  `src/dotnet/src/Ninlil.Web/EnvironmentVariables/OptionsEnvironmentVariableRegistry.cs`.
- [`ENVIRONMENT.md`](./ENVIRONMENT.md) is the single registry of variables. Update it (row, default,
  and the totals at the top) whenever you add, rename or remove an option. The totals are the
  number of names `OptionsEnvironmentVariableMap.BuildMappings` returns for
  `OptionsEnvironmentVariableRegistry.Roots` and `StandaloneConfigurationPaths`; count them, do not
  add by hand.

## Code conventions

- C#: file-scoped namespace preceded by the line `// Define our namespace.`; `using` directives at
  the top, outside it; nullable reference types off.
- Cancellation tokens are named `stoppingToken`.
- Private fields are usually camelCase without a leading underscore, but about 100 files use
  `_camelCase`; match the file you are editing. Explicit types unless the type is apparent
  (`.editorconfig`).
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
      `Ninlil.Web.Tests` if the host, API or DI wiring changed. If you changed a shared library
      (`Ninlil.Common.*`, `Ninlil.Dns.Common`, `Ninlil.Dhcp.Common`, `Ninlil.Dns.Server.Core`),
      run the whole solution: `dotnet test Ninlil.slnx -p:NinlilSkipAngularBuild=true`.
- [ ] `dotnet csharpier format` run on the C# files you changed (`dotnet csharpier check .`
      passes); `npm run lint` and `npm run format:check`
      clean for Angular changes, and `npm test` for UI logic.
- [ ] API changed: OpenAPI snapshot regenerated and `npm run generate:api` run, both committed.
- [ ] Schema changed: one migration per relational provider, MongoDB repository updated.
- [ ] Option added or changed: `OptionsEnvironmentVariableRegistry` (new class) and
      `ENVIRONMENT.md` updated.
- [ ] Behaviour visible to users changed: `README.md` updated.
- [ ] Comments follow the comment standard and every reference in them is still true.
- [ ] Small, logical commits with one-sentence messages.
