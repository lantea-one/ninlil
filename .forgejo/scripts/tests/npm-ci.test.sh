#!/usr/bin/env bash
# Tests npm-ci.sh with a fake `npm` first on PATH.
#
# Covers: a succeeding npm passes through with its arguments; a failing npm is returned at once
# without a retry; a hanging npm is killed at the limit and retried once, with node_modules
# removed before the retry; a first hang followed by success passes; two hangs fail with the
# "npm hung" message; and a fake that ignores TERM is still killed (the --kill-after path).
#
# Usage: .forgejo/scripts/tests/npm-ci.test.sh
# Needs: bash, coreutils timeout. Writes only to a temporary directory it removes on exit. Each
# hang test waits about two seconds (the limit is set to 1s).
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
script="$(cd "${here}/.." && pwd)/npm-ci.sh"
# shellcheck source=./assert.sh
source "${here}/assert.sh"

work_root="$(mktemp -d)"
trap 'rm -rf "${work_root}"' EXIT
mkdir -p "${work_root}/bin" "${work_root}/project"
calls="${work_root}/calls"

# Writes a fake npm. Argument: its behaviour for one call, as shell text; $calls_so_far is the
# number of earlier calls.
write_npm() {
    cat > "${work_root}/bin/npm" <<FAKE
#!/usr/bin/env bash
calls_so_far=\$(wc -l < "${calls}" 2>/dev/null || echo 0)
echo "\$*" >> "${calls}"
$1
FAKE
    chmod +x "${work_root}/bin/npm"
}

# Runs the script in the project folder. Sets output, status and call_count.
run() {
    : > "${calls}"
    mkdir -p "${work_root}/project/node_modules"
    output="$(cd "${work_root}/project" && PATH="${work_root}/bin:${PATH}" \
        NPM_CI_TIMEOUT=1s NPM_CI_KILL_AFTER=1s bash "${script}" "$@" 2>&1)"
    status=$?
    call_count=$(wc -l < "${calls}")
}

write_npm 'exit 0'
run --extra-flag
assert_eq 'npm-ci: success exits 0' 0 "${status}"
assert_eq 'npm-ci: success runs npm once' 1 "${call_count}"
assert_contains 'npm-ci: extra arguments reach npm' "$(cat "${calls}")" 'ci --no-audit --no-fund --extra-flag'

write_npm 'exit 3'
run
assert_eq 'npm-ci: a real npm failure keeps its status' 3 "${status}"
assert_eq 'npm-ci: a real npm failure is not retried' 1 "${call_count}"

# The single quotes are deliberate: the fake script expands the variable, not this test.
# shellcheck disable=SC2016
write_npm 'if [[ "${calls_so_far}" -eq 0 ]]; then sleep 30; fi; test ! -e node_modules'
run
assert_eq 'npm-ci: a hang then success exits 0' 0 "${status}"
assert_eq 'npm-ci: a hang is retried once' 2 "${call_count}"
assert_contains 'npm-ci: the hang is reported' "${output}" 'npm ci hung'

write_npm 'sleep 30'
run
assert_eq 'npm-ci: two hangs exit 1' 1 "${status}"
assert_eq 'npm-ci: two hangs make two attempts' 2 "${call_count}"
assert_contains 'npm-ci: the final message says npm hung' "${output}" 'npm hung twice'
if [[ -e "${work_root}/project/node_modules" ]]; then
    fail 'npm-ci: node_modules is removed after a hang' 'it still exists'
else
    pass 'npm-ci: node_modules is removed after a hang'
fi

write_npm 'trap "" TERM; while true; do sleep 1; done'
run
assert_eq 'npm-ci: an npm that ignores TERM is killed and fails after two attempts' 1 "${status}"
assert_eq 'npm-ci: the TERM-ignoring npm is attempted twice' 2 "${call_count}"

finish
