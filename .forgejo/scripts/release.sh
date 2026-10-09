#!/usr/bin/env bash
# Creates, fills, and publishes the Forgejo release for one pipeline run.
#
# The release is created first, as a draft, and the pipeline's parallel jobs attach their files
# to it as they finish; it doubles as the hand-off between jobs (the Arch Linux packages and the
# container image are built from the Linux binaries other jobs uploaded). A draft is visible only
# to repository writers and creates no git tag, so a run that fails part-way publishes nothing
# (cleanup.sh removes stale drafts after 24 hours).
#
#   release.sh stage                  Creates the empty draft for RELEASE_TAG, replacing a draft
#                                     left over from an earlier attempt at the same tag.
#   release.sh upload <file>...       Attaches each file to the draft, with a <file>.sha256
#                                     checksum next to it.
#   release.sh download <name> <dir>  Downloads the draft's attachment <name> into <dir>.
#   release.sh publish                Replaces the individual .sha256 files with one SHA256SUMS
#                                     and publishes the draft, which creates its git tag.
#
# Environment:
#   NINLIL_CI_TOKEN     a Forgejo access token with repository write access
#   RELEASE_TAG         the release's tag and name (the computed version)
#   RELEASE_PRERELEASE  "true" or "false" (stage only)
#   IMAGE_VERSION       the container image tag (stage only, for the release notes)
#   ARCH_VERSION        the Arch Linux package version (stage only, for the release notes)
#   COMMIT_SHA          the commit being released (stage only)
#   REF_NAME            the branch being built (stage only, for the release notes)
#   FORGEJO_SERVER_URL / GITHUB_SERVER_URL, FORGEJO_REPOSITORY / GITHUB_REPOSITORY
#                       set by the Actions runner
#
# The token reaches curl through a config file (forgejo-api.sh), never on a command line.
#
# Exits non-zero on any failed API call, and when upload, download or publish finds no draft.

set -euo pipefail
shopt -s inherit_errexit

: "${NINLIL_CI_TOKEN:?NINLIL_CI_TOKEN is required}"
: "${RELEASE_TAG:?RELEASE_TAG is required}"

# shellcheck source=./forgejo-api.sh
source "$(dirname "${BASH_SOURCE[0]}")/forgejo-api.sh"

server="${FORGEJO_SERVER_URL:-${GITHUB_SERVER_URL:?no server URL in the environment}}"
repository="${FORGEJO_REPOSITORY:-${GITHUB_REPOSITORY:?no repository in the environment}}"
# forgejo_api calls this base plus a path below the repository.
FORGEJO_API="${server}/api/v1/repos/${repository}"
image="${server#https://}/${repository,,}"

# Prints the id of the draft release for RELEASE_TAG, or nothing when there is none. The whole
# list is read first; a failed page stops the script instead of reading as "no draft".
find_draft() {
    local drafts="${forgejo_work}/drafts.jsonl"
    forgejo_paginate '/releases?draft=true' "${drafts}"
    jq -rs --arg tag "${RELEASE_TAG}" \
        '[.[] | select(.draft and .tag_name == $tag) | .id][0] // empty' "${drafts}"
}

# Prints the id of the draft release for RELEASE_TAG, failing when there is none.
require_draft() {
    local id
    id="$(find_draft)"
    if [[ -z "${id}" ]]; then
        echo "No draft release exists for ${RELEASE_TAG}." >&2
        exit 1
    fi
    echo "${id}"
}

