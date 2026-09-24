#!/usr/bin/env bash
set -e

# ==============================================================================
# OpenFlux Zen Server - Production Installer for Linux
# ==============================================================================

RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
BOLD='\033[1m'
NC='\033[0m' # No Color

echo -e "${CYAN}${BOLD}"
echo "=================================================================="
echo "          OpenFlux Zen Server - Installation Script               "
echo "=================================================================="
echo -e "${NC}"

# 1. Root check
if [ "$(id -u)" -ne 0 ]; then
    echo -e "${RED}[ERROR] This script must be run as root. Try: sudo bash install.sh${NC}"
    exit 1
fi

PREFIX="${OPENFLUX_PREFIX:-/opt/openflux-zen-server}"
REPO_URL="https://github.com/BizhQwe/openflux-zen-server.git"

# 2. Package manager dependencies
echo -e "${BLUE}[1/8] Checking prerequisite system packages...${NC}"
if command -v apt-get >/dev/null 2>&1; then
    apt-get update -y >/dev/null 2>&1 || true
    apt-get install -y curl wget git tar jq openssl ca-certificates >/dev/null 2>&1 || true
elif command -v dnf >/dev/null 2>&1; then
    dnf install -y curl wget git tar jq openssl ca-certificates >/dev/null 2>&1 || true
elif command -v yum >/dev/null 2>&1; then
    yum install -y curl wget git tar jq openssl ca-certificates >/dev/null 2>&1 || true
fi

# 3. Detect architecture
ARCH="$(uname -m)"
case "$ARCH" in
    x86_64) DOTNET_ARCH="x64"; ZROK_ARCH="amd64" ;;
    aarch64|arm64) DOTNET_ARCH="arm64"; ZROK_ARCH="arm64" ;;
    *) echo -e "${RED}[ERROR] Unsupported CPU architecture: $ARCH${NC}"; exit 1 ;;
esac

# 4. Check or install .NET 10 SDK / Runtime
echo -e "${BLUE}[2/8] Checking .NET 10 environment...${NC}"
NEED_DOTNET=1
if command -v dotnet >/dev/null 2>&1; then
    if dotnet --list-sdks 2>/dev/null | grep -q "^10\."; then
        NEED_DOTNET=0
        echo -e "${GREEN}✓ .NET 10 SDK is already installed${NC}"
    fi
fi

if [ "$NEED_DOTNET" -eq 1 ]; then
    echo -e "${YELLOW}Installing .NET 10 via official Microsoft dotnet-install script...${NC}"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet >/dev/null
    ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
    echo -e "${GREEN}✓ .NET 10 installed successfully${NC}"
fi

# 5. Download / Clone Repository
echo -e "${BLUE}[3/8] Fetching OpenFlux Zen Server repository...${NC}"
if [ -d "$PREFIX/.git" ]; then
    echo "Updating existing repository in $PREFIX..."
    git -C "$PREFIX" fetch --all --prune
    git -C "$PREFIX" reset --hard origin/main
else
    if [ -d "$PREFIX" ]; then
        echo "Removing non-git directory at $PREFIX..."
        rm -rf "$PREFIX"
    fi
    mkdir -p "$PREFIX"
    git clone --depth 1 "$REPO_URL" "$PREFIX"
fi

# 6. Publish Application
echo -e "${BLUE}[4/8] Building and publishing OpenFlux Zen Server (Web & CLI)...${NC}"

# Stop any running instances/tunnels to prevent "Text file busy" (ETXTBSY)
systemctl stop openflux-zen-server.service 2>/dev/null || true
pkill -9 -f openflux-linux 2>/dev/null || true

cd "$PREFIX"
dotnet publish src/OpenFlux.Zen.Server.Web/OpenFlux.Zen.Server.Web.csproj -c Release -o "$PREFIX/app" >/dev/null
dotnet publish src/OpenFlux.Zen.Server.Cli/OpenFlux.Zen.Server.Cli.csproj -c Release -o "$PREFIX/app" >/dev/null
chmod +x "$PREFIX/app/OpenFluxZenServer" || true

# Copy runtimes and set permissions (remove destination first to unlink busy inodes)
mkdir -p "$PREFIX/app/runtimes"
rm -f "$PREFIX/app/runtimes/"* 2>/dev/null || true
cp -f -r "$PREFIX/runtimes/"* "$PREFIX/app/runtimes/"
chmod +x "$PREFIX/app/runtimes/"* || true

# 7. Generate Security Credentials
echo -e "${BLUE}[5/8] Generating secure access keys and credentials...${NC}"

