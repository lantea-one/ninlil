#!/usr/bin/env bash
# Runs `npm ci` in the current directory under a time limit and retries once when npm hangs.
#
# On the CI runner, npm has stopped in `npm ci` after reading every tarball from the cache and
# then sat idle until a person cancelled the job. A hung install never ends by itself, so each
# attempt is limited. A timeout is the only failure that is retried: any other npm failure (a
# lockfile mismatch, a registry error) is real and is returned at once.
#
# Used by .forgejo/scripts/build-admin-console.sh and by the "Install Dependencies" step of the
# test-admin-console job in .forgejo/workflows/pipeline.yml.
#
# Usage: npm-ci.sh [extra npm ci arguments]   (run it inside src/angular)
#
# Environment:
#   NPM_CI_TIMEOUT       Limit of one attempt, as a `timeout` duration. Default 10m. A warm-cache
#                        install takes a minute or two, so the default leaves a wide margin.
#   NPM_CI_KILL_AFTER    Grace between the TERM and the KILL signal. Default 20s.
#
# Output: npm's output, and a message on stderr for each timeout.
# Exit status: npm's status; 1 when both attempts hang (message "npm hung"); 2 when `timeout`
# itself is unusable.

set -uo pipefail

limit="${NPM_CI_TIMEOUT:-10m}"
kill_after="${NPM_CI_KILL_AFTER:-20s}"

for attempt in 1 2; do
    # --kill-after: an npm stuck in a syscall can ignore TERM, so KILL follows after a grace.
    # Without --foreground, timeout runs npm in its own process group and signals the whole
    # group, so npm's child processes (lifecycle scripts) die with it.
    timeout --kill-after="${kill_after}" "${limit}" npm ci --no-audit --no-fund "$@"
    status=$?
    # timeout exits 124 when the limit expired and 137 when it had to send KILL.
    if [[ "${status}" -ne 124 && "${status}" -ne 137 ]]; then
        exit "${status}"
    fi
    echo "npm ci hung: no result after ${limit} (attempt ${attempt} of 2)." >&2
    # A half-written node_modules from the killed attempt must not reach the retry.
    rm -rf node_modules
done

echo "npm hung twice in npm ci (limit ${limit} each); giving up. Check the shared npm cache and the runner." >&2
exit 1
