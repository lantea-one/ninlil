#!/usr/bin/env bash
# Creates or updates the issue labels of a Forgejo repository from .forgejo/labels.json, so the
# label set is reviewed in git and can be recreated if the repository moves.
#
# Usage: sync-labels.sh <owner>/<name>
#
# Environment:
#   NINLIL_CI_TOKEN  a Forgejo access token that may write labels in the repository
#   FORGEJO_API      the API base URL, e.g. https://git.example/api/v1
#   LABELS_FILE      optional; the labels file (default: ../labels.json next to this script)
#
# The file is a JSON array of objects with name, color (hex without "#"), description and
# exclusive. The file is validated before any request: an exclusive label needs a "/" in its name
# (Forgejo ignores exclusivity without a scope, the text before the last "/") and names must be
# unique; otherwise the script exits non-zero and sends nothing. Existing labels are compared by
# name; the colour is compared case-insensitively and without a leading "#", because Forgejo
# versions differ in how they return it.
#
# The script only creates and updates. It never deletes a label and never touches an issue.
#
# Rule for anyone who labels issues: never send two labels of one exclusive scope in one call.
# Forgejo keeps both on create, so the issue ends up with two "kind/" labels. Add the labels in
# separate calls, or replace the scoped label.
#
# The token is passed to curl in a config file (forgejo-api.sh), never on a command line.
#
# Output: one line per label, "created <name>", "updated <name>" or "unchanged <name>". Exits
# non-zero when the file is invalid or a request fails.

set -euo pipefail
shopt -s inherit_errexit

repository="${1:?usage: sync-labels.sh <owner>/<name>}"
: "${NINLIL_CI_TOKEN:?NINLIL_CI_TOKEN is required}"
: "${FORGEJO_API:?FORGEJO_API is required}"
labels_file="${LABELS_FILE:-$(dirname "${BASH_SOURCE[0]}")/../labels.json}"

# shellcheck source=./forgejo-api.sh
source "$(dirname "${BASH_SOURCE[0]}")/forgejo-api.sh"

# Fails unless every label has the expected fields, exclusive labels are scoped and names are unique.
validate_labels() {
    jq -e 'type == "array" and all(.[];
        (.name | type == "string" and length > 0)
        and (.color | type == "string" and test("^[0-9a-fA-F]{6}$"))
        and (.description | type == "string")
        and (.exclusive | type == "boolean"))' "${labels_file}" > /dev/null \
        || { echo "${labels_file} is not an array of {name, color, description, exclusive}." >&2; return 1; }
    local unscoped duplicates
    unscoped="$(jq -r '.[] | select(.exclusive and (.name | contains("/") | not)) | .name' "${labels_file}")"
    if [[ -n "${unscoped}" ]]; then
        echo "Exclusive labels need a scope (a \"/\" in the name): ${unscoped//$'\n'/, }" >&2
        return 1
    fi
    duplicates="$(jq -r 'group_by(.name)[] | select(length > 1) | .[0].name' "${labels_file}")"
    if [[ -n "${duplicates}" ]]; then
        echo "Duplicate label names: ${duplicates//$'\n'/, }" >&2
        return 1
    fi
}

validate_labels

existing_file="${forgejo_work}/labels.jsonl"
forgejo_paginate "/repos/${repository}/labels" "${existing_file}"

while IFS= read -r label; do
    name="$(jq -r '.name' <<< "${label}")"
    # Missing fields in the tracker's answer (an older Forgejo has no exclusive) read as unequal.
    current="$(jq -c --arg name "${name}" 'select(.name == $name)' "${existing_file}" | head -n 1)"
    if [[ -z "${current}" ]]; then
        forgejo_api POST "/repos/${repository}/labels" \
            --header 'Content-Type: application/json' --data "${label}" > /dev/null
        echo "created ${name}"
        continue
    fi
    # The id is a number, so it is safe in the path.
    id="$(jq -r '.id' <<< "${current}")"
    differs="$(jq -n --argjson want "${label}" --argjson have "${current}" '
        def hex: ltrimstr("#") | ascii_downcase;
        (($want.color | hex) != (($have.color // "") | hex))
        or ($want.description != ($have.description // ""))
        or ($want.exclusive != ($have.exclusive // false))')"
    if [[ "${differs}" == 'true' ]]; then
        forgejo_api PATCH "/repos/${repository}/labels/${id}" \
            --header 'Content-Type: application/json' --data "${label}" > /dev/null
        echo "updated ${name}"
    else
        echo "unchanged ${name}"
    fi
done < <(jq -c '.[]' "${labels_file}")