stage() {
    : "${RELEASE_PRERELEASE:?RELEASE_PRERELEASE is required}"
    : "${COMMIT_SHA:?COMMIT_SHA is required}"
    : "${ARCH_VERSION:?ARCH_VERSION is required}"

    local existing
    existing="$(find_draft)"
    if [[ -n "${existing}" ]]; then
        echo "Replacing earlier draft ${existing} for ${RELEASE_TAG}"
        forgejo_api DELETE "/releases/${existing}" > /dev/null
    fi

    local kind='Release'
    if [[ "${RELEASE_PRERELEASE}" == 'true' ]]; then
        kind="Pre-release build of \`${REF_NAME}\`; it is removed automatically after 24 hours"
    fi

    local downloads="${server}/${repository}/releases/download/${RELEASE_TAG//+/%2B}"
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

\`ninlil\` packages for this build, installable directly:

\`\`\`sh
sudo pacman -U ${downloads}/ninlil-${ARCH_VERSION}-1-x86_64.pkg.tar.zst
sudo systemctl enable --now ninlil
\`\`\`

(\`ninlil-${ARCH_VERSION}-1-aarch64.pkg.tar.zst\` for ARM64.)

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
    id="$(forgejo_api POST '/releases' --header 'Content-Type: application/json' --data "${payload}" | jq -r '.id')"
    echo "Created draft release ${id} for ${RELEASE_TAG}"
}

# Attaches one file to the release, replacing an attachment of the same name (a re-run job).
attach() {
    local id="$1" file="$2" name existing
    name="$(basename "${file}")"
    existing="$(forgejo_api GET "/releases/${id}/assets" \
        | jq -r --arg name "${name}" '.[] | select(.name == $name) | .id')"
    if [[ -n "${existing}" ]]; then
        forgejo_api DELETE "/releases/${id}/assets/${existing}" > /dev/null
    fi
    echo "Uploading ${name}"
    forgejo_api POST "/releases/${id}/assets?name=${name}" --form "attachment=@${file}" > /dev/null
}

upload() {
    local id file sums
    id="$(require_draft)"
    sums="$(mktemp -d)"
    for file in "$@"; do
        attach "${id}" "${file}"
        (cd "$(dirname "${file}")" && sha256sum "$(basename "${file}")") \
            > "${sums}/$(basename "${file}").sha256"
        attach "${id}" "${sums}/$(basename "${file}").sha256"
    done
    rm -rf "${sums}"
}

download() {
    local name="$1" directory="$2" id url
    id="$(require_draft)"
    url="$(forgejo_api GET "/releases/${id}/assets" \
        | jq -r --arg name "${name}" '.[] | select(.name == $name) | .browser_download_url')"
    if [[ -z "${url}" ]]; then
        echo "The draft for ${RELEASE_TAG} has no attachment named ${name}." >&2
        exit 1
    fi
    mkdir -p "${directory}"
    echo "Downloading ${name}"
    forgejo_download "${url}" "${directory}/${name}"
}

publish() {
    local id assets work asset url asset_id
    id="$(require_draft)"
    assets="$(forgejo_api GET "/releases/${id}/assets")"
    work="${forgejo_work}/publish"
    mkdir -p "${work}"

    # Merge the per-file checksums the jobs attached into one SHA256SUMS, deleting each one
    # as it is read.
    : > "${work}/SHA256SUMS"
    jq -c '.[] | select(.name | endswith(".sha256"))' <<< "${assets}" \
        > "${work}/checksum-assets.jsonl"
    while read -r asset; do
        [[ -z "${asset}" ]] && continue
        url="$(jq -r '.browser_download_url' <<< "${asset}")"
        forgejo_download "${url}" "${work}/one.sha256"
        cat "${work}/one.sha256" >> "${work}/SHA256SUMS"
        asset_id="$(jq -r '.id' <<< "${asset}")"
        forgejo_api DELETE "/releases/${id}/assets/${asset_id}" > /dev/null
    done < "${work}/checksum-assets.jsonl"
    sort -k 2 -o "${work}/SHA256SUMS" "${work}/SHA256SUMS"
    cat "${work}/SHA256SUMS"
    attach "${id}" "${work}/SHA256SUMS"

    forgejo_api PATCH "/releases/${id}" --header 'Content-Type: application/json' \
        --data '{"draft": false}' > /dev/null
    echo "Published release ${id}: ${server}/${repository}/releases/tag/${RELEASE_TAG}"
}

case "${1:-}" in
    stage) stage ;;
    upload)
        shift
        upload "$@"
        ;;
    download) download "${2:?download needs an attachment name}" "${3:?download needs a directory}" ;;
    publish) publish ;;
    *)
        echo "Usage: release.sh stage | upload <file>... | download <name> <dir> | publish" >&2
        exit 1
        ;;
esac
