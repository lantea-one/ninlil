# Releases and versioning

This page explains how Ninlil versions are named, what a release contains, where to get it and how
to check that a download is the file the pipeline built.

The pipeline generates every version at build time. Nobody edits a version by hand.

## Version scheme

A release version is a date plus a counter: `v<yyyy>.<mm>.<dd>.<increment>`, for example
`v2026.09.24.1`. The date is the release's date and the increment counts the releases of that day,
starting at 1. Both come from the name of the release branch, which a maintainer names for the date
([Cutting a release](https://git.lantea.one/lantea-one/ninlil/src/branch/main/CONTRIBUTING.md#cutting-a-release)).

The same version is written in the form each output accepts:

| Where | Example (release) |
|---|---|
| Release name and git tag | `v2026.09.24.1R<short-sha>` |
| Assembly and file version | `2026.9.24.1` |
| NuGet package | `2026.9.24.1+<short-sha>` |
| Container tag | `2026.09.24.1` |

A local build is always `0.0.0-local`.

## Pre-releases

A push to `develop`, `main` or a `hotfix/**` branch, or a hand-started Build of any branch,
publishes a pre-release. Its version is `v<yyyy>.<mm>.<dd>.<increment>R<branch>+<short-sha>`. The
date is the build's UTC date and the increment is the next free one for that branch on that day.
The pre-release, its tag, its container tag and its NuGet versions are all removed after 24 hours.
Use a release for anything you keep. A feature branch has no push build: open the Actions page, pick
the Build workflow, choose **Run workflow** and select the branch.

Pre-release forms: container tag `2026.09.24.3-develop-<short-sha>`, NuGet `2026.9.24.3-develop+<short-sha>`.
A branch name is reduced to lower-case letters, digits and hyphens in these labels.

## Before you start

You need a web browser or `curl`, and a way to run `sha256sum` (Linux) or `Get-FileHash`
(PowerShell, Windows).

## Steps

### Find a release

1. Open the [releases page](https://git.lantea.one/lantea-one/ninlil/releases). Each release lists
   its commit, a link to its release notes and the files below.
2. The release notes are [`CHANGELOG.md`](https://git.lantea.one/lantea-one/ninlil/src/branch/main/CHANGELOG.md)
   at the release's tag. Read them before you [upgrade](../upgrades.md).

### What a release contains

| Item | Files or name |
|---|---|
| Binaries | `ninlil-linux-amd64`, `ninlil-linux-arm64`, `ninlil-macos-amd64`, `ninlil-macos-arm64`, `ninlil-windows-amd64.zip` (the executable and `msquic.dll`) |
| Arch Linux packages | `ninlil-<pkgver>-1-x86_64.pkg.tar.zst`, `ninlil-<pkgver>-1-aarch64.pkg.tar.zst` |
| Debian and Ubuntu packages | `ninlil-<version>-amd64.deb`, `ninlil-<version>-arm64.deb` |
| Fedora, RHEL and Rocky Linux packages | `ninlil-<version>-1.x86_64.rpm`, `ninlil-<version>-1.aarch64.rpm` |
| Checksums | `SHA256SUMS`, one line for each file |
| Container image | `git.lantea.one/lantea-one/ninlil`, tags `<yyyy>.<mm>.<dd>.<increment>` and `latest` ([Docker](../docker.md)) |
| Libraries | One NuGet package for each library project, in the Forgejo package registry |

A release stays an invisible draft until every file, package, image and library is uploaded, so a
visible release is complete. The installation guides say how to install each file
([Choose an installation](../getting-started/installation/index.md)).

### Verify a download

1. Download the file and the checksum list into one folder. Replace `<tag>` with the release's tag
   and the file name with yours:

    ```bash
    curl -LO https://git.lantea.one/lantea-one/ninlil/releases/download/<tag>/ninlil-linux-amd64
    curl -LO https://git.lantea.one/lantea-one/ninlil/releases/download/<tag>/SHA256SUMS
    ```

2. Check the file:

    ```bash
    sha256sum --check --ignore-missing SHA256SUMS
    ```

    ```text
    ninlil-linux-amd64: OK
    ```

    `OK` for your file means it is the one the pipeline built. A `FAILED` line means the download is
    damaged or changed: delete it and download again. On Windows, run `Get-FileHash <file> -Algorithm SHA256`
    and compare the hash with the line in `SHA256SUMS` (not run here; from the README).

The packages and the Windows executable are not signed. The checksums are your check. The macOS
binaries are ad-hoc signed, not notarized
([Download a binary](../getting-started/installation/binary.md)).

### Use the libraries

Add the Forgejo NuGet feed once, then add a package at a release version. Pre-release versions are
removed after 24 hours, so depend on a release:

```bash
dotnet nuget add source https://git.lantea.one/api/packages/lantea-one/nuget/index.json --name ninlil --username <user> --password <access-token> --store-password-in-clear-text
dotnet add package Ninlil.Dns.Common --version 2026.9.24.1
```

`<user>` and `<access-token>` are your Forgejo account and an access token that may read packages.
Packages from one build depend on exactly that build's other packages. Every library is published
except the `Ninlil.Web` host. This step is not run here; it is from the README.

## Check that it worked

`sha256sum --check` printed `OK` for your file, and the file name matches the release you chose.

## Next steps

- [Upgrades](../upgrades.md): moving to a newer release.
- [Choose an installation](../getting-started/installation/index.md).
- The [README](https://git.lantea.one/lantea-one/ninlil/src/branch/main/README.md#releases-and-versioning)
  for the full table of version forms and the pipeline.
