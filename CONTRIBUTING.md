# Contributing to Ninlil

This guide takes you from a clean machine to a merged pull request. Follow it in order the first
time; later, jump to the part you need.

## Contents

- [Who this guide is for](#who-this-guide-is-for)
- [Ground rules](#ground-rules)
- [Prerequisites](#prerequisites)
- [Get the code and build it](#get-the-code-and-build-it)
- [Test](#test)
- [Check the admin console in a browser](#check-the-admin-console-in-a-browser)
- [Format and lint](#format-and-lint)
- [Common changes](#common-changes)
  - [Change the REST API](#change-the-rest-api)
  - [Add an entity end to end](#add-an-entity-end-to-end)
  - [Add a migration](#add-a-migration)
  - [Add a setting](#add-a-setting)
  - [Add a console page](#add-a-console-page)
- [Build the binaries and packages](#build-the-binaries-and-packages)
- [Commit, branch and pull request](#commit-branch-and-pull-request)
- [Cutting a release](#cutting-a-release)
- [Reporting a vulnerability](#reporting-a-vulnerability)

## Who this guide is for

People and AI coding assistants who change Ninlil. An assistant also reads
[`AGENTS.md`](./AGENTS.md), the rules a change must follow, and starts from this guide for the
commands.

The assistant roles are in [`src/agents/`](./src/agents/). If you use a coding assistant, edit
those source files, never the generated copies, then run
`python3 src/script/development/sync-agent-definitions.py`. The lint job runs the same script with
`--check` and fails when a generated copy differs.

To run Ninlil rather than change it, read the [README](./README.md).

## Ground rules

1. **Clean-room.** Never read, fetch or port the source of another DNS, DHCP or ad-blocking
   project. Take protocol details from IETF RFCs and IANA registries. A protocol without an RFC
   takes its details from its own published specification or the vendor's documentation. Match or
   exceed other products' features, but never copy their user interface. See
   [`NOTICE.md`](./NOTICE.md).
2. **Comments.** Every type and public member has a doc comment that says why it exists, and
   inline comments explain only what the code does not. The full rules are the
   [comment standard in `AGENTS.md`](./AGENTS.md#comment-standard).
3. **Small commits.** One logical change per commit, with a one-sentence message (see
   [Commit, branch and pull request](#commit-branch-and-pull-request)).
4. **Finish the checklist.** [`AGENTS.md`](./AGENTS.md#before-you-finish) lists what must be true
   before a change is done.

## Prerequisites

Check each tool with its command. The "Expected" column shows what a working machine prints (your
values differ).

| Tool | Needed for | Check | Expected |
|---|---|---|---|
| git | Everything | `git --version` | `git version 2.56.0` |
| .NET SDK | Build and test the backend | `dotnet --version` | `10.0.113` |
| Node.js and npm | Build and test the admin console | `node --version` and `npm --version` | `v24.22.1` and `12.2.1` |
| A Java runtime | The OpenAPI generator that writes the console's API client | `java -version` | a version banner such as `openjdk version "27.0.1.0.3"` |
| Chromium | The console's unit tests and the smoke harness | `chromium --version` | `Chromium 156.0.8078.79` |
| Docker (optional) | Database tests, the lint container | `docker --version` | `Docker version 29.9.2` |

Version rules:

- The SDK must satisfy [`global.json`](./global.json): version `10.0.100` or a later 10.0 feature
  band (`rollForward` is `latestFeature`).
- The pipeline builds the console with Node.js 24 (`NODE_IMAGE` in
  [`docker/ci.Dockerfile`](./docker/ci.Dockerfile)), so use Node.js 24.
- The pipeline's Java runtime is Debian's `default-jre-headless`, and its Chromium is Debian's
  `chromium` (the same file). Any current Java runtime and Chromium work.
- The console tests find Chromium through the `CHROME_BIN` variable. Set it when the tests cannot
  find the browser:

  ```bash
  export CHROME_BIN=/usr/bin/chromium
  ```

Docker is only needed for tests marked `RequiresDocker` and for the lint container (see
[Test](#test) and [Format and lint](#format-and-lint)).

## Get the code and build it

#### Before you start

1. Install the [prerequisites](#prerequisites).
2. Get a copy of the repository. Contributors without write access fork it on
   `git.lantea.one` first and clone their fork.

#### Steps

1. Clone the repository and enter it:

   ```bash
   git clone https://git.lantea.one/Lantea.One/ninlil.git
   cd ninlil
   ```

2. Restore the local tools (`dotnet-ef`, `csharpier`, `reportgenerator`):

   ```bash
   dotnet tool restore
   ```

   ```text
   Tool 'dotnet-ef' (version '9.0.19') was restored. Available commands: dotnet-ef
   Tool 'csharpier' (version '1.2.6') was restored. Available commands: csharpier
   Tool 'dotnet-reportgenerator-globaltool' (version '5.5.11') was restored. Available commands: reportgenerator
   ```

3. Build the backend. The flag skips the Angular build that `Ninlil.Web` otherwise runs, which
   needs npm and a Java runtime and fails while the OpenAPI snapshot and the controllers disagree:

   ```bash
   dotnet build Ninlil.slnx -p:NinlilSkipAngularBuild=true
   ```

   ```text
   Build succeeded.
       0 Warning(s)
       0 Error(s)
   ```

   The build must report 0 warnings.

4. Build the admin console and copy it into `Ninlil.Web`'s `wwwroot/` folder. The script runs
   `npm ci`, then `npm run build` (which regenerates the API client first), then replaces
   `wwwroot/`. npm prints `npm warn deprecated` lines; they are not errors.

   ```bash
   .forgejo/scripts/build-admin-console.sh
   ```

   The last lines list the files copied into `wwwroot/` (your hashes differ):

   ```text
   favicon.ico
   index.html
   main-2YRNO6GU.js
   styles-LBA24YYS.css
   ```

5. Start Ninlil on unprivileged ports. The bootstrap password creates the `admin` account on the
   first start (at least 12 characters):

   ```bash
   NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD='Change-Me-Now1!' \
   NINLIL_DNS_SERVER_WEB_HTTP_PORT=18080 \
   NINLIL_DNS_SERVER_WEB_HTTPS_PORT=18443 \
   NINLIL_DNS_SERVER_PROTOCOL_PLAIN_PORT=15300 \
   dotnet run --project src/dotnet/src/Ninlil.Web -p:NinlilSkipAngularBuild=true
   ```

   Leave it running and open a second terminal.

#### Check that it worked

The log ends with these lines (your paths differ):

```text
      Storage: Sqlite, primary a local file, no read replica.
      Now listening on: http://[::]:18080
      Now listening on: https://[::]:18443
      Application started. Press Ctrl+C to shut down.
```

In the second terminal, check the health endpoint and sign in. `-k` accepts the self-signed
certificate Ninlil generates:

```bash
curl http://127.0.0.1:18080/status
curl -k https://127.0.0.1:18443/api/auth/login -H 'Content-Type: application/json' -d '{"userName":"admin","password":"Change-Me-Now1!"}'
```

```text
{"status":"Healthy","components":null}
{"requiresTwoFactor":false,"mfaToken":null,"accessToken":"eyJhbGciOiJIUzI1NiIsIn...
```

Open `https://localhost:18443/` in a browser (accept the certificate warning) and sign in as
`admin`. The README's [Quick start](./README.md#quick-start) continues from here with a zone and a
DNS query. For the REST API's conventions (token header, paging, errors), read
[REST API](./README.md#rest-api).

#### Behaviour worth knowing

- Press Ctrl+C in the first terminal to stop Ninlil.
- `dotnet run` keeps the database and keys in `data/` next to the built program,
  `src/dotnet/src/Ninlil.Web/bin/Debug/net10.0/data/`. Delete that folder to start over.
- Without `-p:NinlilSkipAngularBuild=true`, `dotnet run` and `dotnet build` of `Ninlil.Web` also
  build the console. Without npm on `PATH` that step only warns.

## Test

Name a test project by its path. Do not use wildcards: in zsh an unmatched glob makes the whole
command fail. List the projects with `ls src/dotnet/tests`.

1. Run one project:

   ```bash
   dotnet test src/dotnet/tests/Ninlil.Dns.Server.Core.Tests
   ```

   ```text
   Passed!  - Failed:     0, Passed:   344, Skipped:     0, Total:   344, Duration: 4 s - Ninlil.Dns.Server.Core.Tests.dll (net10.0)
   ```

2. Run part of a project with a filter on the full test name:

   ```bash
   dotnet test src/dotnet/tests/Ninlil.Web.Tests --filter "FullyQualifiedName~SpaFallbackTests"
   ```

   ```text
   Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4, Duration: 3 s - Ninlil.Web.Tests.dll (net10.0)
   ```

3. Run the whole solution. It is slow. Leave out the tests that need Docker when no Docker socket
   is available, as the pull-request pipeline does:

   ```bash
   dotnet test Ninlil.slnx -p:NinlilSkipAngularBuild=true --filter "Category!=RequiresDocker"
   ```

4. Run the console's unit tests (headless Chromium):

   ```bash
   cd src/angular
   npm test -- --watch=false --browsers=ChromeHeadlessNoSandbox
   ```

   ```text
   TOTAL: 1625 SUCCESS
   ```

   The count grows with the suite (your value differs).

#### Behaviour worth knowing

- **Docker.** `Ninlil.Dns.Server.QueryLog.Tests` and `Ninlil.Dns.Data.Integration.Tests` start
  database containers (Testcontainers). Every test or class that starts a container carries
  `[Trait("Category", "RequiresDocker")]`; add the trait to any test you write that does. The
  integration project runs the storage layer against PostgreSQL, MySQL, SQL Server and MongoDB, one
  fresh database per test; add multi-engine storage tests there. Its SQLite tests need no Docker.
- **Ports.** Tests take ports the operating system reports as free and need no root.
- **IPv6.** An IPv6 test starts with
  `if (!LocalAddresses.CanBind(IPAddress.IPv6Loopback)) return;` (the `Fakes/LocalAddresses.cs`
  helper in each transport test project).
- **QUIC.** A QUIC test checks `QuicListener.IsSupported` or `QuicConnection.IsSupported` and
  returns early when libmsquic is missing, so it reports as passed, not skipped.
- **The shared Web host.** In `Ninlil.Web.Tests` every class joins
  `[Collection(WebTestCollection.Name)]` and shares one `NinlilDnsWebApplicationFactory` host.
  A test that needs a second host calls `factory.CreateSecondHost(...)`, never
  `WithWebHostBuilder`: the plain call reuses the shared host's DNS listener ports, the bind
  fails, the host stops itself and the first request throws `ObjectDisposedException`. Rows from
  other tests are visible, so never assume an empty table or an exact count; look up what you
  created.
- **`SpaFallbackTests`** serve the built admin console from `wwwroot/`. A build of `Ninlil.Web`
  without `-p:NinlilSkipAngularBuild=true` produces it when npm is on `PATH`; otherwise run
  `.forgejo/scripts/build-admin-console.sh` first.
- **Timing.** The pipeline runs `DnssecKeyRolloverZskPrePublishLiveTests` alone after the other
  tests, because it is sensitive to CPU contention. Run it alone too when it fails in a full run.
- **Test names** read as sentences: `Method_Condition_ExpectedResult`, for example
  `Fallback_ForADeepLinkClientRoute_ServesIndexHtml`.

## Check the admin console in a browser

The smoke harness [`admin-console-smoke.mjs`](./src/script/development/admin-console-smoke.mjs)
checks that every console page renders. It starts a throw-away Ninlil host (its own SQLite database,
free ports, a known administrator), seeds sample data through the REST API, starts headless
Chromium, signs in through the login form and visits the pages at several widths in the light and
dark theme. Every visit checks that there is no script error, that every same-origin response is
below 400, that a heading is visible, that nothing overflows horizontally and that the network goes
idle. The harness is not part of the pipeline: run it by hand after a change to the console.

#### Before you start

1. Build the backend and the console as in
   [Get the code and build it](#get-the-code-and-build-it). The harness starts the host with
   `--no-build`, so the build output must exist, and it serves the console from `wwwroot/`.
2. Node.js 22 or later, `dotnet` on `PATH`, and Chromium (`CHROME_BIN` or `--chrome`).
3. An output directory outside the repository, for example `/tmp/ninlil-smoke`.

#### Steps

1. List the pages the harness knows (it starts nothing):

   ```bash
   node src/script/development/admin-console-smoke.mjs --list
   ```

   ```text
   dashboard
   dashboard-narrow-sidebar
   dashboard-mobile-drawer
   forbidden
   login
   zones-list
   ```

   The list continues (123 lines); the first lines are shown.
2. Run one page at one width, in one theme, as one user. This takes about 20 seconds:

   ```bash
   node src/script/development/admin-console-smoke.mjs --out /tmp/ninlil-smoke --pages dashboard --widths 1400 --themes light --users admin
   ```

   Node prints a warning about `NODE_TLS_REJECT_UNAUTHORIZED` (the script disables certificate
   checks for its own calls to the host's self-signed certificate), then the summary:

   ```text
   ## Summary

   - Checks: 14 (failed: 0)
   - Page visits: 2, distinct pages: 2
   - Users: admin
   - Widths: 1400; themes: light
   - Time zone list holds UTC: false (418 names)
   - Seed steps failed: 0 of 30
   ```

3. Run the full sweep (all pages, widths 1400, 900 and 390, both themes, the administrator and two
   limited users) by leaving out the filters:

   ```bash
   node src/script/development/admin-console-smoke.mjs --out /tmp/ninlil-smoke
   ```

   The options are `--pages`, `--widths`, `--themes` (`light`, `dark`), `--users` (`admin`,
   `limited`), `--chrome`, `--skip-build` and `--list`; the header of the script describes each.

#### Check that it worked

Read the exit code right after the run, then list the output:

```bash
echo $?
ls /tmp/ninlil-smoke/shots
```

```text
0
dashboard-1400-light.png
dashboard-narrow-sidebar-1400-light.png
```

The `shots/` listing is for the one-page run above; the full sweep writes one file per visit. The exit code is the result: `0` when every check passed and at least one page was visited, `1` when
a check failed or the sweep visited nothing, `2` when the host or the browser could not start (for
example `Startup failed: No Chromium found: set CHROME_BIN or pass --chrome`). `report.md` lists
failed checks first; `shots/` holds one full-page screenshot per visit; `host.log` and
`chrome.log` hold the processes' output.

#### When something goes wrong

- *A check fails.* Open `report.md`, then the matching screenshot in `shots/`, then `host.log`.
- *Exit code `2` and `No Chromium found`.* Set `CHROME_BIN` or pass `--chrome <path>`.
- *The host does not start.* Build first (or drop `--skip-build`), and make sure `wwwroot/` holds
  the console.

#### More scripts for manual checks

- [`src/script/development/dhcp-relay-two-link-test.sh`](./src/script/development/dhcp-relay-two-link-test.sh)
  checks that the DHCP relay sends its replies out the client's link. It needs root, Linux network
  namespaces, `tcpdump` and external DHCP clients, so it is not part of the pipeline; its header
  explains the arguments and the result.
- [`src/script/database/README.md`](./src/script/database/README.md) describes the per-engine
  database helper scripts.
- [`.forgejo/scripts/windows-smoke.ps1`](./.forgejo/scripts/windows-smoke.ps1) starts the Windows
  executable and checks `/status`, the console and one DNS query. The pipeline runs it on a Windows
  runner.

## Format and lint

Both formatters are pipeline gates.

1. C#: format the files you changed (or `.` for everything), then check:

   ```bash
   dotnet csharpier format <changed files or folders>
   dotnet csharpier check .
   ```

   ```text
   Checked 2732 files in 7475ms.
   ```

   Generated EF migrations are skipped by `.csharpierignore`. Never format them by hand or remove
   that entry.
2. Angular, from `src/angular/`:

   ```bash
   npm run format
   npm run lint
   npm run format:check
   ```

   ```text
   All matched files use Prettier code style!
   ```

3. Scripts and workflows: after a change under `.forgejo/`, `docker/`, `src/script/` or
   `src/linux-packages/`, run the pipeline's lint job in its toolchain container (needs Docker;
   the first run builds the image):

   ```bash
   ./.forgejo/scripts/in-container.sh lint ./.forgejo/scripts/lint.sh
   ```

   It runs five steps, each reported under its own `==` line: shellcheck, actionlint, no
   block-scoped C# namespaces, the agent definitions
   (`ok: 9 roles, 36 definitions`) and the script self-tests. A clean tree ends with
   `Lint passed.`; otherwise the last line is `Lint failed:` and the names of the failed steps.
4. Every C# file uses a file-scoped namespace (`namespace X;`) preceded by the line
   `// Define our namespace.`, with the `using` directives above it.

## Common changes

### Change the REST API

#### Before you start

Build the backend with `dotnet build Ninlil.slnx -p:NinlilSkipAngularBuild=true` first. The
Angular build fails while the OpenAPI snapshot and the controllers disagree, so the snapshot must
be regenerated before the Angular side is built.

#### Steps

1. Change the controllers and models under `src/dotnet/src/Ninlil.Web/`, with Swashbuckle examples
   under `Examples/`. XML doc comments on API types become the OpenAPI text, so a comment change
   changes the snapshot too.
2. Build the backend as above.
3. Regenerate the snapshot. `OpenApiSnapshotTests` fails until you do:

   ```bash
   NINLIL_UPDATE_OPENAPI_SNAPSHOT=true dotnet test src/dotnet/tests/Ninlil.Web.Tests --filter FullyQualifiedName~OpenApiSnapshotTests
   ```

4. Regenerate the TypeScript client (`src/angular/src/app/api-client/` is generated: never edit it
   by hand):

   ```bash
   cd src/angular
   npm run generate:api
   ```

5. Commit the snapshot (`src/angular/openapi/swagger.json`) and the client together, in a commit
   separate from the backend change. Then build the Angular side normally.

#### Check that it worked

Run the same test without the variable. It passes when the snapshot matches the controllers:

```bash
dotnet test src/dotnet/tests/Ninlil.Web.Tests --filter FullyQualifiedName~OpenApiSnapshotTests
```

```text
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 4 s - Ninlil.Web.Tests.dll (net10.0)
```

On a tree with no API change, the regeneration in step 3 leaves `git status` clean.

### Add an entity end to end

The static IP and CIDR to client mapping is a small, self-contained entity with no owning parent.
Read it start to finish as the template. The steps list its file for each layer.

#### Steps

1. **Entity** in the owning `*.Data.Abstractions` project, inheriting `EntityBase` or
   `AuditableEntityBase`, with a doc comment on the class and on every property
   (`Ninlil.Dns.Data.Abstractions/Entity/DnsClientStaticAddressMappingEntity.cs`).
2. **Repository interface** in the same project, extending `IRepository<TEntity>`
   (`.../Repository/IDnsClientStaticAddressMappingRepository.cs`). A list that can grow gets a
   database-side `SearchAsync` and `CountAsync` pair, never `ListAsync()` sliced in memory.
3. **EF Core implementation** in `Ninlil.Dns.Data.EntityFrameworkCore`:
   - an `IEntityTypeConfiguration<TEntity>` under `Configuration/`
     (`DnsClientStaticAddressMappingConfiguration.cs`);
   - the repository under `Repository/` (`DnsClientStaticAddressMappingRepository.cs`);
   - a `DbSet` in `NinlilDnsContext.cs` and a repository registration in
     `Extension/ServiceCollectionEntityFrameworkCoreExtension.cs`;
   - a migration for every relational provider (see [Add a migration](#add-a-migration)).
4. **MongoDB implementation** in `Ninlil.Dns.Data.MongoDb`: a repository under `Repository/`
   (`DnsClientStaticAddressMappingMongoRepository.cs`), a collection in `NinlilDnsMongoContext.cs`,
   a `RegisterEntity<TEntity>()` line in `Configuration/MongoClassMapRegistrar.cs` and a
   repository registration in `Extension/ServiceCollectionMongoDbExtension.cs`. MongoDB has no
   migrations, and `AutoMap()` picks up added properties. Do not call `MapIdProperty` on `Id`: it
   throws. When you backfill data or change a document shape in a way an older build would
   misread, add a step to `MongoSchemaSteps.Known`; never rename or remove a step.
5. **Backup.** A configuration entity belongs in
   `Ninlil.Dns.Server.Backup/Catalog/BackupEntityCatalog.cs` (the static address mapping is listed
   there); only a history table stays out of it.
6. **REST controller** in `Ninlil.Web/Controllers/`
   (`DnsClientStaticAddressMappingsController.cs`), with the request and response models in
   `Models/DnsClientStaticAddressMappingModels.cs` and Swashbuckle examples under
   `Examples/ClientStaticAddressMappings/`. Page the list through `SearchAsync` and `CountAsync`.
   A GET action reads from a read replica only with `[ReadFromReplica]`; anything that writes, or
   reads and then writes, stays on the primary. Then
   [change the REST API](#change-the-rest-api) steps 2 to 5.
7. **Tests.** Mirror an existing test class rather than inventing a shape:
   - a `WebApplicationFactory<Program>` test in `Ninlil.Web.Tests`
     (`ClientStaticAddressMappingCrudTests.cs`) for the whole CRUD and paging surface;
   - a repository test in `Ninlil.Dns.Data.Integration.Tests/Repository/`, which runs against every
     storage engine.

#### Check that it worked

Run the Web test class, the integration project's SQLite tests and the whole solution:

```bash
dotnet test src/dotnet/tests/Ninlil.Web.Tests --filter "FullyQualifiedName~ClientStaticAddressMappingCrudTests"
dotnet test Ninlil.slnx -p:NinlilSkipAngularBuild=true --filter "Category!=RequiresDocker"
```

### Add a migration

Every relational provider keeps its migrations in its own assembly. All four share one model
(`NinlilDnsContext`, which holds the DNS and the DHCP entities), so one change needs one migration
per provider. Never put two providers' migrations in one assembly.

#### Steps

1. Change the entity and its `IEntityTypeConfiguration` in
   `Ninlil.Dns.Data.EntityFrameworkCore/Configuration/`.
2. Generate the migration once per provider, from inside each provider's migrations project (each
   has its own design-time factory, so no `--startup-project` is needed), then convert the
   namespaces:

   ```bash
   for p in Sqlite PostgreSql MySql SqlServer; do
     (cd src/dotnet/src/Ninlil.Dns.Data.EntityFrameworkCore.Migrations.$p && dotnet ef migrations add AddYourChange)
   done
   src/script/development/file-scoped-namespaces.sh src/dotnet/src/Ninlil.Dns.Data.EntityFrameworkCore.Migrations.*/Migrations
   ```

   `dotnet ef` writes block-scoped namespaces; the script rewrites the generated files to the
   file-scoped form and leaves converted files alone. The lint job fails on a block-scoped
   namespace.
3. Do not edit or reformat the generated migration, its `*.Designer.cs` or
   `NinlilDnsContextModelSnapshot.cs`. One exception: when the change alters how existing rows are
   stored (for example a date column moves to ticks), add the data conversion
   `migrationBuilder.Sql(...)` to `Up` and `Down` of the generated migration file, for every
   provider that needs it. `*ConvertDhcpLeaseTimesToTicks.cs` is the example. Never touch the
   designer or the snapshot.
4. Relational databases apply migrations at start through `RelationalStartupMigrator`. Do not call
   `Database.MigrateAsync()` from `Program.cs` or a host you add.

#### Check that it worked

Each `dotnet ef migrations add` prints `Done. To undo this action, use 'ef migrations remove'`,
and the loop creates two files per provider (`<timestamp>_AddYourChange.cs` and
`<timestamp>_AddYourChange.Designer.cs`). On a model with no change the migration's `Up` and `Down`
are empty and the snapshot is untouched, which shows that the model and the snapshots agree. To
discard such an experiment, delete the two files per provider.

### Add a setting

#### Steps

1. Add the property, with a doc comment, to the options class that owns it. A top-level options
   class has a `SectionName` constant that names its configuration section
   (`DnsRateLimitOptions.SectionName` is `DnsServer:RateLimiting`). A class nested inside another
   options class must not have its own `SectionName`.
2. For a top-level class, bind it in its project's `ServiceCollection...Extension`
   (`AddNinlilDnsServerRateLimiting` in `ServiceCollectionRateLimitingExtension.cs` calls
   `Configure<DnsRateLimitOptions>(configuration.GetSection(DnsRateLimitOptions.SectionName))`) and
   add `new(typeof(YourOptions))` to `OptionsEnvironmentVariableRegistry.Roots` in
   `Ninlil.Web/EnvironmentVariables/OptionsEnvironmentVariableRegistry.cs`.
3. Every setting is also an environment variable: split the PascalCase path into words, uppercase
   them, join them with `_` and prefix `NINLIL_` (`DnsServer:Web:HttpPort` becomes
   `NINLIL_DNS_SERVER_WEB_HTTP_PORT`). List-valued settings have no variable.
4. Add the row to [`ENVIRONMENT.md`](./ENVIRONMENT.md): variable, what it controls and its default.
   Update the "Total:" sentence at the top without changing its wording: a test parses it. Do not
   add the numbers by hand.
5. Run the two tests that compare the document with the registry:

   ```bash
   dotnet test src/dotnet/tests/Ninlil.Web.Tests --filter "FullyQualifiedName~EnvironmentVariableDocumentationTests|FullyQualifiedName~OptionsEnvironmentVariableRegistryTests"
   ```

   A wrong total fails with the right count, for example
   `The document states 331 individual settings; the registry has 332.` Put the count the test
   reports into the sentence.
6. When the behaviour is visible to operators, update the README guide that describes it.

#### Check that it worked

```text
Passed!  - Failed:     0, Passed:    22, Skipped:     0, Total:    22, Duration: 122 ms - Ninlil.Web.Tests.dll (net10.0)
```

### Add a console page

The matching Angular feature for the static address mappings is
`src/angular/src/app/features/client-static-address-mappings/`, a list component and a form
component, each in its own subfolder. Feature pages follow that "list + form" shape.

#### Steps

1. Change the backend and regenerate the snapshot and the client first
   ([Change the REST API](#change-the-rest-api)). The generated client in
   `src/angular/src/app/api-client/` is never edited by hand.
2. Create the feature folder `src/angular/src/app/features/<feature-name>/`. Use standalone
   components only (no `NgModule`), signals for state, and no dependency on `zone.js` (the app is
   zoneless). File names are kebab-case with a type suffix (`*.component.ts`, `*.service.ts`,
   `*.guard.ts`), one exported artifact per file, no barrel `index.ts`.
3. Build the page from CoreUI components, themed through the tokens in `src/angular/src/styles/`.
   Never copy another product's visual design.
4. Add the routes to `src/angular/src/app/app.routes.ts`, each with a
   `canActivate: [permissionGuard('<resource>:<action>')]` that matches the controller's
   permission (the `client-static-address-mappings` entry shows list, `new` and `:id/edit`).
5. Add the sidebar entry to `NAV_ITEMS` in
   `src/angular/src/app/layout/app-shell/nav-item.model.ts`. Its `permission` is the read
   permission of the controller behind the page, and its icon must be registered in `app.config.ts`.
6. Write TSDoc (`/** */`) on every exported component, service, guard, function, constant and
   interface: what it is, why it exists and what uses it.
7. Write a Jasmine spec next to each component, following an existing spec (TestBed setup, a mocked
   API client, assertions on signal state).
8. Run the four gates from `src/angular/`:

   ```bash
   npm run lint
   npm run format:check
   npm run build
   npm test -- --watch=false --browsers=ChromeHeadlessNoSandbox
   ```

9. Run the [smoke harness](#check-the-admin-console-in-a-browser) for the page.

#### Check that it worked

All four commands exit with code 0, and the smoke harness reports `failed: 0`.

## Build the binaries and packages

The pipeline builds each release artifact with a script. Run the same script to test a change to
the build.

#### Steps

1. Build the admin console into `wwwroot/`
   ([Get the code and build it](#get-the-code-and-build-it), step 4). The executable embeds it.
2. Publish one self-contained, single-file executable. The name is `linux-amd64`, `linux-arm64`,
   `macos-amd64`, `macos-arm64` or `windows-amd64`:

   ```bash
   .forgejo/scripts/publish-binary.sh linux-amd64 dist
   ```

3. Pack NuGet packages. The patterns are shell patterns matched against project directory names,
   for example `'Ninlil.Dns.Protocol.* Ninlil.Dns.Server.*'`; `'Ninlil.*'` packs every library:

   ```bash
   .forgejo/scripts/pack-packages.sh 'Ninlil.Common.Extension' packages
   ```

#### Check that it worked

```bash
ls dist packages
```

```text
dist:
ninlil-linux-amd64

packages:
Ninlil.Common.Extension.0.0.0-local.nupkg
Ninlil.Common.Serializer.0.0.0-local.nupkg
```

A local build has the version `0.0.0-local`. The packer also packs the libraries that the pattern's
projects depend on, which is why `Ninlil.Common.Serializer` appears (your list differs with the
pattern).

#### Behaviour worth knowing

The scripts use the Node.js and .NET SDK you have installed. To use the pipeline's own toolchains,
which need only Docker, prefix a script with `.forgejo/scripts/in-container.sh node` or
`.forgejo/scripts/in-container.sh dotnet`.

## Commit, branch and pull request

#### Steps

1. Branch from `develop` for a feature, from `main` for a hotfix:

   | Branch | Created from | Pushed to directly | Merges into | Through |
   |---|---|---|---|---|
   | `feature/*` | `develop` | Yes | `develop` | Pull request |
   | `hotfix/*` | `main` | Yes | `main` | Pull request |
   | `develop` | none | No | `main` | Pull request |
   | `main` | none | No | `v<yyyy>.<mm>.<dd>.<increment>` | A release branch created from it |

   ```bash
   git switch develop && git pull
   git switch -c feature/<short-name>
   ```

2. Commit in small logical steps. The message is one plain-English sentence in the present tense
   that describes the behaviour change as a user or caller sees it. It has no prefix (`feat:`,
   `fix:`) and no scope tag. Examples from the history:
   - `After the admin console changes the user's password, turns two-factor on or off, or generates recovery codes, it signs the user out and the login page says why`
   - `The README explains DHCPv4, DHCPv6, Router Advertisement, TFTP and relays, step by step`

   Keep the backend change, the snapshot with the client, and the console change in separate
   commits.
3. Run the checks from [Format and lint](#format-and-lint) and the
   [`AGENTS.md` checklist](./AGENTS.md#before-you-finish).
4. Push the branch and open a pull request into `develop` (a hotfix goes into `main`):

   ```bash
   git push -u origin feature/<short-name>
   ```

   Never commit secrets. Contributors without write access push to their fork.

#### What the pipeline runs

The workflows are in [`.forgejo/workflows/`](./.forgejo/workflows/). The README's
[pipeline section](./README.md#the-pipeline) describes them in full.

- **A pull request** into `develop` or `main` runs `verify`, which is three jobs: the C# format
  check, a Release build and every test project; the console's lint, format check and unit tests;
  and the lint job. It gets no Docker socket and skips the tests marked `RequiresDocker`.
- **A push** to any branch other than a release branch runs the same checks with the Docker
  socket, so the `RequiresDocker` tests run, and then publishes a pre-release build that expires
  after 24 hours. A same-repository branch is pushed before its pull request, so its push build
  covers the Docker tests.
- **A pull request from a fork** waits for a maintainer's approval before its workflows run. The
  maintainer reads the diff of `.forgejo/` and of every script it runs first.

## Cutting a release

For maintainers. The version is generated by the pipeline from the release branch's name; see
[Releases and versioning](./README.md#releases-and-versioning).

1. In `CHANGELOG.md`, rename the `## Unreleased` heading to the release branch's name from step 3
   (for example `## v2026.09.24.1`) and add an empty `## Unreleased` section above it. Commit this on `develop` (or the
   `hotfix/*` branch) so that step 2 carries it to `main`: the release page links the file at the
   release's tag.
2. Merge `develop` (or a `hotfix/*` branch) into `main` through a pull request.
3. Create the release branch from `main`, named for the release's date and that day's increment:

   ```bash
   git switch main && git pull
   git switch -c v2026.09.24.1
   git push -u origin v2026.09.24.1
   ```

4. The pipeline verifies that the branch points at a commit on `main`, runs every test and
   publishes the release. A release version is published once: pushing the same release branch
   again with a different commit fails rather than overwriting it.
5. Merge `main` back down into `develop`.

## Reporting a vulnerability

Do not open a public issue or pull request for a vulnerability. Send an e-mail to
`git+security@lantea.one`, as [`SECURITY.md`](./SECURITY.md) describes.
