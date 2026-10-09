#!/usr/bin/env bash
# Computes every form of the build version the pipeline stamps onto its outputs.
#
# Ninlil's versions are date-based and computed for every build:
#
#   branch builds (pre-release)  v<yyyy>.<mm>.<dd>.<increment>R<branch>+<short-sha>
#   release builds               v<yyyy>.<mm>.<dd>.<increment>R<short-sha>
#
# A release build runs on a branch named v<yyyy>.<mm>.<dd>.<increment>, which supplies the date
# and increment; the pipeline has already checked that the branch points at a commit on main. A
# branch build takes today's UTC date and the next free increment for that branch on that date,
# one above the highest pre-release tag already pushed for it (a pre-release lives for 24 hours,
# so every tag from the current UTC day is still present).
#
# Not every consumer accepts that string as-is, so it is also emitted in each of their forms:
#
#   version           the canonical string above: release name, git tag, InformationalVersion
#   assembly-version  <yyyy>.<m>.<d>.<increment>  AssemblyVersion/FileVersion allow only four
#                     numeric parts, each at most 65535
#   package-version   <yyyy>.<m>.<d>.<increment>[-<branch>]+<short-sha>  NuGet (SemVer 2) has
#                     no leading "v", needs "-" to mark a pre-release, and drops leading zeros
#   image-version     <yyyy>.<mm>.<dd>.<increment>[-<branch>-<short-sha>]  Docker tags allow
#                     neither "+" nor "/"
#   arch-version      the Arch Linux pkgver, which allows neither "-" nor "+": a release is its
#                     plain date version (2026.09.24.1), a branch build appends its branch (with
#                     "-" turned into "_") and commit as further segments (2026.09.24.3.develop.a1b2c3d)
#   deb-version       the Debian package version: a release is its plain date version
#                     (2026.09.24.1); a branch build appends "~<branch>.<short-sha>"
#                     (2026.09.24.3~develop.a1b2c3d). "~" sorts before the end of the string in
#                     Debian ordering, so a branch build sorts below the release it precedes
#   rpm-version       the RPM version, always the plain date version (2026.09.24.1)
#   rpm-release       the RPM release: 1 for a release; for a branch build
#                     0.<branch with "-" turned into "_">.<short-sha>, which sorts below 1
#   prerelease        true for branch builds, false for release builds
#
# Branch names are reduced to lower-case letters, digits, and hyphens ("feature/DoT-tuning"
# becomes "feature-dot-tuning") so the same label is valid in every form above.
#
# Environment:
#   BUILD_KIND  "prerelease" or "release"
#   REF_NAME    the branch being built
#   COMMIT_SHA  the full commit hash being built
#
# Writes key=value lines to $GITHUB_OUTPUT when set (Forgejo Actions reads step outputs from it),
# and always prints them. Exits non-zero when the branch name or date is invalid, the release
# version was already published from another commit, or the increment is out of range.

set -euo pipefail

: "${BUILD_KIND:?BUILD_KIND must be prerelease or release}"
: "${REF_NAME:?REF_NAME must name the branch being built}"
: "${COMMIT_SHA:?COMMIT_SHA must be the commit being built}"

short_sha="${COMMIT_SHA:0:7}"

# Emits one output, both to the log and to the step's output file.
emit() {
    echo "$1=$2"
    if [[ -n "${GITHUB_OUTPUT:-}" ]]; then
        echo "$1=$2" >> "${GITHUB_OUTPUT}"
    fi
}