# Check for existing credentials to preserve settings across updates
EXISTING_SECRET=""
EXISTING_USER=""
EXISTING_PASS=""
if [ -f "$PREFIX/app/data/.credentials" ]; then
    EXISTING_SECRET=$(grep -o '"secretPath": "[^"]*"' "$PREFIX/app/data/.credentials" 2>/dev/null | cut -d'"' -f4 || true)
    EXISTING_USER=$(grep -o '"username": "[^"]*"' "$PREFIX/app/data/.credentials" 2>/dev/null | cut -d'"' -f4 || true)
    EXISTING_PASS=$(grep -o '"password": "[^"]*"' "$PREFIX/app/data/.credentials" 2>/dev/null | cut -d'"' -f4 || true)
fi

ADMIN_USER="${OPENFLUX_ADMIN_USER:-${EXISTING_USER:-$(openssl rand -base64 8 | tr -dc 'a-zA-Z0-9' | head -c 10)}}"
ADMIN_PASS="${OPENFLUX_ADMIN_PASSWORD:-${EXISTING_PASS:-$(openssl rand -base64 12 | tr -dc 'a-zA-Z0-9' | head -c 16)}}"
SECRET_PATH="${OPENFLUX_SECRET_PATH:-${EXISTING_SECRET:-$(openssl rand -hex 8)}}"
LISTEN_PORT="${OPENFLUX_PORT:-5000}"

# 8. Interactive or Non-interactive Publishing Configuration
echo -e "${BLUE}[6/8] Configuring network accessibility and publishing...${NC}"
PUBLISH_MODE="local"
PUBLIC_URL=""
DOMAIN=""
ZROK_TOKEN=""

HAS_TTY=0
if [ -r /dev/tty ] && [ -w /dev/tty ]; then
    HAS_TTY=1
fi

# Non-interactive overrides via environment variables
if [ -n "${OPENFLUX_NETWORK_ACCESS:-}" ]; then
    NET_ACCESS="$OPENFLUX_NETWORK_ACCESS"
elif [ "$HAS_TTY" -eq 1 ]; then
    echo -e "${CYAN}По умолчанию панель доступна только локально на сервере (127.0.0.1).${NC}"
    read -r -p "Нужна ли сетевая доступность панели из интернета? [y/N]: " NET_ACCESS < /dev/tty
elif [ -t 0 ]; then
    echo -e "${CYAN}По умолчанию панель доступна только локально на сервере (127.0.0.1).${NC}"
    read -r -p "Нужна ли сетевая доступность панели из интернета? [y/N]: " NET_ACCESS
else
    NET_ACCESS="n"
fi

if [[ "$NET_ACCESS" =~ ^[Yy]$ ]]; then
    if [ -n "${OPENFLUX_PUBLISH_MODE:-}" ]; then
        PUB_CHOICE="$OPENFLUX_PUBLISH_MODE"
    elif [ "$HAS_TTY" -eq 1 ]; then
        echo -e "${CYAN}Выберите режим публикации:${NC}"
        echo "  1) Открытые порты (Собственный домен + безопасный HTTPS с авто-сертификатом)"
        echo "  2) Через Zrok (Защищённый туннель без открытия портов наружу)"
        read -r -p "Ваш выбор [1/2]: " PUB_CHOICE < /dev/tty
    elif [ -t 0 ]; then
        echo -e "${CYAN}Выберите режим публикации:${NC}"
        echo "  1) Открытые порты (Собственный домен + безопасный HTTPS с авто-сертификатом)"
        echo "  2) Через Zrok (Защищённый туннель без открытия портов наружу)"
        read -r -p "Ваш выбор [1/2]: " PUB_CHOICE
    else
        PUB_CHOICE="1"
    fi

    if [ "$PUB_CHOICE" = "1" ]; then
        PUBLISH_MODE="domain"
        if [ -n "${OPENFLUX_DOMAIN:-}" ]; then
            DOMAIN="$OPENFLUX_DOMAIN"
        elif [ "$HAS_TTY" -eq 1 ]; then
            read -r -p "Введите ваш домен (например, zen.example.com): " DOMAIN < /dev/tty
        else
            read -r -p "Введите ваш домен (например, zen.example.com): " DOMAIN
        fi

        echo -e "${YELLOW}Проверка существующих SSL-сертификатов для $DOMAIN...${NC}"
        EXISTING_CERT=""
        if [ -f "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" ]; then
            EXISTING_CERT="/etc/letsencrypt/live/$DOMAIN/fullchain.pem"
        elif [ -f "/etc/ssl/certs/$DOMAIN.crt" ]; then
            EXISTING_CERT="/etc/ssl/certs/$DOMAIN.crt"
        fi

        if [ -n "$EXISTING_CERT" ]; then
            echo -e "${GREEN}✓ Обнаружен действующий SSL-сертификат ($EXISTING_CERT). Переиспользуем без перевыпуска.${NC}"
        else
            echo "SSL-сертификат не найден. Проверка доступности портов 80/443..."
            if ss -tulpn | grep -qE ':(80|443)\s'; then
                echo -e "${YELLOW}Порты 80 или 443 заняты (например, веб-сервером или 3x-ui).${NC}"
                if command -v nginx >/dev/null 2>&1; then
                    echo "Настраиваем Nginx reverse proxy без конфликта..."
                    cat > "/etc/nginx/conf.d/openflux-zen.conf" <<EOF
