#!/usr/bin/env bash
# Tests sync-labels.sh against a fake Forgejo server (fake-forgejo.py) with the real curl and jq.
#
# Covers: an empty tracker gets one POST per label and nothing else; a label with the wrong
# colour gets exactly one PATCH to its id (a leading "#" and letter case in the tracker's colour
# do not count as a difference); a tracker that is up to date receives no write; a labels file
# with an exclusive label without a "/" or with a duplicate name makes the script exit non-zero
# before any request; and the token never appears on a curl command line.
#
# Usage: .forgejo/scripts/tests/sync-labels.test.sh
# Needs: bash, python3, curl, jq. Never contacts a real server.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./assert.sh
source "${here}/assert.sh"
# shellcheck source=./harness.sh
source "${here}/harness.sh"

token='tok"en\with\\odd"chars'
api='/api/v1'
labels_file="$(cd "${here}/../.." && pwd)/labels.json"
label_count="$(jq 'length' "${labels_file}")"
bad_file="${work_root}/bad-labels.json"

# Runs the script for acme/ninlil. Arguments: labels file.
run_sync() {
    run_script "FORGEJO_API=${server_url}${api}" "LABELS_FILE=$1" -- bash "${scripts}/sync-labels.sh" acme/ninlil
}

# Counts logged requests of one method.
count_requests() { grep -c "^$1" "${request_log}" || true; }

# The tracker's label list for the committed file, with ids 1..n. Argument: a jq filter applied
# to the list, for example one that changes a colour.
existing_labels() {
    jq -c "[to_entries[] | .value + {id: (.key + 1)}] | $1" "${labels_file}"
}

# --- Empty tracker -----------------------------------------------------------------------------
set_routes '{}'
run_sync "${labels_file}"
assert_eq 'sync-labels: empty tracker exits 0' 0 "${status}"
assert_eq 'sync-labels: empty tracker gets one POST per label' "${label_count}" "$(count_requests POST)"
assert_eq 'sync-labels: empty tracker gets no PATCH' 0 "$(count_requests PATCH)"
assert_eq 'sync-labels: empty tracker reports every label created' "${label_count}" "$(grep -c '^created ' <<< "${output}")"
if grep -qF -- "${token}" "${argv_log}"; then
    fail 'sync-labels: token is not on any curl command line' 'found in argv'
else
    pass 'sync-labels: token is not on any curl command line'
fi

# --- One wrong colour --------------------------------------------------------------------------
set_routes "$(jq -n --arg api "${api}" --argjson list "$(existing_labels 'map(if .name == "kind/bug" then .color = "000000" else . end)')" \
    '{("GET " + $api + "/repos/acme/ninlil/labels#1"): [200, $list]}')"
run_sync "${labels_file}"
assert_eq 'sync-labels: one wrong colour exits 0' 0 "${status}"
assert_eq 'sync-labels: one wrong colour gets one PATCH' 1 "$(count_requests PATCH)"
assert_eq 'sync-labels: one wrong colour gets no POST' 0 "$(count_requests POST)"
bug_id="$(jq 'map(.name) | index("kind/bug") + 1' "${labels_file}")"
assert_contains 'sync-labels: the PATCH targets the label id' "$(grep '^PATCH' "${request_log}")" "${api}/repos/acme/ninlil/labels/${bug_id}"
assert_contains 'sync-labels: the label is reported updated' "${output}" 'updated kind/bug'

# --- Up to date --------------------------------------------------------------------------------
# The tracker may return colours with "#" and in upper case; neither is a difference.
set_routes "$(jq -n --arg api "${api}" --argjson list "$(existing_labels 'map(.color = "#" + (.color | ascii_upcase))')" \
    '{("GET " + $api + "/repos/acme/ninlil/labels#1"): [200, $list]}')"
run_sync "${labels_file}"
assert_eq 'sync-labels: up-to-date tracker exits 0' 0 "${status}"
assert_eq 'sync-labels: up-to-date tracker gets no POST' 0 "$(count_requests POST)"
assert_eq 'sync-labels: up-to-date tracker gets no PATCH' 0 "$(count_requests PATCH)"
assert_eq 'sync-labels: up-to-date tracker reports every label unchanged' "${label_count}" "$(grep -c '^unchanged ' <<< "${output}")"

# --- Invalid files -----------------------------------------------------------------------------
set_routes '{}'
printf '%s' '[{"name":"flat","color":"ffffff","description":"x","exclusive":true}]' > "${bad_file}"
run_sync "${bad_file}"
if [[ "${status}" -ne 0 ]]; then pass 'sync-labels: exclusive label without a slash exits non-zero'; else fail 'sync-labels: exclusive label without a slash exits non-zero' "${output}"; fi
assert_eq 'sync-labels: exclusive label without a slash sends nothing' 0 "$(wc -l < "${request_log}")"

printf '%s' '[{"name":"a/b","color":"ffffff","description":"x","exclusive":true},{"name":"a/b","color":"000000","description":"y","exclusive":false}]' > "${bad_file}"
run_sync "${bad_file}"
if [[ "${status}" -ne 0 ]]; then pass 'sync-labels: duplicate name exits non-zero'; else fail 'sync-labels: duplicate name exits non-zero' "${output}"; fi
assert_eq 'sync-labels: duplicate name sends nothing' 0 "$(wc -l < "${request_log}")"

finish
