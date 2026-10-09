#!/usr/bin/env bash
# Tests bump-pins.sh against a throwaway copy of the pinned files, a fake `docker` that answers
# `buildx imagetools inspect` from canned JSON, and a local bare repository standing in for the
# actions mirror.
#
# Covers: --dry-run lists old -> new and edits nothing; a real run rewrites exactly the pins whose
# digest or tag changed (in every file that holds them) and leaves unchanged pins and every other
# line alone; a second run finds nothing to bump; an image that resolves to a single-architecture
# manifest, or a lookup that fails, exits non-zero without editing any file; the checkout pin
# moves to the newest tag of its major version, resolves an annotated tag to its commit, and
# ignores tags of other majors and pre-release tags; the native-libraries lock is replaced only
# when the refresh command's result differs (a failing refresh edits nothing), and the Microsoft
# repository package sum in ci.Dockerfile follows the downloaded file.
#
# Usage: .forgejo/scripts/tests/bump-pins.test.sh
# Needs: bash, git, jq, sed, curl, sha256sum. Never contacts a real registry or server.
# Exit status: 0 when every assertion passes, 1 otherwise.

set -uo pipefail

here="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
scripts="$(cd "${here}/.." && pwd)"
# shellcheck source=./assert.sh
source "${here}/assert.sh"

work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT
tree="${work}/tree"
digests="${work}/digests"
mirror="${work}/mirror"
mkdir -p "${tree}/docker" "${tree}/.forgejo/workflows" "${tree}/.forgejo/scripts" "${tree}/src/dotnet/tests/Ninlil.Dns.Server.QueryLog.Tests" "${digests}" "${work}/bin"

hex() { printf '%064d' "$1" | tr '0' "$2"; }
old_a="sha256:$(hex 1 a)"
old_b="sha256:$(hex 1 b)"
new_a="sha256:$(hex 1 c)"
new_b="sha256:$(hex 1 d)"

# The Microsoft repository package: a local file the script downloads through file://.
printf 'first deb' > "${work}/microsoft.deb"
deb_sum="$(sha256sum "${work}/microsoft.deb")"
deb_sum="${deb_sum%% *}"

# The lock refresh command: copies ${work}/lock-answer to the file named by its argument, or fails
# when ${work}/lock-fails exists.
cat > "${work}/bin/refresh" <<'REFRESH'
#!/usr/bin/env bash
[[ -f "${LOCK_FAILS}" ]] && { echo 'refresh failed' >&2; exit 1; }
cp "${LOCK_ANSWER}" "$1"
REFRESH
chmod +x "${work}/bin/refresh"

# The fake docker prints the JSON stored for the reference ("/" and ":" become "_").
cat > "${work}/bin/docker" <<'DOCKER'
#!/usr/bin/env bash
file="${FAKE_DIGESTS}/${4//[\/:]/_}"
[[ -f "${file}" ]] && cat "${file}" || { echo "unknown reference $4" >&2; exit 1; }
DOCKER
chmod +x "${work}/bin/docker"

# Stores the answer for one reference. Arguments: reference, digest, media type.
set_image() {
    local name="${1//[\/:]/_}"
    printf '{"mediaType":"%s","digest":"%s"}\n' "$3" "$2" > "${digests}/${name}"
}
index='application/vnd.oci.image.index.v1+json'

# The mirror: actions/checkout with tags v7.0.0 (lightweight), v7.0.1 (annotated), v7.1.0-rc1,
# v6.9.9 and v8.0.0.
git init -q -b main "${work}/seed"
git -C "${work}/seed" -c user.name=t -c user.email=t@t commit -q --allow-empty -m one
commit_old="$(git -C "${work}/seed" rev-parse HEAD)"
git -C "${work}/seed" -c user.name=t -c user.email=t@t commit -q --allow-empty -m two
commit_new="$(git -C "${work}/seed" rev-parse HEAD)"
git -C "${work}/seed" tag v7.0.0 "${commit_old}"
git -C "${work}/seed" tag v6.9.9 "${commit_new}"
git -C "${work}/seed" tag v8.0.0 "${commit_new}"
git -C "${work}/seed" tag v7.1.0-rc1 "${commit_new}"
git -C "${work}/seed" -c user.name=t -c user.email=t@t tag -a -m annotated v7.0.1 "${commit_new}"
mkdir -p "${mirror}/actions"
git clone -q --bare "${work}/seed" "${mirror}/actions/checkout"

