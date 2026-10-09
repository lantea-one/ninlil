#!/usr/bin/env bash
# Publishes one self-contained, single-file Ninlil executable with the .NET SDK. The pipeline runs
# it in the dotnet toolchain container (in-container.sh dotnet publish-binary.sh ...).
#
# The executable carries the .NET runtime, every native library (extracted on first start to
# DOTNET_BUNDLE_EXTRACT_BASE_DIR, or a per-user cache directory), and the admin console
# (NinlilEmbedWebRoot) inside one file. macOS executables are ad-hoc code signed by the SDK,
# which Apple Silicon requires before it runs them; that works from a Linux host too.
#
# The admin console's production build must already be in src/dotnet/src/Ninlil.Web/wwwroot/
# (build-admin-console.sh puts it there).
#
# The Windows executable is unsigned and reads the Windows Service Control Manager's start
# request (UseWindowsService in Program.cs), so it also runs as a service.
#
# Usage: publish-binary.sh <linux-amd64|linux-arm64|macos-amd64|macos-arm64|windows-amd64>
#        <output-directory>
#
# Output: <output-directory>/ninlil-<name>, or ninlil-windows-amd64.exe for windows-amd64. Exits non-zero for an unknown name, a missing admin
# console build, or a failed publish.
#
# Environment (all optional; a local build is 0.0.0-local). These avoid the NINLIL_ prefix,
# which the application reads as its configuration settings:
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
    windows-amd64) rid='win-x64' ;;
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

# The SDK names the apphost Ninlil.Web.exe for Windows runtime identifiers.
built='Ninlil.Web'
target="ninlil-${name}"
if [[ "${rid}" == win-* ]]; then
    built='Ninlil.Web.exe'
    target="${target}.exe"
fi

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
install -m 0755 "${publish}/${built}" "${output}/${target}"
ls -l "${output}/${target}"
