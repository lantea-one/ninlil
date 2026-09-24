#!/usr/bin/env bash
# Builds the Angular admin console with Node.js and installs its production output
# into src/dotnet/src/Ninlil.Web/wwwroot/, where a .NET build serves it (the web host's tests) or
# embeds it (the single-file binaries).
#
# `npm run build` regenerates the typed API client from the committed openapi/swagger.json
# snapshot first, with openapi-generator-cli, so a Java runtime must be installed too. The
# pipeline runs it in the node toolchain container, which has both:
#   .forgejo/scripts/in-container.sh node .forgejo/scripts/build-admin-console.sh
#
# Usage: build-admin-console.sh

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
workspace="${repository_root}/src/angular"
wwwroot="${repository_root}/src/dotnet/src/Ninlil.Web/wwwroot"

(
    cd "${workspace}"
    npm ci --no-audit --no-fund
    npm run build
)

# A fresh copy, so no stale content-hashed bundle from an earlier build lingers.
rm -rf "${wwwroot}"
mkdir -p "${wwwroot}"
cp -r "${workspace}/dist/angular/browser/." "${wwwroot}/"
ls "${wwwroot}"