write_tree() {
    cat > "${tree}/Dockerfile" <<DOCKERFILE
ARG RUNTIME_IMAGE=example.test/runtime:1.0@${old_a}
ARG FETCH_IMAGE=example.test/fetch:2@${old_b}
FROM \${RUNTIME_IMAGE} AS runtime
DOCKERFILE
    cat > "${tree}/docker/ci.Dockerfile" <<DOCKERFILE
ARG SDK_IMAGE=example.test/runtime:1.0@${old_a}
ARG MICROSOFT_REPOSITORY_DEB_SHA256=${deb_sum}
DOCKERFILE
    printf 'amd64\tlibnuma1\t1.0\n' > "${tree}/docker/native-libraries.lock"
    printf 'amd64\tlibnuma1\t1.0\n' > "${work}/lock-answer"
    printf 'images=(example.test/runtime:1.0@%s)\n' "${old_a}" > "${tree}/.forgejo/scripts/linux-packages-smoke.sh"
    printf '    image: example.test/fetch:2@%s\n    image: other\n' "${old_b}" > "${tree}/docker-compose.yml"
    printf 'new PostgreSqlBuilder("example.test/fetch:2@%s")\n' "${old_b}" \
        > "${tree}/src/dotnet/tests/Ninlil.Dns.Server.QueryLog.Tests/QueryLogPostgreSqlTestHost.cs"
    cat > "${tree}/.forgejo/workflows/one.yml" <<YAML
steps:
  - uses: 'actions/checkout@${commit_old}' # v7.0.0
  - uses: 'actions/other@v1'
YAML
    cp "${tree}/.forgejo/workflows/one.yml" "${tree}/.forgejo/workflows/two.yml"
}

# Runs bump-pins.sh on the tree. Output goes to $output, the exit code to $status.
run_bump() {
    output="$(env PATH="${work}/bin:${PATH}" BUMP_PINS_ROOT="${tree}" BUMP_PINS_DOCKER="${work}/bin/docker" \
        FAKE_DIGESTS="${digests}" LOCK_ANSWER="${work}/lock-answer" LOCK_FAILS="${work}/lock-fails" \
        BUMP_PINS_LOCK_REFRESH="${work}/bin/refresh" BUMP_PINS_MICROSOFT_DEB_URL="file://${work}/microsoft.deb" \
        ACTIONS_MIRROR_URL="${mirror}" bash "${scripts}/bump-pins.sh" "$@" 2>&1)"
    status=$?
}

checksum() { (cd "${tree}" && find . -type f -print0 | sort -z | xargs -0 sha256sum); }

# Images: runtime moves, fetch stays.
write_tree
set_image example.test/runtime:1.0 "${new_a}" "${index}"
set_image example.test/fetch:2 "${old_b}" "${index}"
before="$(checksum)"

# --- Dry run -----------------------------------------------------------------------------------
run_bump --dry-run
assert_eq 'bump: dry run exits 0' 0 "${status}"
assert_contains 'bump: dry run lists the changed image as old -> new' "${output}" "example.test/runtime:1.0: ${old_a} -> ${new_a}"
assert_contains 'bump: dry run lists the unchanged image' "${output}" "example.test/fetch:2: unchanged"
assert_contains 'bump: dry run lists the checkout move' "${output}" "v7.0.0 ${commit_old} -> v7.0.1 ${commit_new}"
assert_eq 'bump: dry run edits no file' "${before}" "$(checksum)"

# --- Real run ----------------------------------------------------------------------------------
run_bump
assert_eq 'bump: real run exits 0' 0 "${status}"
assert_eq 'bump: every file holding the moved image is rewritten (including the smoke script)' 3 "$(grep -rlF "example.test/runtime:1.0@${new_a}" "${tree}" | wc -l)"
assert_eq 'bump: the old digest of the moved image is gone' 0 "$(grep -rlF "${old_a}" "${tree}" | wc -l)"
assert_eq 'bump: the unchanged image keeps its digest in all three files' 3 "$(grep -rlF "example.test/fetch:2@${old_b}" "${tree}" | wc -l)"
assert_eq 'bump: both workflows move to the annotated tag peeled commit' 2 "$(grep -rlF "actions/checkout@${commit_new}' # v7.0.1" "${tree}/.forgejo" | wc -l)"
assert_eq 'bump: unrelated lines are untouched' 'image: other' "$(grep -o 'image: other' "${tree}/docker-compose.yml")"
assert_eq 'bump: an unrelated action is untouched' 2 "$(grep -rlF "actions/other@v1" "${tree}/.forgejo" | wc -l)"

run_bump
assert_contains 'bump: a second run has nothing to bump' "${output}" 'Nothing to bump.'

