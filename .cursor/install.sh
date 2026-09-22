#!/usr/bin/env bash
set -euo pipefail

# Idempotent Cloud Agent bootstrap for the Full Crew Jellyfin plugin.
# Installs the .NET 9 SDK when missing (baked into the environment build/snapshot
# on later boots) and refreshes NuGet dependencies for the solution.

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

DOTNET_INSTALL_DIR="/usr/share/dotnet"

have_dotnet9() {
  command -v dotnet >/dev/null 2>&1 && dotnet --list-sdks 2>/dev/null | grep -q '^9\.'
}

if ! have_dotnet9; then
  echo "==> .NET 9 SDK not found; installing to ${DOTNET_INSTALL_DIR}"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  sudo /tmp/dotnet-install.sh --channel 9.0 --install-dir "${DOTNET_INSTALL_DIR}"
  sudo ln -sf "${DOTNET_INSTALL_DIR}/dotnet" /usr/local/bin/dotnet
else
  echo "==> .NET 9 SDK already present; skipping install"
fi

dotnet --info
dotnet restore Jellyfin.Plugin.FullCrew.sln
