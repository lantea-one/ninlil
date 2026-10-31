<One sentence: what changes for a user or caller.>

Refs #<issue>

<!-- One `Refs #n` line for every issue this pull request works on. A feature pull request never
says `Closes`. The commit that finishes an issue carries `Closes #n` in its body, and the issue
closes when that commit reaches `main` through the `develop` into `main` pull request. -->

## Checklist

The full list is [Before you finish](https://git.lantea.one/lantea-one/ninlil/src/branch/main/AGENTS.md#before-you-finish). Tick what applies.

- [ ] The build reports 0 warnings and 0 errors.
- [ ] The tests of every touched project pass, run by explicit path.
- [ ] The changed files are formatted (`dotnet csharpier check .`, `npm run format:check`).
- [ ] An API change regenerated the OpenAPI snapshot and the Angular client.
- [ ] A schema change has one migration per relational provider and a MongoDB update.
- [ ] A new or changed setting updated [ENVIRONMENT.md](https://git.lantea.one/lantea-one/ninlil/src/branch/main/ENVIRONMENT.md).
- [ ] A change users see updated the [README](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md).
- [ ] [CHANGELOG.md](https://git.lantea.one/lantea-one/ninlil/src/branch/main/CHANGELOG.md) has an `Unreleased` entry that links the issue.
