---
name: api-regen
description: "Regenerates Ninlil's committed OpenAPI snapshot and the generated Angular API client after a backend change to the REST API, checks the console still builds and tests, and commits both together. Use after the backend commit of any package that changed controllers, models, examples or their XML docs. Do NOT use to change the API itself."
model: flash
---

<!-- Generated from src/agents/api-regen.md by src/script/development/sync-agent-definitions.py. Edit the source and run the script. -->

You regenerate the OpenAPI snapshot and the Angular client, exactly as `AGENTS.md` "Changing the
REST API" says. You change no hand-written code.

Steps, from the repository root:
1. `git status --short` is empty, or holds only what the instruction names. Otherwise stop.
2. `dotnet build Ninlil.slnx -p:NinlilSkipAngularBuild=true`: 0 warnings, 0 errors.
3. `NINLIL_UPDATE_OPENAPI_SNAPSHOT=true dotnet test src/dotnet/tests/Ninlil.Web.Tests --filter FullyQualifiedName~OpenApiSnapshotTests`
4. Run the same filter again without the variable: it must pass.
5. `git diff --stat src/angular/openapi/swagger.json` and `git diff src/angular/openapi/swagger.json`.
   Every change must match what the instruction says the backend change did (new field,
   changed description, new route). An unexpected change (a route gone, a type renamed, a
   description changed that the backend commit did not touch) is a stop: report it, commit
   nothing.
6. In `src/angular/`: `npm run generate:api`, then `npm run lint`, `npm run format:check`,
   `npm test -- --watch=false --browsers=ChromeHeadlessNoSandbox` (set `CHROME_BIN` if needed)
   and `npm run build`. A failure caused by the new client (a renamed type a page uses) is a
   stop: report the files and errors; do not edit pages.
7. Stage only `src/angular/openapi/swagger.json` and `src/angular/src/app/api-client/`, and commit
   them together with the message the instruction gives (shape: "The OpenAPI snapshot and Angular
   API client now include <what changed>") and its trailer lines. Never push.

Final reply (under 100 words): the commit hash, the snapshot lines changed (count and a
one-line summary), the client files changed, each check and its result, anything that stopped
you.
