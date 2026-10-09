#!/usr/bin/env bash
# Lints the pipeline's own code and runs its script self-tests. This is the "lint" job of
# .forgejo/workflows/pipeline.yml, started through
#   .forgejo/scripts/in-container.sh lint .forgejo/scripts/lint.sh
# so it runs in the lint toolchain of docker/ci.Dockerfile (shellcheck, actionlint, git, jq,
# curl, python3). Run the same command locally before pushing.
#
# Steps, in order. Every step runs even when an earlier one failed, so one run reports all
# problems:
#   1. shellcheck -x -S style on every *.sh under .forgejo/scripts, docker and src/script.
#   2. actionlint on .forgejo/workflows/*.yml with .forgejo/actionlint.yaml. actionlint has no
#      Forgejo mode, so the workflows are copied to a temporary .github/workflows with the
#      forgejo context renamed to github, which has the same shape. It does not treat
#      forgejo.ref_name as untrusted input; the "no ${{ }} in a run: body" rule is kept by review.
#   3. Every .forgejo/scripts/tests/*.test.sh.
#
# Inputs: none; run from anywhere (it changes to the repository root). Needs shellcheck,
# actionlint, find and the tools the tests need. Outputs: tool output on stdout/stderr and a
# summary line. Exit status: 0 when every step passes, 1 otherwise.

set -euo pipefail

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "${repository_root}"

failed=()

echo '== shellcheck'
mapfile -d '' shell_scripts < <(find .forgejo/scripts docker src/script -type f -name '*.sh' -print0 | sort -z)
if [[ ${#shell_scripts[@]} -eq 0 ]]; then
    echo 'No shell scripts found; the search paths are wrong.' >&2
    failed+=('shellcheck')
elif shellcheck -x --source-path=SCRIPTDIR -S style "${shell_scripts[@]}"; then
    echo "ok: ${#shell_scripts[@]} scripts"
else
    failed+=('shellcheck')
fi

echo '== actionlint'
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
# actionlint looks for the project root by finding a git repository.
git init --quiet "${work}"
mkdir -p "${work}/.github/workflows"
for workflow in .forgejo/workflows/*.yml; do
    sed -e 's/forgejo\./github./g' -e 's#\.forgejo/workflows/#.github/workflows/#g' \
        "${workflow}" > "${work}/.github/workflows/$(basename "${workflow}")"
done
if (cd "${work}" && actionlint -config-file "${repository_root}/.forgejo/actionlint.yaml"); then
    echo 'ok'
else
    failed+=('actionlint')
fi

echo '== script self-tests'
for test_script in .forgejo/scripts/tests/*.test.sh; do
    echo "-- ${test_script}"
    if ! bash "${test_script}"; then
        failed+=("${test_script}")
    fi
done

if [[ ${#failed[@]} -gt 0 ]]; then
    echo "Lint failed: ${failed[*]}" >&2
    exit 1
fi
echo 'Lint passed.'
