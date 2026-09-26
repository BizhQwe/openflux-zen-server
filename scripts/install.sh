#!/usr/bin/env bash
# ==============================================================================
# OpenFlux Zen Server — Fast 1-Command Bootstrap Installer (Linux)
# Supports: Linux x64, Linux ARM64
# ==============================================================================

set -euo pipefail

if [ "$(id -u)" -ne 0 ]; then
    echo "  [ОШИБКА] Требуются права суперпользователя. Запустите: sudo bash $0"
    exit 1
fi

ARCH=$(uname -m)
case "$ARCH" in
    x86_64|amd64)
        RID="linux-x64"
        ;;
    aarch64|arm64)
        RID="linux-arm64"
        ;;
    *)
        echo "  [ОШИБКА] Неподдерживаемая архитектура процессора: $ARCH"
        exit 1
        ;;
esac

INSTALLER_BIN="/tmp/openflux-installer-$RID"
RELEASE_URL="https://github.com/BizhQwe/openflux-zen-server/releases/latest/download/openflux-installer-$RID"
FALLBACK_URL="https://raw.githubusercontent.com/BizhQwe/openflux-zen-server/main/dist/openflux-installer-$RID"

echo ""
echo "  OpenFlux Zen Server — Linux ($RID)"
echo "  Загрузка установщика..."

DOWNLOADED=0
if curl -fsSL "$RELEASE_URL" -o "$INSTALLER_BIN" 2>/dev/null && [ -s "$INSTALLER_BIN" ]; then
    DOWNLOADED=1
elif curl -fsSL "$FALLBACK_URL" -o "$INSTALLER_BIN" 2>/dev/null && [ -s "$INSTALLER_BIN" ]; then
    DOWNLOADED=1
fi

if [ "$DOWNLOADED" -eq 1 ]; then
    chmod +x "$INSTALLER_BIN"
    exec "$INSTALLER_BIN" "$@"
else
    echo "  [!] Готовый релиз не найден. Проверка локальной сборки..."
    SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
    REPO_ROOT="$(dirname "$SCRIPT_DIR")"
    if [ -f "$REPO_ROOT/scripts/build-installers.ps1" ] && command -v pwsh >/dev/null 2>&1; then
        pwsh -File "$REPO_ROOT/scripts/build-installers.ps1" -Targets @("$RID")
        if [ -f "$REPO_ROOT/dist/openflux-installer-$RID" ]; then
            chmod +x "$REPO_ROOT/dist/openflux-installer-$RID"
            exec "$REPO_ROOT/dist/openflux-installer-$RID" "$@"
        fi
    fi
    echo "  [ОШИБКА] Не удалось загрузить или собрать установщик для $RID."
    exit 1
fi
