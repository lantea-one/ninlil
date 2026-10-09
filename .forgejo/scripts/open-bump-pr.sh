#!/usr/bin/env bash
# Turns the edits bump-pins.sh left in the working tree into a pull request.
#
# It commits the changes on a new branch bump/pins-<yyyy-mm-dd>, pushes it, and opens a pull
# request into BASE_BRANCH through the Forgejo API. The push build of that branch runs the full
# pipeline and the pull request runs the verify build, so a pin that breaks the build never
# reaches the base branch unseen. Nothing is merged here; a maintainer reviews and merges.
#
# It does nothing when the tree is clean, and when a pull request from a bump/pins-* branch is
# already open, so a weekly schedule never stacks pull requests.
#
# Inputs (environment):
#   BASE_BRANCH         the branch the pull request targets; also the branch checked out
#   NINLIL_CI_TOKEN     a Forgejo token that may push and open pull requests. A push by this token
#                       (unlike the job's own) starts the branch's build.
#   FORGEJO_SERVER_URL / GITHUB_SERVER_URL, FORGEJO_REPOSITORY / GITHUB_REPOSITORY
#                       set by the Actions runner
#   BUMP_DATE           the date in the branch name (default: today, UTC)
#
# The token never appears on a command line: git receives it as an Authorization header through
# GIT_CONFIG_* environment variables, and curl through a config file (forgejo-api.sh).
#
# Outputs: progress lines on stdout. Exits non-zero when the push or an API call fails.

set -euo pipefail
shopt -s inherit_errexit

: "${BASE_BRANCH:?BASE_BRANCH is required}"
: "${NINLIL_CI_TOKEN:?NINLIL_CI_TOKEN is required}"

# shellcheck source=./forgejo-api.sh
source "$(dirname "${BASH_SOURCE[0]}")/forgejo-api.sh"

server="${FORGEJO_SERVER_URL:-${GITHUB_SERVER_URL:?no server URL in the environment}}"
repository="${FORGEJO_REPOSITORY:-${GITHUB_REPOSITORY:?no repository in the environment}}"
FORGEJO_API="${server}/api/v1/repos/${repository}"
branch="bump/pins-${BUMP_DATE:-$(date -u +%F)}"

if [[ -z "$(git status --porcelain)" ]]; then
    echo "Nothing to bump."
    exit 0
fi

open_pulls="${forgejo_work}/pulls.jsonl"
forgejo_paginate '/pulls?state=open' "${open_pulls}"
existing="$(jq -rs '[.[] | select(.head.ref | startswith("bump/pins-")) | .number][0] // empty' "${open_pulls}")"
if [[ -n "${existing}" ]]; then
    echo "Pull request #${existing} from a bump/pins-* branch is still open; not opening another."
    exit 0
fi

git checkout -q -B "${branch}"
git add --update
git -c user.name='Ninlil CI' -c user.email='ninlil-ci@noreply.invalid' commit -q \
    -m 'Base images and the checkout action are now pinned to their newest digests and commits'
summary="$(git show --stat --format= HEAD)"

# The header lives in the environment, not in argv or a file; --force is safe because only this
# script writes bump/pins-* branches.
GIT_CONFIG_COUNT=1 \
    GIT_CONFIG_KEY_0=http.extraHeader \
    GIT_CONFIG_VALUE_0="Authorization: token ${NINLIL_CI_TOKEN}" \
    git push --force origin "${branch}"

body="Weekly refresh by bump-pins.yml. Each pinned base image moved to the current multi-architecture index digest of its tag, and actions/checkout to the newest tag of its major version.

\`\`\`
${summary}
\`\`\`

Review the diff, wait for the build, then merge."
payload="$(jq -n --arg title 'Refresh pinned base images and the checkout action' \
    --arg head "${branch}" --arg base "${BASE_BRANCH}" --arg body "${body}" \
    '{title: $title, head: $head, base: $base, body: $body}')"
number="$(forgejo_api POST '/pulls' --header 'Content-Type: application/json' --data "${payload}" | jq -r '.number')"
echo "Opened pull request #${number} from ${branch} into ${BASE_BRANCH}."
