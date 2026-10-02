#!/bin/bash
# SessionStart hook for Claude Code on the web.
#
# Gives a cloud session the same toolchain a contributor has locally, so an
# agent can build, run the fast unit tests and type-check the Razor views
# before it pushes, instead of using CI as its compiler. Local machines are
# left alone: they already have an SDK pinned by global.json.
#
# Where the tools come from matters, because the sandbox's network policy
# blocks Microsoft's download hosts (builds.dotnet.microsoft.com,
# dotnetcli.azureedge.net), so dotnet-install.sh cannot work:
#   - the SDK comes from the Ubuntu archive (dotnet-sdk-10.0); global.json
#     rolls forward to it (latestFeature);
#   - PowerShell, which check-views.ps1 and the other scripts need, is the
#     "PowerShell" .NET global tool from api.nuget.org.
# SQL-backed integration tests need SQL Server in Docker and stay CI-only.
#
# Idempotent: each step checks before it installs.
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

cd "${CLAUDE_PROJECT_DIR:-$(dirname "$0")/../..}"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export PATH="$PATH:$HOME/.dotnet/tools"

if ! dotnet --list-sdks 2>/dev/null | grep -q '^10\.'; then
  echo "Installing the .NET 10 SDK from the Ubuntu archive..."
  sudo=""
  if [ "$(id -u)" -ne 0 ]; then sudo="sudo"; fi
  # Some third-party apt sources are blocked by the network policy; a
  # partial index refresh is fine as long as the Ubuntu archive answers.
  $sudo apt-get update -qq || true
  $sudo env DEBIAN_FRONTEND=noninteractive apt-get install -y -qq dotnet-sdk-10.0
fi

if ! command -v pwsh >/dev/null 2>&1; then
  echo "Installing PowerShell as a .NET global tool..."
  dotnet tool install --global PowerShell
fi

dotnet restore

if [ -n "${CLAUDE_ENV_FILE:-}" ]; then
  {
    echo 'export DOTNET_CLI_TELEMETRY_OPTOUT=1'
    echo 'export DOTNET_NOLOGO=1'
    echo 'export PATH="$PATH:$HOME/.dotnet/tools"'
  } >> "$CLAUDE_ENV_FILE"
fi

echo "Toolchain ready: $(dotnet --version) SDK, PowerShell $(pwsh -NoLogo -NoProfile -Command '$PSVersionTable.PSVersion.ToString()')"
