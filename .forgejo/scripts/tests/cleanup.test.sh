#!/usr/bin/env bash
# Tests cleanup.sh against a fake Forgejo server (fake-forgejo.py) with the real curl and jq.
#
# Covers: the token never appears in any curl command line; the curl config file is mode 0600
# and gone after the run; a token holding a double quote and a backslash still reaches the server
# unchanged; a failed page of the package listing stops the run before any DELETE; published
# pre-releases are found (the release listing has no draft filter) and removed with their tag;
# a digest that a kept tag references is kept; DRY_RUN removes nothing; and a kept tag whose
# manifest index lists nothing aborts the run instead of deleting every digest.
#
# Usage: .forgejo/scripts/tests/cleanup.test.sh
# Needs: bash, python3, curl, jq. Never contacts a real server.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./assert.sh
source "${here}/assert.sh"
# shellcheck source=./harness.sh
source "${here}/harness.sh"

token='tok"en\with\\odd"chars'
old="$(date -u -d '-3 days' +%Y-%m-%dT%H:%M:%SZ)"
fresh="$(date -u +%Y-%m-%dT%H:%M:%SZ)"
api='/api/v1'

# The standard case: one published pre-release, one published release, one expired image tag,
# the release tag and latest (both old but kept), and two expired per-architecture digests of
# which only one is referenced by a kept tag.
standard_routes() {
    cat <<JSON
{
  "GET ${api}/repos/acme/ninlil/releases#1": [200, [
    {"id": 11, "tag_name": "v2026.01.01.1Rmain+abc1234", "prerelease": true, "draft": false, "created_at": "${old}"},
    {"id": 12, "tag_name": "v2026.01.01.2", "prerelease": false, "draft": false, "created_at": "${old}"},
    {"id": 13, "tag_name": "v2026.01.01.3Rmain+def5678", "prerelease": true, "draft": false, "created_at": "${fresh}"}
  ]],
  "GET ${api}/packages/acme#1": [200, [
    {"type": "container", "name": "ninlil", "version": "2026.01.01.1-main-abc1234", "created_at": "${old}"},
    {"type": "container", "name": "ninlil", "version": "2026.01.01.2", "created_at": "${old}"},
    {"type": "container", "name": "ninlil", "version": "latest", "created_at": "${old}"},
    {"type": "container", "name": "ninlil", "version": "sha256:aaa", "created_at": "${old}"},
    {"type": "container", "name": "ninlil", "version": "sha256:bbb", "created_at": "${old}"},
    {"type": "nuget", "name": "Ninlil.Dns.Common", "version": "2026.1.1.1-main", "created_at": "${old}"}
  ]],
  "GET /v2/token": [200, {"token": "registry-token"}],
  "GET /v2/acme/ninlil/manifests/latest": [200, {"manifests": [{"digest": "sha256:aaa"}]}],
  "GET /v2/acme/ninlil/manifests/2026.01.01.2": [200, {"manifests": [{"digest": "sha256:aaa"}]}]
}
JSON
}

deletes() { grep -c '^DELETE' "${request_log}" || true; }

# --- Standard run -----------------------------------------------------------------------------
set_routes "$(standard_routes)"
run_script -- bash "${scripts}/cleanup.sh"
assert_eq 'cleanup: standard run exits 0' 0 "${status}"
assert_contains 'cleanup: removes the published pre-release' "${output}" 'Removing release v2026.01.01.1Rmain+abc1234'
assert_contains 'cleanup: removes the pre-release tag' "${output}" 'Removing tag v2026.01.01.1Rmain+abc1234'
assert_contains 'cleanup: removes the expired image tag' "${output}" 'Removing container ninlil 2026.01.01.1-main-abc1234'
assert_contains 'cleanup: removes the expired NuGet pre-release' "${output}" 'Removing nuget Ninlil.Dns.Common 2026.1.1.1-main'
assert_contains 'cleanup: removes the unreferenced digest' "${output}" 'Removing container ninlil sha256:bbb'
for kept in 'release v2026.01.01.2' 'release v2026.01.01.3' 'container ninlil 2026.01.01.2' 'container ninlil latest' 'container ninlil sha256:aaa'; do
    if [[ "${output}" == *"Removing ${kept}"* ]]; then
        fail "cleanup: keeps ${kept}" 'it was removed'
    else
        pass "cleanup: keeps ${kept}"
    fi
