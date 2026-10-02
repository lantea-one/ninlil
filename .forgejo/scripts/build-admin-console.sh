#!/usr/bin/env bash
# Builds the Angular admin console with Node.js and installs its production output into
# src/dotnet/src/Ninlil.Web/wwwroot/, where a .NET build serves it (the web host's tests) or
# embeds it (the single-file binaries).
#
# `npm run build` first regenerates the typed API client from the committed openapi/swagger.json
# snapshot (the "prebuild" script runs openapi-generator-cli), so a Java runtime must be installed
# too. The pipeline runs this script in the node toolchain container, which has both:
#   .forgejo/scripts/in-container.sh node .forgejo/scripts/build-admin-console.sh
#
# Usage: build-admin-console.sh (no arguments, no environment)
#
# Output: src/dotnet/src/Ninlil.Web/wwwroot/, replaced. Exits non-zero when npm fails.

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
workspace="${repository_root}/src/angular"
wwwroot="${repository_root}/src/dotnet/src/Ninlil.Web/wwwroot"

(
    cd "${workspace}"
    npm ci --no-audit --no-fund
    npm run build
)

# Replace the directory, so no content-hashed bundle from an earlier build lingers.
rm -rf "${wwwroot}"
mkdir -p "${wwwroot}"
cp -r "${workspace}/dist/angular/browser/." "${wwwroot}/"
ls "${wwwroot}"
