#!/usr/bin/env bash
# Publishes one self-contained, single-file Ninlil executable with the host's .NET SDK.
#
# The executable carries the .NET runtime, every native library (extracted to a per-user cache
# directory on first start), and the admin console (NinlilEmbedWebRoot) inside one file. macOS
# executables are ad-hoc code signed by the SDK itself, which Apple Silicon requires before it
# will run them at all; that works from a Linux host too.
#
# The admin console's production build must already be in src/dotnet/src/Ninlil.Web/wwwroot/
# (the pipeline exports it there from the Dockerfile's admin-console-dist target).
#
# Usage: publish-binary.sh <linux-amd64|linux-arm64|macos-amd64|macos-arm64> <output-directory>
#
# Environment (all optional; a local build is 0.0.0-local). These deliberately avoid the
# NINLIL_ prefix, which belongs to the application's own configuration settings:
#   BUILD_VERSION           InformationalVersion, e.g. v2026.09.24.1Ra1b2c3d
#   BUILD_PACKAGE_VERSION   Version, e.g. 2026.9.24.1+a1b2c3d
#   BUILD_ASSEMBLY_VERSION  AssemblyVersion/FileVersion, e.g. 2026.9.24.1

set -euo pipefail

name="${1:?usage: publish-binary.sh <binary-name> <output-directory>}"
output="${2:?usage: publish-binary.sh <binary-name> <output-directory>}"

case "${name}" in
    linux-amd64) rid='linux-x64' ;;
    linux-arm64) rid='linux-arm64' ;;
    macos-amd64) rid='osx-x64' ;;
    macos-arm64) rid='osx-arm64' ;;
    *)
        echo "Unknown binary '${name}'." >&2
        exit 1
        ;;
esac

repository_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
if [[ ! -f "${repository_root}/src/dotnet/src/Ninlil.Web/wwwroot/index.html" ]]; then
    echo "The admin console is not in src/dotnet/src/Ninlil.Web/wwwroot/; build it first." >&2
    exit 1
fi

publish="$(mktemp -d)"
trap 'rm -rf "${publish}"' EXIT

dotnet publish "${repository_root}/src/dotnet/src/Ninlil.Web/Ninlil.Web.csproj" \
    --configuration Release \
    --runtime "${rid}" \
    --self-contained \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:NinlilEmbedWebRoot=true \
    -p:NinlilSkipAngularBuild=true \
    -p:DebugType=none \
    -p:ContinuousIntegrationBuild=true \
    -p:Version="${BUILD_PACKAGE_VERSION:-0.0.0-local}" \
    -p:AssemblyVersion="${BUILD_ASSEMBLY_VERSION:-0.0.0.0}" \
    -p:FileVersion="${BUILD_ASSEMBLY_VERSION:-0.0.0.0}" \
    -p:InformationalVersion="${BUILD_VERSION:-0.0.0-local}" \
    --output "${publish}"

mkdir -p "${output}"
install -m 0755 "${publish}/Ninlil.Web" "${output}/ninlil-${name}"
ls -l "${output}/ninlil-${name}"
