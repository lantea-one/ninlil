#!/usr/bin/env bash
# Creates and publishes Forgejo releases for the pipeline.
#
#   release.sh stage <directory>   Creates a draft release for RELEASE_TAG and attaches every file
#                                  in <directory> to it. A draft is invisible to users and creates
#                                  no git tag, so a pipeline that fails after this step leaves
#                                  nothing half-published behind (cleanup.sh removes stale drafts).
#                                  A draft left over from an earlier attempt at the same tag is
#                                  replaced.
#   release.sh publish             Publishes the draft for RELEASE_TAG, which creates its git tag.
#                                  The pipeline runs this only once every artifact -- binaries and
#                                  container image -- has been built and uploaded.
#
# Environment:
#   NINLIL_CI_TOKEN     a Forgejo access token with repository write access
#   RELEASE_TAG         the release's tag and name (the computed version)
#   RELEASE_PRERELEASE  "true" or "false" (stage only)
#   IMAGE_VERSION       the container image tag (stage only, for the release notes)
#   PACKAGE_VERSION     the NuGet package version (stage only, for the release notes)
#   COMMIT_SHA          the commit being released (stage only)
#   REF_NAME            the branch being built (stage only, for the release notes)

set -euo pipefail

: "${NINLIL_CI_TOKEN:?NINLIL_CI_TOKEN is required}"
: "${RELEASE_TAG:?RELEASE_TAG is required}"

server="${FORGEJO_SERVER_URL:-${GITHUB_SERVER_URL:?no server URL in the environment}}"
repository="${FORGEJO_REPOSITORY:-${GITHUB_REPOSITORY:?no repository in the environment}}"
api="${server}/api/v1/repos/${repository}"
image="${server#https://}/${repository,,}"

# Calls the Forgejo API, failing on any HTTP error and printing the response body.
forgejo() {
    local method="$1" path="$2"
    shift 2
    curl --fail-with-body --silent --show-error \
        --request "${method}" \
        --header "Authorization: token ${NINLIL_CI_TOKEN}" \
        --header 'Accept: application/json' \
        "$@" \
        "${api}${path}"
}

# Prints the id of the draft release for RELEASE_TAG, or nothing when there is none.
find_draft() {
    local page=1 releases id
    while :; do
        releases="$(forgejo GET "/releases?draft=true&limit=50&page=${page}")"
        # An empty page means every release has been examined.
        if [[ "$(jq 'length' <<< "${releases}")" -eq 0 ]]; then
            return
        fi
        id="$(jq -r --arg tag "${RELEASE_TAG}" \
            '[.[] | select(.draft and .tag_name == $tag) | .id][0] // empty' <<< "${releases}")"
        if [[ -n "${id}" ]]; then
            echo "${id}"
            return
        fi
        page=$((page + 1))
    done
}

stage() {
    local directory="$1"
    : "${RELEASE_PRERELEASE:?RELEASE_PRERELEASE is required}"
    : "${COMMIT_SHA:?COMMIT_SHA is required}"

    local existing
    existing="$(find_draft)"
    if [[ -n "${existing}" ]]; then
        echo "Replacing earlier draft ${existing} for ${RELEASE_TAG}"
        forgejo DELETE "/releases/${existing}" > /dev/null
    fi

    # The Arch Linux package version, read from the package files in the directory being attached.
    local package_file
    package_file="$(find "${directory}" -maxdepth 1 -name 'ninlil-bin-*-x86_64.pkg.tar.zst' -printf '%f\n' | head -n 1)"
    PKGVER="${package_file#ninlil-bin-}"
    PKGVER="${PKGVER%-1-x86_64.pkg.tar.zst}"

    local kind='Release'
    if [[ "${RELEASE_PRERELEASE}" == 'true' ]]; then
        kind="Pre-release build of \`${REF_NAME}\`; it is removed automatically after 24 hours"
    fi

    local body
    body="$(cat <<EOF
${kind}.

**Commit:** \`${COMMIT_SHA}\`

### Binaries

Self-contained single-file executables; no .NET installation is needed. Download the one for your
platform, mark it executable, and run it. Its data directory is created next to the executable.

| Platform | File |
| --- | --- |
| Linux x86-64 | \`ninlil-linux-amd64\` |
| Linux ARM64 | \`ninlil-linux-arm64\` |
| macOS Intel | \`ninlil-macos-amd64\` |
| macOS Apple Silicon | \`ninlil-macos-arm64\` |

Verify a download with \`sha256sum --check --ignore-missing SHA256SUMS\`.

### Arch Linux

\`ninlil-bin\` packages for this build, installable directly:

\`\`\`sh
sudo pacman -U ${server}/${repository}/releases/download/${RELEASE_TAG//+/%2B}/ninlil-bin-${PKGVER}-1-x86_64.pkg.tar.zst
\`\`\`

(\`...-aarch64.pkg.tar.zst\` for ARM64.) The package installs a \`ninlil\` systemd service; start it
with \`sudo systemctl enable --now ninlil\`.

### Container image

\`\`\`sh
docker pull ${image}:${IMAGE_VERSION}
\`\`\`

\`linux/amd64\` and \`linux/arm64\`.
EOF
)"

    local payload id
    payload="$(jq -n \
        --arg tag "${RELEASE_TAG}" \
        --arg target "${COMMIT_SHA}" \
        --arg body "${body}" \
        --argjson prerelease "${RELEASE_PRERELEASE}" \
        '{tag_name: $tag, target_commitish: $target, name: $tag, body: $body, draft: true, prerelease: $prerelease}')"
    id="$(forgejo POST '/releases' --header 'Content-Type: application/json' --data "${payload}" | jq -r '.id')"
    echo "Created draft release ${id} for ${RELEASE_TAG}"

    local file
    for file in "${directory}"/*; do
        echo "Uploading $(basename "${file}")"
        forgejo POST "/releases/${id}/assets?name=$(basename "${file}")" \
            --form "attachment=@${file}" > /dev/null
    done
}

publish() {
    local id
    id="$(find_draft)"
    if [[ -z "${id}" ]]; then
        echo "No draft release exists for ${RELEASE_TAG}." >&2
        exit 1
    fi
    forgejo PATCH "/releases/${id}" --header 'Content-Type: application/json' \
        --data '{"draft": false}' > /dev/null
    echo "Published release ${id}: ${server}/${repository}/releases/tag/${RELEASE_TAG}"
}

case "${1:-}" in
    stage) stage "${2:?stage needs the directory of files to attach}" ;;
    publish) publish ;;
    *)
        echo "Usage: release.sh stage <directory> | release.sh publish" >&2
        exit 1
        ;;
esac