server {
    listen 80;
    server_name $DOMAIN;
    location / {
        proxy_pass http://127.0.0.1:$LISTEN_PORT;
        proxy_http_version 1.1;
        proxy_set_header Upgrade \$http_upgrade;
        proxy_set_header Connection "upgrade";
        proxy_set_header Host \$host;
        proxy_cache_bypass \$http_upgrade;
    }
}
EOF
                    systemctl reload nginx 2>/dev/null || true
                fi
            fi
        fi
        PUBLIC_URL="https://$DOMAIN/$SECRET_PATH/"

    elif [ "$PUB_CHOICE" = "2" ]; then
        PUBLISH_MODE="zrok"
        if [ -n "${OPENFLUX_ZROK_TOKEN:-}" ]; then
            ZROK_TOKEN="$OPENFLUX_ZROK_TOKEN"
        elif [ "$HAS_TTY" -eq 1 ]; then
            read -r -p "Введите ваш Zrok токен (Account Token): " ZROK_TOKEN < /dev/tty
        else
            read -r -p "Введите ваш Zrok токен (Account Token): " ZROK_TOKEN
        fi

        # Check or download zrok
        if ! command -v zrok >/dev/null 2>&1; then
            echo "Скачивание zrok v2.0.4 ($ZROK_ARCH)..."
            ZROK_URL="https://github.com/openziti/zrok/releases/download/v2.0.4/zrok_2.0.4_linux_${ZROK_ARCH}.tar.gz"
            curl -sSL "$ZROK_URL" -o /tmp/zrok.tar.gz
            mkdir -p /tmp/zrok_ext
            tar -xzf /tmp/zrok.tar.gz -C /tmp/zrok_ext
            if [ -f "/tmp/zrok_ext/zrok2" ]; then
                cp /tmp/zrok_ext/zrok2 /usr/local/bin/zrok2
                ln -sf /usr/local/bin/zrok2 /usr/local/bin/zrok
            elif [ -f "/tmp/zrok_ext/zrok" ]; then
                cp /tmp/zrok_ext/zrok /usr/local/bin/zrok
            fi
            chmod +x /usr/local/bin/zrok /usr/local/bin/zrok2 2>/dev/null || true
            rm -rf /tmp/zrok.tar.gz /tmp/zrok_ext
        fi

        echo "Включение окружения Zrok..."
        export HOME=/root
        zrok enable "$ZROK_TOKEN" >/dev/null 2>&1 || true

        # Setup persistent zrok systemd service
        cat > /etc/systemd/system/openflux-zrok.service <<EOF
[Unit]
Description=OpenFlux Zrok Tunnel Service
After=network.target openflux-zen-server.service
Wants=openflux-zen-server.service

[Service]
Type=simple
User=root
Environment=HOME=/root
ExecStart=/usr/local/bin/zrok share public http://127.0.0.1:$LISTEN_PORT --headless
Restart=always
RestartSec=5

[Install]
WantedBy=multi-user.target
EOF
        systemctl daemon-reload
        systemctl enable --now openflux-zrok.service 2>/dev/null || true

        # Wait a moment for zrok to initialize and parse URL
        sleep 5
        ZROK_ENDPOINT=$(journalctl -u openflux-zrok --no-pager -n 50 2>/dev/null | grep -Eo '[a-z0-9]+\.shares?\.zrok\.io' | tail -n1 || true)
        if [ -z "$ZROK_ENDPOINT" ] && command -v zrok >/dev/null 2>&1; then
            ZROK_ENDPOINT=$(zrok overview 2>/dev/null | grep -Eo '[a-z0-9]+\.shares?\.zrok\.io' | head -n1 || true)
        fi
        if [ -n "$ZROK_ENDPOINT" ]; then
            ZROK_SHARE_URL="https://$ZROK_ENDPOINT"
            PUBLIC_URL="$ZROK_SHARE_URL/$SECRET_PATH/"
        else
            PUBLIC_URL="https://<zrok-share-url>/$SECRET_PATH/"
        fi
    fi
fi

LOCAL_URL="http://127.0.0.1:$LISTEN_PORT/$SECRET_PATH/"
FINAL_URL="${PUBLIC_URL:-$LOCAL_URL}"

