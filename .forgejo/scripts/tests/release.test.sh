#!/usr/bin/env bash
# Tests release.sh (upload and draft lookup) against a fake Forgejo server (fake-forgejo.py)
# with the real curl and jq.
#
# Covers: stage puts the Deb and RPM install commands in the release notes; publish merges the per-file checksums; a successful upload exits 0 and the token is on no curl command line; a failing asset
# upload exits non-zero; a failing second page of the draft listing exits non-zero instead of
# reading as "no draft"; a missing draft exits non-zero.
#
# Usage: .forgejo/scripts/tests/release.test.sh
# Needs: bash, python3, curl, jq. Never contacts a real server.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./assert.sh
source "${here}/assert.sh"
# shellcheck source=./harness.sh
source "${here}/harness.sh"

token='tok"en\with\\odd"chars'
tag='v2026.01.01.1Rmain+abc1234'
releases="/api/v1/repos/acme/ninlil/releases"
artifact="${work_root}/ninlil-linux-amd64"
printf 'binary' > "${artifact}"

draft_routes() {
    cat <<JSON
{
  "GET ${releases}#1": [200, [{"id": 7, "tag_name": "${tag}", "draft": true}]],
  "GET ${releases}/7/assets": [200, []],
  "POST ${releases}/7/assets": [$1, {}]
}
JSON
}

# --- Stage: the release notes carry the Deb and RPM install commands -----------------------------
stage_env=(RELEASE_TAG="${tag}" RELEASE_PRERELEASE=false COMMIT_SHA=abc1234def REF_NAME=main
    IMAGE_VERSION=2026.01.01.1 ARCH_VERSION=2026.01.01.1)
set_routes "{\"GET ${releases}#1\": [200, []], \"POST ${releases}\": [201, {\"id\": 9}]}"
run_script "${stage_env[@]}" DEB_VERSION=2026.01.01.1 RPM_VERSION=2026.01.01.1 RPM_RELEASE=1 -- bash "${scripts}/release.sh" stage
assert_eq 'release: stage exits 0' 0 "${status}"
assert_contains 'release: the notes install the Deb with apt' "$(cat "${argv_log}")" 'sudo apt install ./ninlil_2026.01.01.1_amd64.deb'
assert_contains 'release: the notes name the Windows zip' "$(cat "${argv_log}")" 'ninlil-windows-amd64.zip'
assert_contains 'release: the notes install the RPM with dnf' "$(cat "${argv_log}")" 'sudo dnf install ./ninlil-2026.01.01.1-1.x86_64.rpm'
set_routes "{}"
run_script "${stage_env[@]}" -- bash "${scripts}/release.sh" stage
if [[ "${status}" -ne 0 && "${output}" == *'DEB_VERSION is required'* ]]; then pass 'release: stage without the package versions fails'; else fail 'release: stage without the package versions fails' "${output}"; fi

# --- Successful upload -------------------------------------------------------------------------
set_routes "$(draft_routes 201)"
run_script RELEASE_TAG="${tag}" -- bash "${scripts}/release.sh" upload "${artifact}"
assert_eq 'release: upload exits 0' 0 "${status}"
assert_eq 'release: upload posts the file and its checksum' 2 "$(grep -c '^POST' "${request_log}")"
if grep -qF -- "${token}" "${argv_log}"; then
    fail 'release: token is not on any curl command line' 'found in argv'
else
    pass 'release: token is not on any curl command line'
fi
assert_eq 'release: server receives the token intact' "token ${token}" "$(grep '^POST' "${request_log}" | head -n 1 | cut -f 3)"
assert_eq 'release: no config file is left behind' 0 "$(find "${run_temp}" -mindepth 1 | wc -l)"

# --- Publish merges the checksum attachments and un-drafts the release ---------------------------
set_routes "{\"GET ${releases}#1\": [200, [{\"id\": 7, \"tag_name\": \"${tag}\", \"draft\": true}]],
  \"GET ${releases}/7/assets\": [200, [
    {\"id\": 21, \"name\": \"b.sha256\", \"browser_download_url\": \"${server_url}/dl/b\"},
    {\"id\": 22, \"name\": \"a.sha256\", \"browser_download_url\": \"${server_url}/dl/a\"}]],
  \"GET /dl/a\": [200, \"1111  a\\n\"], \"GET /dl/b\": [200, \"2222  b\\n\"]}"
run_script RELEASE_TAG="${tag}" -- bash "${scripts}/release.sh" publish
assert_eq 'release: publish exits 0' 0 "${status}"
assert_contains 'release: publish prints the merged, sorted checksums' "${output}" $'1111  a\n2222  b'
assert_eq 'release: publish deletes both checksum attachments' 2 "$(grep -c '^DELETE' "${request_log}")"
assert_eq 'release: publish clears the draft flag' 1 "$(grep -c '^PATCH' "${request_log}")"

# --- Asset API failing -------------------------------------------------------------------------
set_routes "$(draft_routes 500)"
run_script RELEASE_TAG="${tag}" -- bash "${scripts}/release.sh" upload "${artifact}"
if [[ "${status}" -ne 0 ]]; then pass 'release: a failing asset upload exits non-zero'; else fail 'release: a failing asset upload exits non-zero' "${output}"; fi

# --- Failed second page of the draft listing ---------------------------------------------------
set_routes "{\"GET ${releases}#1\": [200, [{\"id\": 3, \"tag_name\": \"other\", \"draft\": true}]],
  \"GET ${releases}#2\": [500, {\"message\": \"boom\"}]}"
run_script RELEASE_TAG="${tag}" -- bash "${scripts}/release.sh" upload "${artifact}"
if [[ "${status}" -ne 0 ]]; then pass 'release: a failed draft listing page exits non-zero'; else fail 'release: a failed draft listing page exits non-zero' "${output}"; fi
if [[ "${output}" == *'No draft release exists'* ]]; then
    fail 'release: a failed page is not reported as a missing draft' "${output}"
else
    pass 'release: a failed page is not reported as a missing draft'
fi

# --- No draft ----------------------------------------------------------------------------------
set_routes "{\"GET ${releases}#1\": [200, [{\"id\": 3, \"tag_name\": \"other\", \"draft\": true}]]}"
run_script RELEASE_TAG="${tag}" -- bash "${scripts}/release.sh" upload "${artifact}"
if [[ "${status}" -ne 0 ]]; then pass 'release: a missing draft exits non-zero'; else fail 'release: a missing draft exits non-zero' "${output}"; fi
assert_contains 'release: a missing draft says so' "${output}" 'No draft release exists'

finish
