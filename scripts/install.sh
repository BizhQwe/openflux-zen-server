#!/usr/bin/env bash
# ==============================================================================
# OpenFlux Zen Server - Fast 1-Command Bootstrap Installer (Linux)
# Supports: Linux x64, Linux ARM64, Linux x86, Linux ARMv7
# ==============================================================================

set -eu
if set -o pipefail 2>/dev/null; then
    set -o pipefail
fi

if [ "$(id -u)" -ne 0 ]; then
    echo "  [ERROR] Root privileges required. Run with: sudo bash $0"
    exit 1
fi

for arg in "$@"; do
    if [ "$arg" = "--uninstall" ] || [ "$arg" = "uninstall" ] || [ "$arg" = "-u" ]; then
        echo ""
        echo "=============================================================="
        echo "  OpenFlux Zen Server — Полное удаление"
        echo "=============================================================="
        echo "  [INFO] Остановка служб и процессов..."
        systemctl disable --now openflux-zen-server.service openflux-zrok.service openflux.service 2>/dev/null || true
        pkill -9 -x OpenFlux.Zen.Server.Web 2>/dev/null || true
        pkill -9 -x OpenFlux.Zen.Server 2>/dev/null || true
        pkill -9 -x OpenFluxZenServer 2>/dev/null || true
        pkill -9 -x openflux-linux-amd64 2>/dev/null || true
        pkill -9 -x openflux-linux-arm64 2>/dev/null || true
        pkill -9 -x openflux-zen-server-web 2>/dev/null || true
        pkill -9 -x openflux-zen-server-core 2>/dev/null || true
        pkill -9 -x openflux-zen-server-cli 2>/dev/null || true
        pkill -9 -x openflux 2>/dev/null || true
        pkill -9 -x zrok 2>/dev/null || true

        echo "  [INFO] Удаление служб systemd..."
        rm -f /etc/systemd/system/openflux*.service /etc/systemd/system/multi-user.target.wants/openflux*.service /lib/systemd/system/openflux*.service
        systemctl daemon-reload 2>/dev/null || true
        systemctl reset-failed 2>/dev/null || true

        echo "  [INFO] Удаление конфигураций и файлов..."
        rm -f /etc/nginx/conf.d/openflux*.conf /etc/nginx/sites-enabled/openflux* /etc/nginx/sites-available/openflux* 2>/dev/null || true
        systemctl reload nginx 2>/dev/null || true
        rm -f /etc/profile.d/openflux*.sh /etc/sudoers.d/openflux 2>/dev/null || true
        rm -rf /opt/openflux-zen-server /opt/openflux /etc/openflux /var/log/openflux /var/lib/openflux /tmp/openflux* /var/tmp/openflux* ~/.openflux ~/.config/openflux
        rm -f /usr/local/bin/OpenFluxZenServer /usr/local/bin/openfluxzenserver /usr/local/bin/openflux /usr/local/bin/openflux-zen-server /usr/bin/OpenFluxZenServer /usr/bin/openfluxzenserver /usr/bin/openflux /usr/bin/openflux-zen-server /bin/openflux 2>/dev/null || true

        echo ""
        echo "  [OK] OpenFlux Zen Server полностью удален из системы без каких-либо остатков."
        echo "=============================================================="
        echo ""
        exit 0
    fi
done

ARCH=$(uname -m)
case "$ARCH" in
    x86_64|amd64)
        RID="linux-amd64"
        DISPLAY_ARCH="linux-x64"
        ;;
    aarch64|arm64)
        RID="linux-arm64"
        DISPLAY_ARCH="linux-arm64"
        ;;
    i386|i486|i586|i686|x86)
        RID="linux-386"
        DISPLAY_ARCH="linux-x86"
        ;;
    armv7l|armv8l|armv7)
        RID="linux-armv7"
        DISPLAY_ARCH="linux-armv7"
        ;;
    *)
        echo "  [ERROR] Unsupported CPU architecture: $ARCH"
        exit 1
        ;;
esac

TMP_DIR="/var/tmp"
if [ ! -w "$TMP_DIR" ]; then
    TMP_DIR="/tmp"
fi
INSTALLER_BIN="$TMP_DIR/openflux-zen-server-installer-$RID"
REPO="${OPENFLUX_REPOSITORY:-BizhQwe/openflux-zen-server}"
RELEASE_URL="https://github.com/$REPO/releases/latest/download/openflux-zen-server-installer-$RID"
TAG_URL="https://github.com/$REPO/releases/download/v1.0.0/openflux-zen-server-installer-$RID"
FALLBACK_URL="https://github.com/$REPO/releases/latest/download/openflux-zen-server-installer-$RID"

echo ""
echo "  OpenFlux Zen Server - Linux ($DISPLAY_ARCH)"
echo "  Downloading installer..."

DOWNLOADED=0
if command -v curl >/dev/null 2>&1; then
    if curl -s -fSL "$RELEASE_URL" -o "$INSTALLER_BIN" 2>/dev/null && [ -s "$INSTALLER_BIN" ]; then
        DOWNLOADED=1
    elif curl -s -fSL "$TAG_URL" -o "$INSTALLER_BIN" 2>/dev/null && [ -s "$INSTALLER_BIN" ]; then
        DOWNLOADED=1
    elif curl -s -fSL "$FALLBACK_URL" -o "$INSTALLER_BIN" 2>/dev/null && [ -s "$INSTALLER_BIN" ]; then
        DOWNLOADED=1
    fi
elif command -v wget >/dev/null 2>&1; then
    if wget -q -O "$INSTALLER_BIN" "$RELEASE_URL" 2>/dev/null && [ -s "$INSTALLER_BIN" ]; then
        DOWNLOADED=1
    elif wget -q -O "$INSTALLER_BIN" "$TAG_URL" 2>/dev/null && [ -s "$INSTALLER_BIN" ]; then
        DOWNLOADED=1
    fi
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
    echo "  [ERROR] Failed to download installer binary for $DISPLAY_ARCH."
    echo "  Check repository access or download manually:"
    echo "  $RELEASE_URL"
    exit 1
fi