case "${BUILD_KIND}" in
    release)
        if [[ ! "${REF_NAME}" =~ ^v([0-9]{4})\.([0-9]{2})\.([0-9]{2})\.([0-9]+)$ ]]; then
            echo "Release branch '${REF_NAME}' does not match v<yyyy>.<mm>.<dd>.<increment>." >&2
            exit 1
        fi
        year="${BASH_REMATCH[1]}"
        month="${BASH_REMATCH[2]}"
        day="${BASH_REMATCH[3]}"
        increment="$((10#${BASH_REMATCH[4]}))"

        # Reject impossible dates (v2026.02.30.1) rather than publishing them.
        if ! date -u -d "${year}-${month}-${day}" +%F > /dev/null 2>&1 \
            || [[ "$(date -u -d "${year}-${month}-${day}" +%Y.%m.%d)" != "${year}.${month}.${day}" ]]; then
            echo "Release branch '${REF_NAME}' does not name a real calendar date." >&2
            exit 1
        fi

        version="v${year}.${month}.${day}.${increment}R${short_sha}"
        package_suffix=""
        image_suffix=""
        arch_suffix=""
        deb_suffix=""
        rpm_release="1"
        prerelease="false"

        # A release version is published once. Refuse to build it again from another commit,
        # which would overwrite its container tag and collide with its NuGet packages. The glob
        # also matches pre-release tags (v<date>.<n>R<branch>+<sha>), so the exact release
        # shape is filtered here. A failed listing must stop the build: treating it as "no tags"
        # would let a published release be rebuilt.
        if ! listing="$(git ls-remote --tags origin "refs/tags/v${year}.${month}.${day}.${increment}R*")"; then
            echo "Cannot list tags on origin to check whether the release was already published." >&2
            exit 1
        fi
        existing=""
        while read -r _ ref; do
            if [[ "${ref}" =~ ^refs/tags/v${year}\.${month}\.${day}\.${increment}R[0-9a-f]{7,}$ \
                && "${ref}" != "refs/tags/${version}" ]]; then
                existing+="${ref}"$'\n'
            fi
        done <<< "${listing}"
        if [[ -n "${existing}" ]]; then
            echo "Release v${year}.${month}.${day}.${increment} was already published:" >&2
            echo "${existing}" >&2
            exit 1
        fi
        ;;

    prerelease)
        branch="$(echo "${REF_NAME}" | tr '[:upper:]' '[:lower:]' | sed -E 's/[^a-z0-9]+/-/g; s/^-+//; s/-+$//')"
        branch="${branch:0:40}"
        branch="${branch%-}"
        if [[ -z "${branch}" ]]; then
            echo "Branch '${REF_NAME}' reduces to an empty label." >&2
            exit 1
        fi

        year="$(date -u +%Y)"
        month="$(date -u +%m)"
        day="$(date -u +%d)"

        # The highest increment already tagged today for this branch, plus one.
        # The listing must succeed: a failed one would hand out an increment that is already taken.
        if ! listing="$(git ls-remote --tags origin "refs/tags/v${year}.${month}.${day}.*")"; then
            echo "Cannot list tags on origin to find the next increment." >&2
            exit 1
        fi
        highest=0
        while read -r _ ref; do
            [[ -z "${ref}" ]] && continue
            if [[ "${ref}" =~ ^refs/tags/v${year}\.${month}\.${day}\.([0-9]+)R${branch}\+[0-9a-f]+$ ]]; then
                candidate="$((10#${BASH_REMATCH[1]}))"
                if ((candidate > highest)); then
                    highest="${candidate}"
                fi
            fi
        done <<< "${listing}"
        increment="$((highest + 1))"

        version="v${year}.${month}.${day}.${increment}R${branch}+${short_sha}"
        package_suffix="-${branch}"
        image_suffix="-${branch}-${short_sha}"
        arch_suffix=".${branch//-/_}.${short_sha}"
        deb_suffix="~${branch}.${short_sha}"
        rpm_release="0.${branch//-/_}.${short_sha}"
        prerelease="true"
        ;;

    *)
        echo "BUILD_KIND must be prerelease or release, not '${BUILD_KIND}'." >&2
        exit 1
        ;;
esac

if ((increment < 1 || increment > 65535)); then
    echo "Increment ${increment} is outside 1-65535, the range an assembly version allows." >&2
    exit 1
fi

numeric="${year}.$((10#${month})).$((10#${day})).${increment}"

emit version "${version}"
emit assembly-version "${numeric}"
emit package-version "${numeric}${package_suffix}+${short_sha}"
emit image-version "${year}.${month}.${day}.${increment}${image_suffix}"
emit arch-version "${year}.${month}.${day}.${increment}${arch_suffix}"
emit deb-version "${year}.${month}.${day}.${increment}${deb_suffix}"
emit rpm-version "${year}.${month}.${day}.${increment}"
emit rpm-release "${rpm_release}"
emit prerelease "${prerelease}"
emit short-sha "${short_sha}"