# Save initial credentials for CLI
mkdir -p "$PREFIX/app/data"
cat > "$PREFIX/app/data/.credentials" <<EOF
{
  "username": "$ADMIN_USER",
  "password": "$ADMIN_PASS",
  "secretPath": "$SECRET_PATH",
  "publicUrl": "$FINAL_URL",
  "updatedAt": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
}
EOF
chmod 600 "$PREFIX/app/data/.credentials"

# 9. Setup Systemd Service
echo -e "${BLUE}[7/8] Configuring and starting systemd service...${NC}"
cat > /etc/systemd/system/openflux-zen-server.service <<EOF
[Unit]
Description=OpenFlux Zen Server Management Panel
After=network.target

[Service]
Type=simple
User=root
WorkingDirectory=$PREFIX/app
ExecStart=/usr/bin/dotnet $PREFIX/app/OpenFlux.Zen.Server.Web.dll
Restart=always
RestartSec=5
Environment=OPENFLUX_HOST=127.0.0.1
Environment=OPENFLUX_PORT=$LISTEN_PORT
Environment=OPENFLUX_SECRET_PATH=$SECRET_PATH
Environment=OPENFLUX_ADMIN_USER=$ADMIN_USER
Environment=OPENFLUX_ADMIN_PASSWORD=$ADMIN_PASS
Environment=OPENFLUX_PUBLIC_URL=$FINAL_URL
Environment=OPENFLUX_PUBLISH_MODE=$PUBLISH_MODE

[Install]
WantedBy=multi-user.target
EOF

systemctl daemon-reload
systemctl enable openflux-zen-server.service >/dev/null 2>&1
systemctl restart openflux-zen-server.service --no-block

# 10. Setup CLI Command in PATH
echo -e "${BLUE}[8/8] Installing OpenFluxZenServer CLI command in PATH...${NC}"
ln -sf "$PREFIX/app/OpenFluxZenServer" /usr/local/bin/OpenFluxZenServer
chmod +x "$PREFIX/app/OpenFluxZenServer" /usr/local/bin/OpenFluxZenServer

# Wait for service startup with live visual progress
echo -ne "${CYAN}Ожидание готовности службы OpenFlux Zen Server${NC}"
HEALTH_OK=0
for i in {1..30}; do
    if curl -sf "http://127.0.0.1:$LISTEN_PORT/$SECRET_PATH/api/health" 2>/dev/null | grep -q "healthy"; then
        HEALTH_OK=1
        echo -e " ${GREEN}✓ Готово!${NC}"
        break
    fi
    echo -ne "${CYAN}.${NC}"
    sleep 1
done

if [ "$HEALTH_OK" -eq 0 ]; then
    echo -e " ${YELLOW}(служба ещё инициализируется)${NC}"
fi

echo -e "\n${GREEN}${BOLD}=================================================================="
echo "      OpenFlux Zen Server УСПЕШНО УСТАНОВЛЕН И ЗАПУЩЕН!          "
echo "==================================================================${NC}"
echo -e "${CYAN}Панель управления (Секретная ссылка):${NC} ${BOLD}$FINAL_URL${NC}"
echo -e "${CYAN}Локальный адрес:${NC}                      ${BOLD}$LOCAL_URL${NC}"
echo -e "${CYAN}Логин:${NC}                                ${BOLD}$ADMIN_USER${NC}"
echo -e "${CYAN}Пароль:${NC}                               ${BOLD}$ADMIN_PASS${NC}"
echo -e "${CYAN}Секретный путь:${NC}                       ${BOLD}/$SECRET_PATH/${NC}"
echo -e "${YELLOW}CLI-команда доступна из любой папки:  ${BOLD}OpenFluxZenServer <command>${NC}"
echo "  OpenFluxZenServer start        - запуск службы сервера"
echo "  OpenFluxZenServer stop         - остановка службы сервера"
echo "  OpenFluxZenServer restart      - перезапуск службы сервера"
echo "  OpenFluxZenServer status       - текущий статус сервера"
echo "  OpenFluxZenServer autostart    - настройка автозапуска (enable | disable | status)"
echo "  OpenFluxZenServer credentials  - просмотр данных входа и ссылок"
echo "  OpenFluxZenServer help         - справка по всем командам"
echo "  OpenFluxZenServer uninstall    - полное удаление панели из системы"
echo "=================================================================="

if [ "$HEALTH_OK" -eq 1 ]; then
    echo -e "${GREEN}✓ Служба активна и отвечает на запросы.${NC}"
else
    echo -e "${YELLOW}! Служба запускается (проверьте статус через: systemctl status openflux-zen-server).${NC}"
fi

exit 0
