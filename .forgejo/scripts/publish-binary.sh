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
# request (UseWindowsService in Program.cs), so it also runs as a service. Windows ships as a zip
# holding the executable and msquic.dll side by side: System.Net.Quic loads msquic.dll only from
# the folder of the executable, and the .NET runtime pack marks the file as one a single-file
# publish leaves out of the bundle (DropFromSingleFile), so DNS-over-QUIC needs the second file.
#
# Usage: publish-binary.sh <linux-amd64|linux-arm64|macos-amd64|macos-arm64|windows-amd64>
#        <output-directory>
#
# Output: <output-directory>/ninlil-<name>; for windows-amd64 it is ninlil-windows-amd64.zip,
# which holds ninlil-windows-amd64.exe and msquic.dll from the same publish (needs the zip tool;
# the dotnet toolchain image has it). Exits non-zero for an unknown name, a missing admin
# console build, a Windows publish without msquic.dll, or a failed publish.
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
    if ! command -v zip > /dev/null; then
        echo "The zip tool is required for ${name}." >&2
        exit 1
    fi
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

if [[ "${rid}" == win-* ]]; then
    # Without msquic.dll the zip would start but report QUIC as unsupported, so a publish that
    # does not leave the file behind (a runtime pack change) must fail here, not at the smoke test.
    if [[ ! -f "${publish}/msquic.dll" ]]; then
        echo "The ${rid} publish has no msquic.dll next to the executable." >&2
        exit 1
    fi
    # The zip holds the two files under their final names, with no folder; -X drops the extra
    # file attributes so the archive does not depend on the build host.
    stage="$(mktemp -d)"
    trap 'rm -rf "${publish}" "${stage}"' EXIT
    install -m 0755 "${publish}/${built}" "${stage}/${target}"
    install -m 0644 "${publish}/msquic.dll" "${stage}/msquic.dll"
    archive="${target%.exe}.zip"
    output_directory="$(cd "${output}" && pwd)"
    rm -f "${output_directory}/${archive}"
    (cd "${stage}" && zip -q -X "${output_directory}/${archive}" "${target}" msquic.dll)
    ls -l "${output}/${archive}"
else
    install -m 0755 "${publish}/${built}" "${output}/${target}"
    ls -l "${output}/${target}"
fi
