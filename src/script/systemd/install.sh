#!/usr/bin/env bash
#
# This script installs Ninlil as a systemd service: it creates a dedicated, unprivileged
# service account, lays down the directories the service needs, installs the unit file sitting
# alongside this script, and enables (but does not start) the service.
#
# It accepts either a release binary (ninlil-linux-amd64 / ninlil-linux-arm64) or the output
# directory of `dotnet publish src/dotnet/src/Ninlil.Web`:
#
#   sudo ./install.sh ./ninlil-linux-amd64
#   sudo ./install.sh /path/to/published/Ninlil.Web
#
# Re-running it upgrades the installed program and leaves the data directory and the
# environment file untouched.

set -euo pipefail

usage() {
    echo "Usage: sudo ./install.sh <ninlil-linux-binary | publish-directory>" >&2
    exit 1
}

if [[ "${EUID}" -ne 0 ]]; then
    echo "This script must be run as root." >&2
    usage
fi

SOURCE="${1:-}"
[[ -n "${SOURCE}" && -e "${SOURCE}" ]] || usage

SCRIPT_DIRECTORY="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
INSTALL_DIRECTORY="/opt/ninlil-dns"
CONFIG_DIRECTORY="/etc/ninlil-dns"
LOG_DIRECTORY="/var/log/ninlil-dns"
SERVICE_USER="ninlil-dns"

# Create the dedicated, unprivileged service account, if it does not already exist.
if ! id "${SERVICE_USER}" &>/dev/null; then
    useradd --system --no-create-home --shell /usr/sbin/nologin "${SERVICE_USER}"
fi

# Lay down the directories the service needs. The program itself stays root-owned (read-only to
# the service account); only the data and log directories belong to it.
mkdir -p "${INSTALL_DIRECTORY}" "${INSTALL_DIRECTORY}/data" "${CONFIG_DIRECTORY}" "${LOG_DIRECTORY}"
if [[ -f "${SOURCE}" ]]; then
    # A single-file release binary, installed under the name the unit starts.
    install -m 0755 "${SOURCE}" "${INSTALL_DIRECTORY}/Ninlil.Web"
    # The admin console is built into the executable; the unit still names a web root, which
    # ASP.NET Core would try to create (and fail to, read-only) if it did not already exist.
    mkdir -p "${INSTALL_DIRECTORY}/wwwroot"
else
    cp -r "${SOURCE}/." "${INSTALL_DIRECTORY}/"
    chmod 0755 "${INSTALL_DIRECTORY}/Ninlil.Web"
fi
chown -R "${SERVICE_USER}:${SERVICE_USER}" "${INSTALL_DIRECTORY}/data" "${LOG_DIRECTORY}"
chmod 0700 "${INSTALL_DIRECTORY}/data"

# Create a starter environment file, if one does not already exist, without overwriting any
# configuration an operator has already put in place.
ENVIRONMENT_FILE="${CONFIG_DIRECTORY}/ninlil-dns.env"
if [[ ! -f "${ENVIRONMENT_FILE}" ]]; then
    cat > "${ENVIRONMENT_FILE}" <<'ENVEOF'
# Environment variables for the Ninlil service. Every setting Ninlil reads can be set here using
# its NINLIL_-prefixed name; see ENVIRONMENT.md in the repository for the complete list.
# For example:
#   NINLIL_STORAGE_ENGINE=PostgreSql
#   NINLIL_STORAGE_CONNECTION_STRING=Host=localhost;Database=ninlil;Username=ninlil;Password=change-me
#   NINLIL_IDENTITY_BOOTSTRAP_ADMINISTRATOR_PASSWORD=Change-Me-Now1!
NINLIL_ASPNETCORE_ENVIRONMENT=Production
ENVEOF
    chmod 0640 "${ENVIRONMENT_FILE}"
    chgrp "${SERVICE_USER}" "${ENVIRONMENT_FILE}"
fi

# Install and enable the systemd unit.
cp "${SCRIPT_DIRECTORY}/ninlil-dns.service" /etc/systemd/system/ninlil-dns.service
systemctl daemon-reload
systemctl enable ninlil-dns

echo "Ninlil has been installed. Review ${ENVIRONMENT_FILE}, then start it with:"
echo "  sudo systemctl start ninlil-dns"
echo "The bootstrap administrator's password is printed once to: journalctl -u ninlil-dns"
