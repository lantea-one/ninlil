#!/usr/bin/env bash
# Builds the wiki under pages/ with MkDocs in strict mode: any warning (a broken link, a page
# missing from the navigation that the build cannot read, a bad configuration key) fails the build.
# The pipeline runs this script in the docs toolchain container, which holds MkDocs and the
# Material theme at fixed versions (docker/ci.Dockerfile):
#   .forgejo/scripts/in-container.sh docs .forgejo/scripts/build-wiki.sh
#
# Usage: build-wiki.sh (no arguments)
#
# Environment: none.
#
# Output: pages/site/, replaced (git-ignored); the build log on stdout. Exits non-zero when the
# build prints a warning or an error.

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${repository_root}"

# Replace the folder, so no page that was removed from the sources lingers in the output.
rm -rf pages/site
mkdocs build --strict --config-file pages/mkdocs.yml
