#!/usr/bin/env bash
# Tests compute-version.sh against bare local repositories standing in for origin.
#
# Every case builds a fresh bare origin, seeds the tags the case needs, runs the script from a
# clone and checks its exit code and output. Dates come from `date -u`, so the tests never depend
# on a fixed day (a run that straddles UTC midnight can fail; rerun it).
#
# Usage: .forgejo/scripts/tests/compute-version.test.sh
# Needs: bash, git. Writes only to a temporary directory it removes on exit.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$(cd "${here}/.." && pwd)/compute-version.sh"
# shellcheck source=./assert.sh
source "${here}/assert.sh"

work_root="$(mktemp -d)"
trap 'rm -rf "${work_root}"' EXIT

export GIT_AUTHOR_NAME=test GIT_AUTHOR_EMAIL=test@example.invalid
export GIT_COMMITTER_NAME=test GIT_COMMITTER_EMAIL=test@example.invalid

today="$(date -u +%Y.%m.%d)"
full_sha="1234567abcdef1234567abcdef1234567abcdef1"

# Creates a clone ("repo") whose origin is a fresh bare repository, then pushes the tags given as
# arguments. Sets $repo.
new_repo() {
    local case_dir
    case_dir="$(mktemp -d -p "${work_root}")"
    git init --quiet --bare "${case_dir}/origin.git"
    git init --quiet "${case_dir}/repo"
    repo="${case_dir}/repo"
    git -C "${repo}" remote add origin "${case_dir}/origin.git"
    git -C "${repo}" commit --quiet --allow-empty -m seed
    local tag
    for tag in "$@"; do
        git -C "${repo}" tag "${tag}"
    done
    if (($# > 0)); then
        git -C "${repo}" push --quiet origin --tags
    fi
}

# Runs compute-version.sh in $repo. Arguments: kind, ref name, [commit sha]. Sets $status and
# $output (stdout and stderr together).
run_version() {
    output="$(cd "${repo}" && BUILD_KIND="$1" REF_NAME="$2" COMMIT_SHA="${3:-${full_sha}}" bash "${script}" 2>&1)"
    status=$?
}

# --- release builds ---------------------------------------------------------------------------

new_repo "v${today}.1Rmain+1111111"
run_version release "v${today}.1"
assert_eq "release ignores a same-day pre-release tag: exit code" 0 "${status}"
assert_contains "release ignores a same-day pre-release tag: version" "${output}" "version=v${today}.1R1234567"

new_repo
run_version release "v${today}.1"
assert_contains "release deb version: plain date version" "${output}" "deb-version=${today}.1"
assert_contains "release rpm version: plain date version" "${output}" "rpm-version=${today}.1"
assert_contains "release rpm release: 1" "${output}" "rpm-release=1"

new_repo "v${today}.1R7654321"
run_version release "v${today}.1"
assert_eq "release already published from another commit: exit code" 1 "${status}"
assert_contains "release already published from another commit: message" "${output}" "was already published"

new_repo "v${today}.1R1234567"
run_version release "v${today}.1"
assert_eq "release rerun from the same commit: exit code" 0 "${status}"
assert_contains "release rerun from the same commit: version" "${output}" "version=v${today}.1R1234567"

new_repo "v${today}.1R7654321" "v${today}.1Rmain+1111111"
run_version release "v${today}.1"
assert_eq "published release beside a pre-release tag: exit code" 1 "${status}"

new_repo
git -C "${repo}" remote set-url origin ../missing.git
run_version release "v${today}.1"
assert_eq "release with an unreachable origin fails: exit code" 1 "${status}"
assert_contains "release with an unreachable origin fails: message" "${output}" "Cannot list tags on origin"

new_repo
run_version release "v2026.02.30.1"
assert_eq "release on an impossible date fails: exit code" 1 "${status}"

new_repo
run_version release "v${today}.65536"
assert_eq "release increment 65536 fails: exit code" 1 "${status}"

# --- pre-release builds -----------------------------------------------------------------------

new_repo "v${today}.2Rdevelop+aaaaaaa" "v${today}.5Rmain+bbbbbbb"
run_version prerelease develop
assert_eq "pre-release counts only its own branch: exit code" 0 "${status}"
assert_contains "pre-release counts only its own branch: version" "${output}" "version=v${today}.3Rdevelop+1234567"

new_repo
git -C "${repo}" remote set-url origin ../missing.git
run_version prerelease develop
assert_eq "pre-release with an unreachable origin fails: exit code" 1 "${status}"

new_repo "v${today}.2Rdevelop+aaaaaaa"
run_version prerelease develop
assert_contains "pre-release deb version: tilde suffix" "${output}" "deb-version=${today}.3~develop.1234567"
assert_contains "pre-release rpm version: plain date version" "${output}" "rpm-version=${today}.3"
assert_contains "pre-release rpm release: zero-prefixed" "${output}" "rpm-release=0.develop.1234567"

new_repo
run_version prerelease "Feature/DoT_tuning"
assert_contains "pre-release rpm release: hyphens become underscores" "${output}" "rpm-release=0.feature_dot_tuning.1234567"
assert_eq "pre-release branch label: exit code" 0 "${status}"
assert_contains "pre-release branch label: image version" "${output}" "image-version=${today}.1-feature-dot-tuning-1234567"

finish
