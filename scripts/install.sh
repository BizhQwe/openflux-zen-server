#!/usr/bin/env bash
# ==============================================================================
# OpenFlux Zen Server - Fast 1-Command Bootstrap Installer (Linux)
# Supports: Linux x64, Linux ARM64, Linux x86, Linux ARMv7
# ==============================================================================

set -eu
if (set -o pipefail) 2>/dev/null; then
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

umask 077
TMP_BASE="/var/tmp"
[ -w "$TMP_BASE" ] || TMP_BASE="/tmp"
TMP_DIR=$(mktemp -d "$TMP_BASE/openflux-install.XXXXXXXXXX") || exit 1
ASSET="openflux-zen-server-installer-$RID"
INSTALLER_BIN="$TMP_DIR/$ASSET"
CHECKSUM_FILE="$TMP_DIR/installer.sha256"
trap 'rm -f -- "$INSTALLER_BIN" "$CHECKSUM_FILE"; rmdir -- "$TMP_DIR"' EXIT
trap 'exit 1' HUP INT TERM
REPO="${OPENFLUX_REPOSITORY:-BizhQwe/openflux-zen-server}"
RELEASE_BASE="https://github.com/$REPO/releases/latest/download"
TAG_BASE="https://github.com/$REPO/releases/download/v1.1.2"

command -v curl >/dev/null 2>&1 || { echo '  [ERROR] curl is required.' >&2; exit 1; }
command -v sha256sum >/dev/null 2>&1 || { echo '  [ERROR] sha256sum is required.' >&2; exit 1; }

download() {
    curl --proto '=https' --proto-redir '=https' --tlsv1.2 \
        --connect-timeout 15 --max-time 300 --max-filesize "$3" \
        --retry 2 -fsSL "$1" -o "$2"
}

echo ""
echo "  OpenFlux Zen Server - Linux ($DISPLAY_ARCH)"
echo "  Downloading and verifying installer..."

DOWNLOADED=0
for base in "$RELEASE_BASE" "$TAG_BASE"; do
    if ! download "$base/$ASSET.sha256" "$CHECKSUM_FILE" 4096; then
        continue
    fi
    expected_hash=''
    expected_file=''
    extra=''
    read -r expected_hash expected_file extra < "$CHECKSUM_FILE" || [ -n "$expected_hash" ]
    expected_file=$(printf '%s' "$expected_file" | tr -d '\r')
    case "$expected_hash" in *[!0-9a-fA-F]*|'') echo '  [ERROR] Invalid SHA-256.' >&2; exit 1 ;; esac
    [ "${#expected_hash}" -eq 64 ] && [ -z "$extra" ] || { echo '  [ERROR] Invalid SHA-256.' >&2; exit 1; }
    case "$expected_file" in "$ASSET"|"*$ASSET") ;; *) echo '  [ERROR] Invalid checksum filename.' >&2; exit 1 ;; esac
    if ! download "$base/$ASSET" "$INSTALLER_BIN" 268435456; then
        continue
    fi
    [ -s "$INSTALLER_BIN" ] || { echo '  [ERROR] Empty installer.' >&2; exit 1; }
    if ! (cd "$TMP_DIR" && printf '%s  %s\n' "$expected_hash" "$ASSET" | sha256sum --check --status); then
        echo '  [ERROR] Installer SHA-256 mismatch. Nothing was executed.' >&2
        exit 1
    fi
    DOWNLOADED=1
    break
done

if [ "$DOWNLOADED" -ne 1 ]; then
    echo "  [ERROR] Failed to download a verified installer for $DISPLAY_ARCH." >&2
    exit 1
fi
chmod 700 "$INSTALLER_BIN"
if [ -t 0 ]; then
    "$INSTALLER_BIN" "$@"
elif [ -c /dev/tty ] && ( : < /dev/tty ) 2>/dev/null; then
    "$INSTALLER_BIN" "$@" < /dev/tty
else
    "$INSTALLER_BIN" "$@"
fi
