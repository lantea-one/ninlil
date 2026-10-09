#!/usr/bin/env bash
# Moves every pinned base image and the checkout action to the newest upstream version, in place.
#
# What is pinned, and how:
#   - Container images are written as `name:tag@sha256:<index digest>` in the Dockerfile,
#     docker/ci.Dockerfile, docker-compose.yml and QueryLogPostgreSqlTestHost.cs. The tag is for
#     readers; the digest decides what is pulled. This script re-resolves the tag and rewrites
#     the digest. It uses the multi-architecture index digest (never a single-architecture
#     manifest), because the pipeline builds for amd64 and arm64 from the same line, and it fails
#     when a tag does not resolve to an index.
#   - Workflow steps use `actions/checkout@<40-hex commit> # vX.Y.Z`. This script keeps the major
#     version in the comment and moves to the newest vX.Y.Z tag of that major, pinned by the
#     commit the tag points at (an annotated tag is peeled to its commit).
#
# The script only edits files in the working tree. .forgejo/workflows/bump-pins.yml runs it weekly
# and open-bump-pr.sh turns the change into a pull request, which runs the normal verify build.
#
# Usage: bump-pins.sh [--dry-run]
#   --dry-run  Lists every pin as "old -> new" (or "unchanged") and changes nothing.
#
# Environment:
#   ACTIONS_MIRROR_URL  where the checkout action's repository is looked up, as
#                       <ACTIONS_MIRROR_URL>/actions/checkout (default https://github.com; set it
#                       to the instance's actions mirror when Forgejo resolves actions elsewhere)
#   BUMP_PINS_ROOT      the tree to edit (default: the repository this script is in)
#   BUMP_PINS_DOCKER    the docker command (default docker; the self-test substitutes a fake)
#
# Output: one line per pin on stdout and a summary line. Needs docker with buildx, git, jq, sed.
# Exits non-zero, without editing any file, when any lookup fails.

set -euo pipefail
shopt -s inherit_errexit

dry_run=false
case "${1:-}" in
    '') ;;
    --dry-run) dry_run=true ;;
    *)
        echo "usage: bump-pins.sh [--dry-run]" >&2
        exit 2
        ;;
esac

root="${BUMP_PINS_ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
docker_command="${BUMP_PINS_DOCKER:-docker}"
mirror="${ACTIONS_MIRROR_URL:-https://github.com}"
cd "${root}"

