#!/usr/bin/env bash
# Packs NuGet packages for a family of Ninlil's library projects with the host's .NET SDK.
#
# Every project under src/dotnet/src except the Ninlil.Web host is a reusable library. A project
# reference between two of them becomes a NuGet dependency on the same version, so a set of
# packages from one build always depends on exactly that build's other packages. Debug symbols
# are embedded in each assembly so a consumer can step into the code with no symbol server.
#
# Usage: pack-packages.sh "<pattern>..." <output-directory>
#   The patterns are shell patterns matched against project directory names, for example
#   "Ninlil.Dns.Protocol.* Ninlil.Dns.Server.*"; "Ninlil.*" packs every library.
#
# Environment (all optional; a local build is 0.0.0-local). These deliberately avoid the
# NINLIL_ prefix, which belongs to the application's own configuration settings:
#   BUILD_VERSION           InformationalVersion, e.g. v2026.09.24.1Ra1b2c3d
#   BUILD_PACKAGE_VERSION   Version, e.g. 2026.9.24.1+a1b2c3d
#   BUILD_ASSEMBLY_VERSION  AssemblyVersion/FileVersion, e.g. 2026.9.24.1

set -euo pipefail

patterns="${1:?usage: pack-packages.sh \"<pattern>...\" <output-directory>}"
output="${2:?usage: pack-packages.sh \"<pattern>...\" <output-directory>}"

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

# A throwaway solution holding exactly the selected projects.
dotnet new sln --name packages --format slnx --output "${work}" > /dev/null
read -ra selected_patterns <<< "${patterns}"
count=0
for project in "${repository_root}"/src/dotnet/src/*/*.csproj; do
    directory="$(basename "$(dirname "${project}")")"
    [[ "${directory}" == 'Ninlil.Web' ]] && continue
    for pattern in "${selected_patterns[@]}"; do
        # shellcheck disable=SC2053 # Deliberately a pattern match.
        if [[ "${directory}" == ${pattern} ]]; then
            dotnet sln "${work}/packages.slnx" add "${project}" > /dev/null
            count=$((count + 1))
            break
        fi
    done
done
if ((count == 0)); then
    echo "No library project matches '${patterns}'." >&2
    exit 1
fi
echo "Packing ${count} projects matching '${patterns}'"

dotnet pack "${work}/packages.slnx" \
    --configuration Release \
    -p:DebugType=embedded \
    -p:ContinuousIntegrationBuild=true \
    -p:NinlilSkipAngularBuild=true \
    -p:Version="${BUILD_PACKAGE_VERSION:-0.0.0-local}" \
    -p:AssemblyVersion="${BUILD_ASSEMBLY_VERSION:-0.0.0.0}" \
    -p:FileVersion="${BUILD_ASSEMBLY_VERSION:-0.0.0.0}" \
    -p:InformationalVersion="${BUILD_VERSION:-0.0.0-local}" \
    --output "${output}"
ls -l "${output}"
