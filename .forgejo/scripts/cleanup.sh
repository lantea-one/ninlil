#!/usr/bin/env bash
# Removes pre-release builds once they are older than MAX_AGE_HOURS (24 by default), counted
# from when each release or package version was created. The cleanup workflow runs it every hour.
#
# Removed once older than MAX_AGE_HOURS:
#   releases        every pre-release, together with its git tag, and every draft (a draft is a
#                   release whose pipeline never finished, including an interrupted release run)
#   container image every version tagged <yyyy>.<mm>.<dd>.<increment>-<branch>-<short-sha>, and
#                   every untagged per-architecture manifest (a "sha256:..." version) that no
#                   remaining tag's manifest index still references
#   NuGet packages  every version with a pre-release label (<yyyy>.<m>.<d>.<increment>-<branch>)
#
# Never removed: published releases, their tags, their container tags (the release version and
# latest), and their NuGet packages. Each rule matches only the pre-release shape of its
# artifact, so a release artifact is never mistaken for one.
#
# Environment:
#   NINLIL_CI_TOKEN  a Forgejo access token with repository and package write access
#   NINLIL_CI_USER   that token's user name (for the container registry)
#   MAX_AGE_HOURS    optional, default 24
#   DRY_RUN          optional; "true" lists what would be removed without removing it
#   FORGEJO_SERVER_URL / GITHUB_SERVER_URL, FORGEJO_REPOSITORY / GITHUB_REPOSITORY
#                    set by the Actions runner
#
# The token is passed to curl in a config file (forgejo-api.sh), never on a command line. Every
# listing is read in full before anything is removed, and a failed page stops the run.
#
# Output: one "Removing ..." (or "Would remove ...") line per item. Exits non-zero when a removal
# or a manifest lookup fails, leaving the remaining items for the next run.

set -euo pipefail
shopt -s inherit_errexit

: "${NINLIL_CI_TOKEN:?NINLIL_CI_TOKEN is required}"

# shellcheck source=./forgejo-api.sh
source "$(dirname "${BASH_SOURCE[0]}")/forgejo-api.sh"

server="${FORGEJO_SERVER_URL:-${GITHUB_SERVER_URL:?no server URL in the environment}}"
repository="${FORGEJO_REPOSITORY:-${GITHUB_REPOSITORY:?no repository in the environment}}"
owner="${repository%%/*}"
FORGEJO_API="${server}/api/v1"
max_age_hours="${MAX_AGE_HOURS:-24}"
dry_run="${DRY_RUN:-false}"
cutoff="$(date -u -d "-${max_age_hours} hours" +%s)"

# Pre-release shapes, per artifact (see compute-version.sh).
image_pattern='^[0-9]{4}\.[0-9]{2}\.[0-9]{2}\.[0-9]+-[a-z0-9-]+-[0-9a-f]{7,}$'
package_pattern='^[0-9]{4}\.[0-9]+\.[0-9]+\.[0-9]+-[a-z0-9-]+(\+[0-9a-f]+)?$'

# True when an ISO-8601 timestamp is older than the cutoff.
expired() {
    local seconds
    seconds="$(date -u -d "$1" +%s)"
    ((seconds < cutoff))
}

# Runs a removal, or only reports it in a dry run.
remove() {
    local description="$1"
    shift
    if [[ "${dry_run}" == 'true' ]]; then
        echo "Would remove ${description}"
    else
        echo "Removing ${description}"
        "$@" > /dev/null
    fi
}

# Percent-encodes a path segment (tags contain "+").
encode() {
    jq -rn --arg value "$1" '$value | @uri'
}

cutoff_text="$(date -u -d "@${cutoff}" '+%F %T UTC')"
echo "Removing pre-release builds published before ${cutoff_text}"

# Both listings are read in full, and fail the run if any page fails, before anything is
# removed: a short list would hide a kept tag's manifests and let their digests be deleted.
# The release list carries no draft filter: on this Forgejo version `draft=true` selects drafts
# only, which would hide published pre-releases; a token with write access sees drafts anyway.
releases_file="${forgejo_work}/releases.jsonl"
forgejo_paginate "/repos/${repository}/releases" "${releases_file}"
versions_file="${forgejo_work}/versions.jsonl"
forgejo_paginate "/packages/${owner}" "${versions_file}"

# Releases: removed after the listing, so deleting never shifts the pages being read.
release_list="${forgejo_work}/releases-selected.jsonl"
jq -c 'select(.prerelease or .draft) | {id, tag_name, draft, created_at}' "${releases_file}" \
    > "${release_list}"