image_files=(
    Dockerfile
    docker/ci.Dockerfile
    docker-compose.yml
    src/dotnet/tests/Ninlil.Dns.Server.QueryLog.Tests/QueryLogPostgreSqlTestHost.cs
)
workflow_files=(.forgejo/workflows/*.yml)

# Each edit is "<file>\t<old text>\t<new text>"; all lookups finish before any file is touched.
edits=()
changed=0

# Prints the multi-architecture index digest of an image reference (name:tag), or fails.
resolve_image_digest() {
    local manifest media_type digest
    manifest="$("${docker_command}" buildx imagetools inspect "$1" --format '{{json .Manifest}}')"
    media_type="$(jq -r '.mediaType' <<< "${manifest}")"
    digest="$(jq -r '.digest' <<< "${manifest}")"
    if [[ "${media_type}" != *index* && "${media_type}" != *manifest.list* ]]; then
        echo "$1 resolves to ${media_type}, not a multi-architecture index." >&2
        return 1
    fi
    if [[ ! "${digest}" =~ ^sha256:[0-9a-f]{64}$ ]]; then
        echo "$1 resolved to an unusable digest: ${digest}" >&2
        return 1
    fi
    printf '%s' "${digest}"
}

# Prints the commit of the newest vMAJOR.minor.patch tag of the checkout action, as
# "<tag> <commit>", or fails. Lightweight tags list their commit directly; an annotated tag lists
# the tag object and a second "^{}" line with the commit, which wins.
resolve_checkout() {
    local major="$1" listing line sha ref name
    declare -A commits=()
    listing="$(git ls-remote --tags "${mirror}/actions/checkout" "refs/tags/${major}.*")"
    while read -r sha ref; do
        [[ -n "${sha}" ]] || continue
        name="${ref#refs/tags/}"
        name="${name%^\{\}}"
        [[ "${name}" =~ ^${major}\.[0-9]+\.[0-9]+$ ]] || continue
        if [[ "${ref}" == *'^{}' || -z "${commits[${name}]:-}" ]]; then
            commits["${name}"]="${sha}"
        fi
    done <<< "${listing}"
    if [[ "${#commits[@]}" -eq 0 ]]; then
        echo "No ${major}.x.y tag found in ${mirror}/actions/checkout." >&2
        return 1
    fi
    line="$(printf '%s\n' "${!commits[@]}" | sort -V | tail -n 1)"
    printf '%s %s' "${line}" "${commits[${line}]}"
}

# Images. The pin pattern is name:tag@sha256:<64 hex>; each distinct pin is resolved once.
mapfile -t image_pins < <(grep -ohE '[A-Za-z0-9][A-Za-z0-9._/-]*:[A-Za-z0-9._-]+@sha256:[0-9a-f]{64}' "${image_files[@]}" | sort -u)
for pin in "${image_pins[@]}"; do
    reference="${pin%@*}"
    old_digest="${pin#*@}"
    new_digest="$(resolve_image_digest "${reference}")"
    if [[ "${new_digest}" == "${old_digest}" ]]; then
        echo "image     ${reference}: unchanged (${old_digest})"
        continue
    fi
    echo "image     ${reference}: ${old_digest} -> ${new_digest}"
    changed=$((changed + 1))
    for file in "${image_files[@]}"; do
        if grep -qF -- "${pin}" "${file}"; then
            edits+=("${file}"$'\t'"${pin}"$'\t'"${reference}@${new_digest}")
        fi
    done
done

# The checkout action. The comment after the commit names the tag, whose major version is kept.
mapfile -t checkout_pins < <(grep -ohE "actions/checkout@[0-9a-f]{40}' # v[0-9]+\.[0-9]+\.[0-9]+" "${workflow_files[@]}" | sort -u)
for pin in "${checkout_pins[@]}"; do
    old_sha="${pin#*@}"
    old_sha="${old_sha:0:40}"
    old_tag="${pin##* }"
    major="${old_tag%%.*}"
    # Not a process substitution: its failure would not stop the script.
    resolved="$(resolve_checkout "${major}")"
    read -r new_tag new_sha <<< "${resolved}"
    if [[ "${new_sha}" == "${old_sha}" && "${new_tag}" == "${old_tag}" ]]; then
        echo "checkout  actions/checkout ${major}: unchanged (${old_tag} ${old_sha})"
        continue
    fi
    echo "checkout  actions/checkout ${major}: ${old_tag} ${old_sha} -> ${new_tag} ${new_sha}"
    changed=$((changed + 1))
    new_pin="actions/checkout@${new_sha}' # ${new_tag}"
    for file in "${workflow_files[@]}"; do
        if grep -qF -- "${pin}" "${file}"; then
            edits+=("${file}"$'\t'"${pin}"$'\t'"${new_pin}")
        fi
    done
done

if [[ "${changed}" -eq 0 ]]; then
    echo "Nothing to bump."
    exit 0
fi
if [[ "${dry_run}" == 'true' ]]; then
    echo "${changed} pin(s) would change (dry run, nothing edited)."
    exit 0
fi

# Literal replacement: the old text is escaped for sed (the dot is the only special character a
# pin holds), and "|" cannot appear in any pin, so it is a safe delimiter.
for edit in "${edits[@]}"; do
    IFS=$'\t' read -r file old new <<< "${edit}"
    sed -i "s|${old//./\\.}|${new}|g" "${file}"
done
echo "${changed} pin(s) changed."