# --- Another major's tag never wins; a newer patch of the same major does -------------------------
git -C "${work}/seed" -c user.name=t -c user.email=t@t commit -q --allow-empty -m three
commit_newer="$(git -C "${work}/seed" rev-parse HEAD)"
git -C "${work}/seed" tag v7.0.10 "${commit_newer}"
git -C "${work}/seed" push -q "${mirror}/actions/checkout" --tags
run_bump
assert_contains 'bump: 7.0.10 sorts above 7.0.1' "${output}" "-> v7.0.10 ${commit_newer}"

# --- A single-architecture manifest is refused and nothing is edited ------------------------------
set_image example.test/fetch:2 "${new_b}" 'application/vnd.oci.image.manifest.v1+json'
set_image example.test/runtime:1.0 "sha256:$(hex 1 e)" "${index}"
before="$(checksum)"
run_bump
assert_eq 'bump: a non-index image exits 1' 1 "${status}"
assert_contains 'bump: the refusal names the media type' "${output}" 'not a multi-architecture index'
assert_eq 'bump: a refusal edits no file' "${before}" "$(checksum)"

# --- A failing lookup stops the run and edits nothing -------------------------------------------
rm "${digests}/example.test_fetch_2"
run_bump
assert_eq 'bump: an unresolvable image exits 1' 1 "${status}"
assert_eq 'bump: a failed lookup edits no file' "${before}" "$(checksum)"

# --- The native-libraries lock and the Microsoft repository sum ------------------------------------
write_tree
set_image example.test/runtime:1.0 "${old_a}" "${index}"
set_image example.test/fetch:2 "${old_b}" "${index}"
git -C "${work}/seed" tag -d v7.0.10 > /dev/null
rm -rf "${mirror}/actions/checkout"
git clone -q --bare "${work}/seed" "${mirror}/actions/checkout"
# Pin the checkout to the newest tag so only the lock and the sum can move.
sed -i "s/actions\/checkout@${commit_old}' # v7.0.0/actions\/checkout@${commit_new}' # v7.0.1/" "${tree}/.forgejo/workflows/"*.yml
run_bump
assert_contains 'bump: an identical lock is unchanged' "${output}" 'docker/native-libraries.lock: unchanged'
assert_contains 'bump: an identical sum is unchanged' "${output}" 'packages-microsoft-prod.deb: unchanged'
assert_contains 'bump: with nothing moved the run ends cleanly' "${output}" 'Nothing to bump.'
assert_eq 'bump: no candidate lock is left behind' 0 "$(find "${tree}" -name '*.lock.new' | wc -l)"

printf 'amd64\tlibnuma1\t2.0\n' > "${work}/lock-answer"
printf 'second deb' > "${work}/microsoft.deb"
new_deb_sum="$(sha256sum "${work}/microsoft.deb")"
new_deb_sum="${new_deb_sum%% *}"
before="$(checksum)"
run_bump --dry-run
assert_contains 'bump: dry run reports the changed lock' "${output}" 'docker/native-libraries.lock: changed'
assert_contains 'bump: dry run reports the sum move' "${output}" "${deb_sum} -> ${new_deb_sum}"
assert_eq 'bump: dry run edits neither the lock nor the sum' "${before}" "$(checksum)"
run_bump
assert_eq 'bump: a lock and sum bump exits 0' 0 "${status}"
assert_eq 'bump: the lock is replaced by the refreshed one' "$(cat "${work}/lock-answer")" "$(cat "${tree}/docker/native-libraries.lock")"
assert_eq 'bump: ci.Dockerfile carries the new sum' 1 "$(grep -cF "MICROSOFT_REPOSITORY_DEB_SHA256=${new_deb_sum}" "${tree}/docker/ci.Dockerfile")"
assert_eq 'bump: the old sum is gone' 0 "$(grep -cF "${deb_sum}" "${tree}/docker/ci.Dockerfile")"

printf 'amd64\tlibnuma1\t3.0\n' > "${work}/lock-answer"
touch "${work}/lock-fails"
before="$(checksum)"
run_bump
assert_eq 'bump: a failing lock refresh exits non-zero' 1 "${status}"
assert_eq 'bump: a failing lock refresh edits no file' "${before}" "$(checksum)"
rm "${work}/lock-fails"
rm "${work}/microsoft.deb"
before="$(checksum)"
run_bump
assert_eq 'bump: a failing download exits non-zero' nonzero "$([[ "${status}" -ne 0 ]] && echo nonzero || echo zero)"
assert_eq 'bump: a failing download edits no file' "${before}" "$(checksum)"

finish
