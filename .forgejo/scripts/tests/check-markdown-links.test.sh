#!/usr/bin/env bash
# Tests check-markdown-links.py against a throwaway git repository.
#
# The fixture holds one good file link, one good anchor, one anchor on a duplicate heading
# (GitHub slugs the second one with "-1"), one broken file link, one broken anchor, a reference
# definition, links whose paths hold a space (<angle brackets>) or parentheses, a heading with
# __bold__ emphasis, and broken-looking links inside a code fence, an inline code span and
# HTML comments (one line and several lines) that the checker must ignore. A second phase adds
# links in the Forgejo web form (https://git.lantea.one/lantea-one/ninlil/src/branch/main/<path>),
# which the checker resolves against the working tree: one good file and anchor, one missing
# file and one missing anchor.
#
# Usage: .forgejo/scripts/tests/check-markdown-links.test.sh
# Needs: bash, git, python3. Writes only to a temporary directory it removes on exit.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$(cd "${here}/.." && pwd)/check-markdown-links.py"
# shellcheck source=./assert.sh
source "${here}/assert.sh"

work_root="$(mktemp -d)"
trap 'rm -rf "${work_root}"' EXIT

export GIT_AUTHOR_NAME=test GIT_AUTHOR_EMAIL=test@example.invalid
export GIT_COMMITTER_NAME=test GIT_COMMITTER_EMAIL=test@example.invalid

repo="${work_root}/repo"
git init --quiet "${repo}"
mkdir -p "${repo}/docs"
touch "${repo}/docs/a b.md" "${repo}/docs/c(1).md"

cat > "${repo}/README.md" <<'MARKDOWN'
# Title

## Setup `now`

## Setup `now`

Good file link: [guide](docs/guide.md).
Good anchor: [guide section](docs/guide.md#the-section-1).
Good same-file anchor: [setup](#setup-now) and [second setup](#setup-now-1).
Good reference: [ref][r1].
Skipped external: [site](https://example.invalid/missing).

Broken file: [gone](docs/missing.md).
Broken anchor: [bad](docs/guide.md#no-such-heading).
Broken same-file anchor: [bad](#nowhere).

```
[ignored fence](docs/fence-missing.md)
```

An inline `[ignored span](docs/span-missing.md)` is skipped too.
<!-- [ignored comment](docs/comment-missing.md) -->
<!--
[ignored long comment](docs/long-comment-missing.md)
-->
Space and parentheses: [spaced](<docs/a b.md>) and [paren](docs/c(1).md).
Bold heading: [bold](docs/guide.md#a_b_c-and-bold-name).

[r1]: docs/guide.md
MARKDOWN

cat > "${repo}/docs/guide.md" <<'MARKDOWN'
# Guide

## The Section

## The Section!

## A_b_c and __bold__ name

Back to [readme](../README.md#title).
MARKDOWN

git -C "${repo}" add -A

output="$(cd "${repo}" && python3 -I "${script}" 2>&1)"
status=$?

assert_eq "broken links: exit code" 1 "${status}"
assert_contains "broken file link is reported with its line" "${output}" "README.md:13: "
assert_contains "broken file link names the target" "${output}" "docs/missing.md"
assert_contains "broken anchor is reported with its line" "${output}" "README.md:14: "
assert_contains "broken anchor names the fragment" "${output}" "no-such-heading"
assert_contains "broken same-file anchor is reported" "${output}" "README.md:15: "
assert_contains "summary counts three problems" "${output}" "3 problems"

problem_lines="$(grep -c '^[^ ]*\.md:[0-9]*: ' <<< "${output}")"
assert_eq "only the three broken links are reported" 3 "${problem_lines}"

# The same repository without the three broken lines passes.
sed -i '13,15d' "${repo}/README.md"
output="$(cd "${repo}" && python3 -I "${script}" 2>&1)"
status=$?
assert_eq "good links: exit code" 0 "${status}"
assert_contains "good links: summary" "${output}" "0 problems"

# A link in the Forgejo web form is checked against the working tree.
cat >> "${repo}/README.md" <<'MARKDOWN'

Forge file and anchor: [guide](https://git.lantea.one/lantea-one/ninlil/src/branch/main/docs/guide.md#the-section).
Forge directory: [docs](https://git.lantea.one/lantea-one/ninlil/src/branch/develop/docs).
MARKDOWN
output="$(cd "${repo}" && python3 -I "${script}" 2>&1)"
status=$?
assert_eq "good forge links: exit code" 0 "${status}"

cat >> "${repo}/README.md" <<'MARKDOWN'
Forge missing file: [gone](https://git.lantea.one/lantea-one/ninlil/src/branch/main/docs/missing.md).
Forge missing anchor: [bad](https://git.lantea.one/lantea-one/ninlil/src/branch/main/docs/guide.md#nowhere).
MARKDOWN
output="$(cd "${repo}" && python3 -I "${script}" 2>&1)"
status=$?
assert_eq "broken forge links: exit code" 1 "${status}"
assert_contains "broken forge file is reported" "${output}" "file not found: /docs/missing.md"
assert_contains "broken forge anchor is reported" "${output}" "anchor not found in docs/guide.md: #nowhere"
assert_contains "summary counts two forge problems" "${output}" "2 problems"

finish
