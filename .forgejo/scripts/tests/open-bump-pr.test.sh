#!/usr/bin/env bash
# Tests open-bump-pr.sh against a fake Forgejo server (fake-forgejo.py) and a local bare
# repository standing in for origin, with the real git, curl and jq.
#
# Covers: a clean tree opens nothing; a changed tree is committed on bump/pins-<date>, pushed to
# origin and opened as a pull request into the base branch; the token is on no curl command line;
# an already-open bump/pins-* pull request stops a second one; a failing pull request creation
# exits non-zero.
#
# Usage: .forgejo/scripts/tests/open-bump-pr.test.sh
# Needs: bash, git, python3, curl, jq. Never contacts a real server.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
# shellcheck source=./assert.sh
source "${here}/assert.sh"
# shellcheck source=./harness.sh
source "${here}/harness.sh"

token='tok"en\with\\odd"chars'
pulls='/api/v1/repos/acme/ninlil/pulls'

origin="${work_root}/origin.git"
clone="${work_root}/clone"
git init -q --bare -b develop "${origin}"
git clone -q "${origin}" "${clone}" 2> /dev/null
git -C "${clone}" -c user.name=t -c user.email=t@t checkout -q -b develop
printf 'old\n' > "${clone}/pins.txt"
git -C "${clone}" add pins.txt
git -C "${clone}" -c user.name=t -c user.email=t@t commit -q -m seed
git -C "${clone}" push -q origin develop 2> /dev/null

# Runs the script inside the clone (not a subshell, so $status and $output survive).
# Argument: the branch date.
run_pr() {
    pushd "${clone}" > /dev/null || exit 1
    run_script BASE_BRANCH=develop "BUMP_DATE=$1" -- bash "${scripts}/open-bump-pr.sh"
    popd > /dev/null || exit 1
}

# --- A clean tree opens nothing -----------------------------------------------------------------
set_routes '{}'
run_pr 2026-10-12
assert_eq 'pr: a clean tree exits 0' 0 "${status}"
assert_contains 'pr: a clean tree says there is nothing to bump' "${output}" 'Nothing to bump.'
assert_eq 'pr: a clean tree makes no API call' 0 "$(wc -l < "${request_log}")"

# --- A changed tree is pushed and opened ---------------------------------------------------------
printf 'new\n' > "${clone}/pins.txt"
set_routes "{\"POST ${pulls}\": [201, {\"number\": 42}]}"
run_pr 2026-10-12
assert_eq 'pr: a changed tree exits 0' 0 "${status}"
assert_contains 'pr: the pull request number is reported' "${output}" 'Opened pull request #42 from bump/pins-2026-10-12 into develop.'
assert_eq 'pr: the branch exists on origin' 1 "$(git -C "${origin}" branch --list 'bump/pins-2026-10-12' | wc -l)"
assert_eq 'pr: the branch carries the change' 'new' "$(git -C "${origin}" show bump/pins-2026-10-12:pins.txt)"
assert_eq 'pr: develop on origin is untouched' 'old' "$(git -C "${origin}" show develop:pins.txt)"
assert_eq 'pr: one pull request is created' 1 "$(grep -c "^POST" "${request_log}")"
if grep -qF -- "${token}" "${argv_log}"; then
    fail 'pr: token is not on any curl command line' 'found in argv'
else
    pass 'pr: token is not on any curl command line'
fi

# --- An open bump pull request stops a second one -----------------------------------------------
git -C "${clone}" checkout -q develop
git -C "${clone}" checkout -q -- .
printf 'newer\n' > "${clone}/pins.txt"
set_routes "{\"GET ${pulls}#1\": [200, [{\"number\": 7, \"head\": {\"ref\": \"bump/pins-2026-10-05\"}}]]}"
run_pr 2026-10-19
assert_eq 'pr: an open bump pull request exits 0' 0 "${status}"
assert_contains 'pr: it names the open pull request' "${output}" 'Pull request #7'
assert_eq 'pr: it creates no pull request' 0 "$(grep -c "^POST" "${request_log}")"
assert_eq 'pr: it pushes no branch' 0 "$(git -C "${origin}" branch --list 'bump/pins-2026-10-19' | wc -l)"

# --- A failing pull request creation fails the run -----------------------------------------------
set_routes "{\"POST ${pulls}\": [500, {}]}"
run_pr 2026-10-19
assert_eq 'pr: a failed pull request creation exits non-zero' 1 "$([[ "${status}" -ne 0 ]] && echo 1 || echo 0)"

finish