done
if grep -q 'releases?.*draft' "${request_log}"; then
    fail 'cleanup: release listing has no draft filter' "$(grep 'releases?' "${request_log}")"
else
    pass 'cleanup: release listing has no draft filter'
fi
if grep -qF -- "${token}" "${argv_log}"; then
    fail 'cleanup: token is not on any curl command line' 'found in argv'
else
    pass 'cleanup: token is not on any curl command line'
fi
if grep -q '^CONFIG ' "${argv_log}" && ! grep '^CONFIG ' "${argv_log}" | grep -qv ' 600$'; then
    pass 'cleanup: every curl config file is mode 600'
else
    fail 'cleanup: every curl config file is mode 600' "$(grep '^CONFIG' "${argv_log}")"
fi
leftover="$(find "${run_temp}" -mindepth 1 | wc -l)"
assert_eq 'cleanup: no config file is left behind' 0 "${leftover}"
received="$(grep '^DELETE' "${request_log}" | head -n 1 | cut -f 3)"
assert_eq 'cleanup: server receives the token with quote and backslash intact' "token ${token}" "${received}"

# --- Failed page of the package listing --------------------------------------------------------
routes_json="$(standard_routes | jq --arg api "${api}" '. + {("GET " + $api + "/packages/acme#2"): [500, {"message": "boom"}]}')"
set_routes "${routes_json}"
run_script -- bash "${scripts}/cleanup.sh"
if [[ "${status}" -ne 0 ]]; then pass 'cleanup: a failed package page exits non-zero'; else fail 'cleanup: a failed package page exits non-zero' "${output}"; fi
assert_eq 'cleanup: a failed package page removes nothing' 0 "$(deletes)"
leftover="$(find "${run_temp}" -mindepth 1 | wc -l)"
assert_eq 'cleanup: no config file is left behind after a failure' 0 "${leftover}"

# --- Non-array page ----------------------------------------------------------------------------
set_routes "$(standard_routes | jq --arg api "${api}" '.["GET " + $api + "/packages/acme#1"] = [200, {"message": "oops"}]')"
run_script -- bash "${scripts}/cleanup.sh"
if [[ "${status}" -ne 0 ]]; then pass 'cleanup: an error object as a page exits non-zero'; else fail 'cleanup: an error object as a page exits non-zero' "${output}"; fi
assert_eq 'cleanup: an error object as a page removes nothing' 0 "$(deletes)"

# --- Dry run -----------------------------------------------------------------------------------
set_routes "$(standard_routes)"
run_script DRY_RUN=true -- bash "${scripts}/cleanup.sh"
assert_eq 'cleanup: dry run exits 0' 0 "${status}"
assert_contains 'cleanup: dry run reports what it would remove' "${output}" 'Would remove release v2026.01.01.1Rmain+abc1234'
assert_eq 'cleanup: dry run sends no DELETE' 0 "$(deletes)"
if [[ "${output}" == *'Removing release'* || "${output}" == *'Removing tag'* ]]; then
    fail 'cleanup: dry run prints no "Removing" line' "${output}"
else
    pass 'cleanup: dry run prints no "Removing" line'
fi

# --- Kept tags that reference nothing ----------------------------------------------------------
set_routes "$(standard_routes | jq '.["GET /v2/acme/ninlil/manifests/latest"] = [200, {}]
    | .["GET /v2/acme/ninlil/manifests/2026.01.01.2"] = [200, {}]')"
run_script -- bash "${scripts}/cleanup.sh"
if [[ "${status}" -ne 0 ]]; then pass 'cleanup: kept tags with no manifests abort the run'; else fail 'cleanup: kept tags with no manifests abort the run' "${output}"; fi
assert_contains 'cleanup: the abort says why' "${output}" 'Refusing to delete'
if grep -q '^DELETE.*sha256' "${request_log}"; then
    fail 'cleanup: no digest is deleted when references are missing' "$(grep '^DELETE' "${request_log}")"
else
    pass 'cleanup: no digest is deleted when references are missing'
fi

finish