mapfile -t releases < "${release_list}"
for release in "${releases[@]}"; do
    id="$(jq -r '.id' <<< "${release}")"
    tag="$(jq -r '.tag_name' <<< "${release}")"
    draft="$(jq -r '.draft' <<< "${release}")"
    created="$(jq -r '.created_at' <<< "${release}")"
    expired "${created}" || continue

    remove "release ${tag}" forgejo_api DELETE "/repos/${repository}/releases/${id}"
    # A draft never created its tag; a published pre-release did.
    if [[ "${draft}" == 'false' ]]; then
        encoded_tag="$(encode "${tag}")"
        remove "tag ${tag}" forgejo_api DELETE "/repos/${repository}/tags/${encoded_tag}"
    fi
done

# Packages: the container image's pre-release tags and NuGet pre-release versions.
expired_tags=()
version_list="${forgejo_work}/versions-selected.jsonl"
jq -c '{type, name, version, created_at}' "${versions_file}" > "${version_list}"
mapfile -t versions < "${version_list}"
for entry in "${versions[@]}"; do
    type="$(jq -r '.type' <<< "${entry}")"
    name="$(jq -r '.name' <<< "${entry}")"
    version="$(jq -r '.version' <<< "${entry}")"
    created="$(jq -r '.created_at' <<< "${entry}")"
    expired "${created}" || continue

    case "${type}" in
        container)
            [[ "${name}" == 'ninlil' && "${version}" =~ ${image_pattern} ]] || continue
            expired_tags+=("${version}")
            ;;
        nuget)
            [[ "${name}" == Ninlil.* && "${version}" =~ ${package_pattern} ]] || continue
            ;;
        *)
            continue
            ;;
    esac

    encoded_name="$(encode "${name}")"
    encoded_version="$(encode "${version}")"
    remove "${type} ${name} ${version}" \
        forgejo_api DELETE "/packages/${owner}/${type}/${encoded_name}/${encoded_version}"
done

# Untagged per-architecture manifests. A multi-architecture push stores each platform's manifest
# as its own "sha256:..." version, referenced from the tag's manifest index; deleting the tag
# leaves them behind. One is removed only once it is older than the cutoff and no remaining tag
# -- a release, latest, or a pre-release still inside its 24 hours -- references it.
: "${NINLIL_CI_USER:?NINLIL_CI_USER is required}"
image_path="${owner,,}/ninlil"

# The registry's token endpoint accepts the user and token as basic authentication; the
# bearer token it returns goes into its own config file as well.
basic_config=''
forgejo_write_config basic_config user "${NINLIL_CI_USER}:${NINLIL_CI_TOKEN}"
token_response="${forgejo_work}/registry-token.json"
curl --config "${basic_config}" --fail --silent --show-error \
    "${server}/v2/token?service=container_registry&scope=repository:${image_path}:pull" \
    > "${token_response}"
registry_token="$(jq -er '.token' "${token_response}")"
bearer_config=''
forgejo_write_config bearer_config header "Authorization: Bearer ${registry_token}"

referenced="${forgejo_work}/referenced"
: > "${referenced}"
digest_file="${forgejo_work}/digests.tsv"
jq -r 'select(.type == "container" and .name == "ninlil" and (.version | startswith("sha256:"))) | [.version, .created_at] | @tsv' \
    "${version_list}" > "${digest_file}"
mapfile -t digests < "${digest_file}"
kept_tags=0
for entry in "${versions[@]}"; do
    type="$(jq -r '.type' <<< "${entry}")"
    name="$(jq -r '.name' <<< "${entry}")"
    version="$(jq -r '.version' <<< "${entry}")"
    [[ "${type}" == 'container' && "${name}" == 'ninlil' && "${version}" != sha256:* ]] || continue
    # A tag removed above no longer protects anything. In a dry run nothing was removed, so
    # expired tags still count and the listing can miss manifests a real run would remove.
    [[ " ${expired_tags[*]:-} " == *" ${version} "* && "${dry_run}" != 'true' ]] && continue
    kept_tags=$((kept_tags + 1))
    index_file="${forgejo_work}/index.json"
    curl --config "${bearer_config}" --fail --silent --show-error \
        --header 'Accept: application/vnd.oci.image.index.v1+json, application/vnd.docker.distribution.manifest.list.v2+json' \
        "${server}/v2/${image_path}/manifests/${version}" > "${index_file}"
    jq -r '.manifests[]?.digest' "${index_file}" >> "${referenced}"
done

# Kept tags exist but none references a manifest: the lookups returned something unexpected,
# and deleting every digest would break the images those tags point at.
if ((${#digests[@]} > 0 && kept_tags > 0)) && [[ ! -s "${referenced}" ]]; then
    echo "Refusing to delete: ${kept_tags} kept tags reference no manifests." >&2
    exit 1
fi

for digest_entry in "${digests[@]}"; do
    digest="$(cut -f 1 <<< "${digest_entry}")"
    created="$(cut -f 2 <<< "${digest_entry}")"
    expired "${created}" || continue
    grep -qxF "${digest}" "${referenced}" && continue
    encoded_digest="$(encode "${digest}")"
    remove "container ninlil ${digest}" \
        forgejo_api DELETE "/packages/${owner}/container/ninlil/${encoded_digest}"
done
