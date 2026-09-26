#!/usr/bin/env bash
# ==============================================================================
# OpenFlux Zen Server - Fast 1-Command Bootstrap Installer (Linux)
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
TAG_URL="https://github.com/BizhQwe/openflux-zen-server/releases/download/v1.0.8/openflux-installer-$RID"
FALLBACK_URL="https://github.com/BizhQwe/openflux-zen-server/releases/download/v1.0.7/openflux-installer-$RID"

echo ""
echo "  OpenFlux Zen Server - Linux ($RID)"
echo "  Загрузка установщика..."

DOWNLOADED=0
if curl -# -fSL "$RELEASE_URL" -o "$INSTALLER_BIN" 2>/dev/null && [ -s "$INSTALLER_BIN" ]; then
    DOWNLOADED=1
elif curl -# -fSL "$TAG_URL" -o "$INSTALLER_BIN" 2>/dev/null && [ -s "$INSTALLER_BIN" ]; then
    DOWNLOADED=1
elif curl -# -fSL "$FALLBACK_URL" -o "$INSTALLER_BIN" 2>/dev/null && [ -s "$INSTALLER_BIN" ]; then
    DOWNLOADED=1
fi

if [ "$DOWNLOADED" -eq 1 ]; then
    chmod +x "$INSTALLER_BIN"
    if [ -t 0 ]; then
        exec "$INSTALLER_BIN" "$@"
    elif [ -c /dev/tty ]; then
        exec "$INSTALLER_BIN" "$@" < /dev/tty
    else
        exec "$INSTALLER_BIN" "$@"
    fi
else
    echo "  [ОШИБКА] Не удалось загрузить исполняемый файл установщика для $RID."
    echo "  Проверьте доступность репозитория или скачайте файл вручную:"
    echo "  $RELEASE_URL"
    exit 1
fi
