#!/usr/bin/env bash
set -e

export LC_ALL="${LC_ALL:-C.UTF-8}"
export LANG="${LANG:-C.UTF-8}"

# ==============================================================================
# OpenFlux Zen Server - Production Installer for Linux
# Multi-language (RU / EN), Beautiful Visual Presentation, Systemd & CLI Setup
# ==============================================================================

# ANSI Color Palette
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
CYAN='\033[0;36m'
MAGENTA='\033[0;35m'
GRAY='\033[0;90m'
BOLD='\033[1m'
NC='\033[0m' # No Color

# 1. Root check
if [ "$(id -u)" -ne 0 ]; then
    echo -e "${RED}[ERROR] This script must be run as root. Try: sudo bash install.sh${NC}"
    exit 1
fi

HAS_TTY=0
if [ -r /dev/tty ] && [ -w /dev/tty ]; then
    HAS_TTY=1
fi

# 2. Language Selection (Interactive or via Flag / Env)
# Prompt is displayed in English
CHOSEN_LANG=""
for arg in "$@"; do
    case "$arg" in
        --lang=ru|--ru|-ru) CHOSEN_LANG="ru" ;;
        --lang=en|--en|-en) CHOSEN_LANG="en" ;;
    esac
done

if [ -z "$CHOSEN_LANG" ] && [ -n "${INSTALL_LANG:-}" ]; then
    CHOSEN_LANG="$INSTALL_LANG"
fi

if [ -z "$CHOSEN_LANG" ]; then
    echo -e "\n${CYAN}${BOLD}=================================================================="
    echo -e "  OpenFlux Zen Server — Language Selection"
    echo -e "==================================================================${NC}"
    echo -e "  Please select your preferred language:"
    echo -e "    ${BOLD}1)${NC} English"
    echo -e "    ${BOLD}2)${NC} Russian (Русский)"
    if [ "$HAS_TTY" -eq 1 ]; then
        read -r -p "Enter choice [1/2, default: 1]: " LANG_INPUT < /dev/tty
    elif [ -t 0 ]; then
        read -r -p "Enter choice [1/2, default: 1]: " LANG_INPUT
    else
        LANG_INPUT="1"
    fi

    case "$LANG_INPUT" in
        2|[Rr][Uu]*) CHOSEN_LANG="ru" ;;
        *) CHOSEN_LANG="en" ;;
    esac
fi

# Language Dictionary
if [ "$CHOSEN_LANG" = "ru" ]; then
    TXT_TITLE="OpenFlux Zen Server — Установка и настройка"
    TXT_SUBTITLE="Универсальная серверная платформа туннелей"
    TXT_ERR_ARCH="[ОШИБКА] Неподдерживаемая архитектура процессора:"
    TXT_STEP1="[1/8] Проверка системных пакетов и зависимостей..."
    TXT_STEP2="[2/8] Проверка и подготовка среды .NET 10..."
    TXT_DOTNET_OK="✓ .NET 10 SDK уже установлен в системе"
    TXT_DOTNET_INSTALL="Установка .NET 10 через официальный скрипт Microsoft..."
    TXT_DOTNET_DONE="✓ .NET 10 успешно установлен"
    TXT_STEP3="[3/8] Получение исходного кода OpenFlux Zen Server..."
    TXT_REPO_UPDATE="Обновление существующего репозитория в"
    TXT_REPO_CLONE="Клонирование репозитория в"
    TXT_STEP4="[4/8] Сборка и публикация компонентов (Web & CLI)..."
    TXT_STEP5="[5/8] Генерация ключей доступа и учётных данных..."
    TXT_STEP6="[6/8] Настройка сетевого доступа и публикации..."
    TXT_NET_LOCAL_DESC="По умолчанию панель доступна только локально на сервере (127.0.0.1)."
    TXT_NET_PROMPT="Нужна ли сетевая доступность панели из интернета? [y/N]: "
    TXT_PUB_TITLE="Выберите режим публикации:"
    TXT_PUB_OPT1="  1) Открытые порты (Собственный домен + HTTPS с авто-сертификатом)"
    TXT_PUB_OPT2="  2) Через Localtunnel (Защищённый туннель без открытия портов, без токенов)"
    TXT_PUB_PROMPT="Ваш выбор [1/2]: "
    TXT_DOMAIN_PROMPT="Введите ваш домен (например, zen.example.com): "
    TXT_DOMAIN_CHECK="Проверка существующих SSL-сертификатов для"
    TXT_DOMAIN_CERT_FOUND="✓ Обнаружен действующий SSL-сертификат. Переиспользуем."
    TXT_LT_SELECTED="✓ Выбран Localtunnel: туннель запускается автоматически внутри службы сервера (без сторонних утилит и токенов)."
    TXT_LT_PASS_HINT="Пароль первого входа для loca.lt (IP сервера):"
    TXT_STEP7="[7/8] Настройка автозапуска и системной службы systemd..."
    TXT_AUTOSTART_PROMPT="Включить автозапуск сервера при загрузке системы? [Y/n]: "
    TXT_STEP8="[8/8] Регистрация команды OpenFluxZenServer в PATH..."
    TXT_WAIT_HEALTH="Ожидание готовности службы OpenFlux Zen Server"
    TXT_WAIT_TUNNEL="Ожидание готовности туннеля Localtunnel"
    TXT_HEALTH_OK="Готово!"
    TXT_HEALTH_INIT="(служба ещё инициализируется)"
    TXT_SUCCESS_TITLE="OpenFlux Zen Server — УСПЕШНО УСТАНОВЛЕН И ЗАПУЩЕН!"
    TXT_LBL_SECRET_URL="Панель управления (Секретная ссылка)"
    TXT_LBL_LOCAL_URL="Локальный адрес"
    TXT_LBL_CREDS_TITLE="Учётные данные"
    TXT_LBL_USER="Логин"
    TXT_LBL_PASS="Пароль"
    TXT_LBL_SECRET="Секретный путь"
    TXT_LBL_PUBMODE="Режим публикации"
    TXT_PUB_MODE_LT="Через Localtunnel (Zero-config)"
    TXT_PUB_MODE_DOMAIN="Открытые порты / Домен"
    TXT_PUB_MODE_LOCAL="Локальный (127.0.0.1)"
    TXT_LBL_LT_PASS="Пароль loca.lt (IP)"
    TXT_LBL_LANG="Язык интерфейса"
    TXT_LBL_AUTOSTART="Автозапуск"
    TXT_ENABLED="Включён"
    TXT_DISABLED="Выключен"
    TXT_LBL_CLI="Управление сервером через команду: OpenFluxZenServer <command>"
    TXT_CLI_START="запуск службы сервера"
    TXT_CLI_STOP="остановка службы сервера"
    TXT_CLI_RESTART="перезапуск службы сервера"
    TXT_CLI_STATUS="текущий статус сервера"
    TXT_CLI_AUTOSTART="настройка автозапуска (enable | disable | status)"
    TXT_CLI_CREDS="просмотр данных входа и ссылок"
    TXT_CLI_HELP="справка по всем командам"
    TXT_CLI_UNINSTALL="полное удаление панели из системы"
    TXT_SRV_ACTIVE="✓ Служба активна и отвечает на запросы."
    TXT_SRV_STARTING="! Служба запускается (проверьте: systemctl status openflux-zen-server)."
else
    TXT_TITLE="OpenFlux Zen Server — Installer & Setup"
    TXT_SUBTITLE="Universal High-Performance Tunnel Platform"
    TXT_ERR_ARCH="[ERROR] Unsupported CPU architecture:"
    TXT_STEP1="[1/8] Checking prerequisite system packages..."
    TXT_STEP2="[2/8] Checking and preparing .NET 10 environment..."
    TXT_DOTNET_OK="✓ .NET 10 SDK is already installed"
    TXT_DOTNET_INSTALL="Installing .NET 10 via official Microsoft script..."
    TXT_DOTNET_DONE="✓ .NET 10 installed successfully"
    TXT_STEP3="[3/8] Fetching OpenFlux Zen Server repository..."
    TXT_REPO_UPDATE="Updating existing repository in"
    TXT_REPO_CLONE="Cloning repository into"
    TXT_STEP4="[4/8] Building and publishing components (Web & CLI)..."
    TXT_STEP5="[5/8] Generating secure access keys and credentials..."
    TXT_STEP6="[6/8] Configuring network accessibility and publishing..."
    TXT_NET_LOCAL_DESC="By default, the dashboard is accessible locally only (127.0.0.1)."
    TXT_NET_PROMPT="Do you want the dashboard accessible from the internet? [y/N]: "
    TXT_PUB_TITLE="Select publishing mode:"
    TXT_PUB_OPT1="  1) Open ports (Custom domain + secure HTTPS with auto-certificate)"
    TXT_PUB_OPT2="  2) Via Localtunnel (Encrypted tunnel without open ports, zero-config)"
    TXT_PUB_PROMPT="Your choice [1/2]: "
    TXT_DOMAIN_PROMPT="Enter your domain (e.g. zen.example.com): "
    TXT_DOMAIN_CHECK="Checking existing SSL certificates for"
    TXT_DOMAIN_CERT_FOUND="✓ Valid SSL certificate found. Reusing without reissuing."
    TXT_LT_SELECTED="✓ Localtunnel selected: tunnel runs automatically inside server service (zero external tools or tokens)."
    TXT_LT_PASS_HINT="First-time loca.lt password (server IP):"
    TXT_STEP7="[7/8] Configuring autostart and systemd service..."
    TXT_AUTOSTART_PROMPT="Enable server autostart on system boot? [Y/n]: "
    TXT_STEP8="[8/8] Installing OpenFluxZenServer CLI command in PATH..."
    TXT_WAIT_HEALTH="Waiting for OpenFlux Zen Server service readiness"
    TXT_WAIT_TUNNEL="Waiting for Localtunnel readiness"
    TXT_HEALTH_OK="Done!"
    TXT_HEALTH_INIT="(service is still initializing)"
    TXT_SUCCESS_TITLE="OpenFlux Zen Server — SUCCESSFULLY INSTALLED & STARTED!"
    TXT_LBL_SECRET_URL="Web Dashboard URL (Secret link)"
    TXT_LBL_LOCAL_URL="Local Access URL"
    TXT_LBL_CREDS_TITLE="Authentication Details"
    TXT_LBL_USER="Username"
    TXT_LBL_PASS="Password"
    TXT_LBL_SECRET="Secret Path"
    TXT_LBL_PUBMODE="Publish Mode"
    TXT_PUB_MODE_LT="Via Localtunnel (Zero-config)"
    TXT_PUB_MODE_DOMAIN="Open ports / Domain"
    TXT_PUB_MODE_LOCAL="Local only (127.0.0.1)"
    TXT_LBL_LT_PASS="loca.lt Password (IP)"
    TXT_LBL_LANG="Interface Language"
    TXT_LBL_AUTOSTART="Autostart"
    TXT_ENABLED="Enabled"
    TXT_DISABLED="Disabled"
    TXT_LBL_CLI="CLI command available anywhere: OpenFluxZenServer <command>"
    TXT_CLI_START="start server service"
    TXT_CLI_STOP="stop server service"
    TXT_CLI_RESTART="restart server service"
    TXT_CLI_STATUS="show server status"
    TXT_CLI_AUTOSTART="configure autostart (enable | disable | status)"
    TXT_CLI_CREDS="view login credentials and URLs"
    TXT_CLI_HELP="show CLI command help"
    TXT_CLI_UNINSTALL="completely uninstall from system"
    TXT_SRV_ACTIVE="✓ Service is running and responding."
    TXT_SRV_STARTING="! Service is starting up (check: systemctl status openflux-zen-server)."
fi

# Visual Row Alignment Helpers
print_kv_row() {
    local label="$1:"
    local value="$2"
    local target=22
    local clen=${#label}
    local pad=$((target - clen))
    [ $pad -lt 1 ] && pad=1
    local spaces=$(printf "%*s" "$pad" "")
    echo -e "    ${CYAN}${label}${NC}${spaces}${value}"
}

print_cli_row() {
    local cmd="$1"
    local desc="$2"
    local target=32
    local clen=${#cmd}
    local pad=$((target - clen))
    [ $pad -lt 1 ] && pad=1
    local spaces=$(printf "%*s" "$pad" "")
    echo -e "    ${BOLD}${cmd}${NC}${spaces}— ${desc}"
}

# Print Header Banner
echo -e "\n${CYAN}${BOLD}=================================================================="
echo -e "  $TXT_TITLE"
echo -e "  $TXT_SUBTITLE"
echo -e "==================================================================${NC}"
echo -e "${GRAY}Language: [${CHOSEN_LANG^^}]${NC}\n"

PREFIX="${OPENFLUX_PREFIX:-/opt/openflux-zen-server}"
REPO_URL="https://github.com/BizhQwe/openflux-zen-server.git"

# 2. Package manager dependencies
echo -e "${BLUE}${BOLD}${TXT_STEP1}${NC}"
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
    x86_64) DOTNET_ARCH="x64" ;;
    aarch64|arm64) DOTNET_ARCH="arm64" ;;
    *) echo -e "${RED}${TXT_ERR_ARCH} $ARCH${NC}"; exit 1 ;;
esac

# 4. Check or install .NET 10 SDK / Runtime
echo -e "${BLUE}${BOLD}${TXT_STEP2}${NC}"
NEED_DOTNET=1
if command -v dotnet >/dev/null 2>&1; then
    if dotnet --list-sdks 2>/dev/null | grep -q "^10\."; then
        NEED_DOTNET=0
        echo -e "${GREEN}${TXT_DOTNET_OK}${NC}"
    fi
fi

if [ "$NEED_DOTNET" -eq 1 ]; then
    echo -e "${YELLOW}${TXT_DOTNET_INSTALL}${NC}"
    curl -sSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
    chmod +x /tmp/dotnet-install.sh
    /tmp/dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet >/dev/null
    ln -sf /usr/share/dotnet/dotnet /usr/bin/dotnet
    echo -e "${GREEN}${TXT_DOTNET_DONE}${NC}"
fi

# 5. Download / Clone Repository
echo -e "${BLUE}${BOLD}${TXT_STEP3}${NC}"
if [ -d "$PREFIX/.git" ]; then
    echo -e "${GRAY}${TXT_REPO_UPDATE} $PREFIX...${NC}"
    git -C "$PREFIX" fetch --all --prune >/dev/null 2>&1
    git -C "$PREFIX" reset --hard origin/main >/dev/null 2>&1
else
    if [ -d "$PREFIX" ]; then
        rm -rf "$PREFIX"
    fi
    mkdir -p "$PREFIX"
    echo -e "${GRAY}${TXT_REPO_CLONE} $PREFIX...${NC}"
    git clone --depth 1 "$REPO_URL" "$PREFIX" >/dev/null 2>&1
fi

# 6. Publish Application
echo -e "${BLUE}${BOLD}${TXT_STEP4}${NC}"

# Stop any running instances/tunnels to prevent "Text file busy" (ETXTBSY)
systemctl stop openflux-zen-server.service 2>/dev/null || true
pkill -9 -f openflux-linux 2>/dev/null || true

cd "$PREFIX"
dotnet publish src/OpenFlux.Zen.Server.Web/OpenFlux.Zen.Server.Web.csproj -c Release -o "$PREFIX/app" >/dev/null
dotnet publish src/OpenFlux.Zen.Server.Cli/OpenFlux.Zen.Server.Cli.csproj -c Release -o "$PREFIX/app" >/dev/null
dotnet build-server shutdown >/dev/null 2>&1 || true
chmod +x "$PREFIX/app/OpenFluxZenServer" || true

# Copy runtimes and set permissions (remove destination first to unlink busy inodes)
mkdir -p "$PREFIX/app/runtimes"
rm -f "$PREFIX/app/runtimes/"* 2>/dev/null || true
cp -f -r "$PREFIX/runtimes/"* "$PREFIX/app/runtimes/"
chmod +x "$PREFIX/app/runtimes/"* || true

# 7. Generate Security Credentials
echo -e "${BLUE}${BOLD}${TXT_STEP5}${NC}"

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
echo -e "${BLUE}${BOLD}${TXT_STEP6}${NC}"
PUBLISH_MODE="local"
PUBLIC_URL=""
DOMAIN=""
LOCALTUNNEL_PASSWORD=""

# Non-interactive overrides via environment variables
if [ -n "${OPENFLUX_NETWORK_ACCESS:-}" ]; then
    NET_ACCESS="$OPENFLUX_NETWORK_ACCESS"
elif [ "$HAS_TTY" -eq 1 ]; then
    echo -e "${CYAN}${TXT_NET_LOCAL_DESC}${NC}"
    read -r -p "$TXT_NET_PROMPT" NET_ACCESS < /dev/tty
elif [ -t 0 ]; then
    echo -e "${CYAN}${TXT_NET_LOCAL_DESC}${NC}"
    read -r -p "$TXT_NET_PROMPT" NET_ACCESS
else
    NET_ACCESS="n"
fi

if [[ "$NET_ACCESS" =~ ^[Yy]$ ]]; then
    if [ -n "${OPENFLUX_PUBLISH_MODE:-}" ]; then
        PUB_CHOICE="$OPENFLUX_PUBLISH_MODE"
    elif [ "$HAS_TTY" -eq 1 ]; then
        echo -e "${CYAN}${TXT_PUB_TITLE}${NC}"
        echo "$TXT_PUB_OPT1"
        echo "$TXT_PUB_OPT2"
        read -r -p "$TXT_PUB_PROMPT" PUB_CHOICE < /dev/tty
    elif [ -t 0 ]; then
        echo -e "${CYAN}${TXT_PUB_TITLE}${NC}"
        echo "$TXT_PUB_OPT1"
        echo "$TXT_PUB_OPT2"
        read -r -p "$TXT_PUB_PROMPT" PUB_CHOICE
    else
        PUB_CHOICE="1"
    fi

    if [ "$PUB_CHOICE" = "1" ]; then
        PUBLISH_MODE="domain"
        if [ -n "${OPENFLUX_DOMAIN:-}" ]; then
            DOMAIN="$OPENFLUX_DOMAIN"
        elif [ "$HAS_TTY" -eq 1 ]; then
            read -r -p "$TXT_DOMAIN_PROMPT" DOMAIN < /dev/tty
        else
            read -r -p "$TXT_DOMAIN_PROMPT" DOMAIN
        fi

        echo -e "${YELLOW}${TXT_DOMAIN_CHECK} $DOMAIN...${NC}"
        EXISTING_CERT=""
        if [ -f "/etc/letsencrypt/live/$DOMAIN/fullchain.pem" ]; then
            EXISTING_CERT="/etc/letsencrypt/live/$DOMAIN/fullchain.pem"
        elif [ -f "/etc/ssl/certs/$DOMAIN.crt" ]; then
            EXISTING_CERT="/etc/ssl/certs/$DOMAIN.crt"
        fi

        if [ -n "$EXISTING_CERT" ]; then
            echo -e "${GREEN}${TXT_DOMAIN_CERT_FOUND}${NC}"
        else
            if ss -tulpn | grep -qE ':(80|443)\s'; then
                if command -v nginx >/dev/null 2>&1; then
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

    elif [ "$PUB_CHOICE" = "2" ] || [ "$PUB_CHOICE" = "localtunnel" ] || [ "$PUB_CHOICE" = "tunnel" ]; then
        PUBLISH_MODE="localtunnel"

        # Stop and remove any legacy Zrok service if present
        systemctl stop openflux-zrok.service 2>/dev/null || true
        systemctl disable openflux-zrok.service 2>/dev/null || true
        rm -f /etc/systemd/system/openflux-zrok.service 2>/dev/null || true

        # Detect public IP for loca.lt browser friendly reminder
        SERVER_IP=$(curl -sSL --connect-timeout 4 https://api.ipify.org 2>/dev/null || curl -sSL --connect-timeout 4 https://ifconfig.me/ip 2>/dev/null || echo "")
        LOCALTUNNEL_PASSWORD="$SERVER_IP"

        SUBDOMAIN_PREFIX="openflux-${SECRET_PATH:0:8}"
        PUBLIC_URL="https://${SUBDOMAIN_PREFIX}.loca.lt/${SECRET_PATH}/"

        echo -e "${GREEN}${TXT_LT_SELECTED}${NC}"
        if [ -n "$LOCALTUNNEL_PASSWORD" ]; then
            echo -e "${YELLOW}  ${TXT_LT_PASS_HINT} ${LOCALTUNNEL_PASSWORD}${NC}"
        fi
    fi
fi

LOCAL_URL="http://127.0.0.1:$LISTEN_PORT/$SECRET_PATH/"
FINAL_URL="${PUBLIC_URL:-$LOCAL_URL}"

# 9. Autostart Choice & Setup Systemd Service
echo -e "${BLUE}${BOLD}${TXT_STEP7}${NC}"
AUTOSTART_ENABLED=1
if [ -n "${OPENFLUX_AUTOSTART:-}" ]; then
    if [[ "$OPENFLUX_AUTOSTART" =~ ^(0|[Nn]|[Ff]alse)$ ]]; then
        AUTOSTART_ENABLED=0
    fi
elif [ "$HAS_TTY" -eq 1 ]; then
    read -r -p "$TXT_AUTOSTART_PROMPT" AUTOSTART_CHOICE < /dev/tty
    if [[ "$AUTOSTART_CHOICE" =~ ^[Nn] ]]; then
        AUTOSTART_ENABLED=0
    fi
elif [ -t 0 ]; then
    read -r -p "$TXT_AUTOSTART_PROMPT" AUTOSTART_CHOICE
    if [[ "$AUTOSTART_CHOICE" =~ ^[Nn] ]]; then
        AUTOSTART_ENABLED=0
    fi
fi

# Save initial credentials for CLI
mkdir -p "$PREFIX/app/data"
cat > "$PREFIX/app/data/.credentials" <<EOF
{
  "username": "$ADMIN_USER",
  "password": "$ADMIN_PASS",
  "secretPath": "$SECRET_PATH",
  "publicUrl": "$FINAL_URL",
  "publishMode": "$PUBLISH_MODE",
  "localtunnelPassword": "$LOCALTUNNEL_PASSWORD",
  "language": "$CHOSEN_LANG",
  "autostart": $AUTOSTART_ENABLED,
  "updatedAt": "$(date -u +"%Y-%m-%dT%H:%M:%SZ")"
}
EOF
chmod 600 "$PREFIX/app/data/.credentials"

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
Environment=OPENFLUX_LANGUAGE=$CHOSEN_LANG
Environment=DOTNET_gcServer=0
Environment=DOTNET_GCHeapHardLimit=80000000

[Install]
WantedBy=multi-user.target
EOF

# Ensure IPv4 forwarding is active for OpenFlux tunnels
echo "net.ipv4.ip_forward = 1" > /etc/sysctl.d/99-openflux.conf
sysctl --system >/dev/null 2>&1 || true

systemctl daemon-reload
if [ "$AUTOSTART_ENABLED" -eq 1 ]; then
    systemctl enable openflux-zen-server.service >/dev/null 2>&1
else
    systemctl disable openflux-zen-server.service >/dev/null 2>&1 || true
fi
systemctl restart openflux-zen-server.service --no-block

# 10. Setup CLI Command in PATH
echo -e "${BLUE}${BOLD}${TXT_STEP8}${NC}"
ln -sf "$PREFIX/app/OpenFluxZenServer" /usr/local/bin/OpenFluxZenServer
chmod +x "$PREFIX/app/OpenFluxZenServer" /usr/local/bin/OpenFluxZenServer

# Wait for service startup with live visual progress
echo -ne "${CYAN}${TXT_WAIT_HEALTH}${NC}"
HEALTH_OK=0
for i in {1..30}; do
    if curl -sf "http://127.0.0.1:$LISTEN_PORT/$SECRET_PATH/api/health" 2>/dev/null | grep -q "healthy"; then
        HEALTH_OK=1
        echo -e " ${GREEN}✓ ${TXT_HEALTH_OK}${NC}"
        break
    fi
    echo -ne "${CYAN}.${NC}"
    sleep 1
done

if [ "$HEALTH_OK" -eq 0 ]; then
    echo -e " ${YELLOW}${TXT_HEALTH_INIT}${NC}"
fi

# Wait for Localtunnel URL in .credentials if localtunnel mode was chosen
if [ "$PUBLISH_MODE" = "localtunnel" ]; then
    echo -ne "${CYAN}  ${TXT_WAIT_TUNNEL}${NC}"
    for i in {1..30}; do
        if [ -f "$PREFIX/app/data/.credentials" ]; then
            LIVE_URL=$(grep -o '"publicUrl": "[^"]*"' "$PREFIX/app/data/.credentials" 2>/dev/null | cut -d'"' -f4 || true)
            LIVE_PASS=$(grep -o '"localtunnelPassword": "[^"]*"' "$PREFIX/app/data/.credentials" 2>/dev/null | cut -d'"' -f4 || true)
            if [[ "$LIVE_URL" =~ \.loca\.lt ]] && [ -n "$LIVE_PASS" ]; then
                FINAL_URL="$LIVE_URL"
                LOCALTUNNEL_PASSWORD="$LIVE_PASS"
                echo -e " ${GREEN}✓${NC}"
                break
            fi
        fi
        echo -ne "${CYAN}.${NC}"
        sleep 0.5
    done
    echo ""
fi

PUB_MODE_DISPLAY="$TXT_PUB_MODE_LOCAL"
if [ "$PUBLISH_MODE" = "localtunnel" ]; then
    PUB_MODE_DISPLAY="$TXT_PUB_MODE_LT"
elif [ "$PUBLISH_MODE" = "domain" ]; then
    if [ -n "$DOMAIN" ]; then
        PUB_MODE_DISPLAY="Domain ($DOMAIN)"
    else
        PUB_MODE_DISPLAY="$TXT_PUB_MODE_DOMAIN"
    fi
fi

# Print Final Summary Card (Pixel-perfect column alignment)
echo -e "\n${GREEN}${BOLD}=================================================================="
echo -e "  $TXT_SUCCESS_TITLE"
echo -e "==================================================================${NC}\n"

echo -e "${CYAN}${BOLD}  ${TXT_LBL_SECRET_URL}:${NC}"
echo -e "    ${BOLD}${YELLOW}$FINAL_URL${NC}\n"

echo -e "${CYAN}${BOLD}  ${TXT_LBL_LOCAL_URL}:${NC}"
echo -e "    ${BOLD}$LOCAL_URL${NC}\n"

echo -e "${CYAN}${BOLD}  ${TXT_LBL_CREDS_TITLE}:${NC}"
print_kv_row "$TXT_LBL_USER" "${BOLD}$ADMIN_USER${NC}"
print_kv_row "$TXT_LBL_PASS" "${BOLD}$ADMIN_PASS${NC}"
print_kv_row "$TXT_LBL_SECRET" "${BOLD}/$SECRET_PATH/${NC}"
print_kv_row "$TXT_LBL_PUBMODE" "${BOLD}$PUB_MODE_DISPLAY${NC}"
if [ "$PUBLISH_MODE" = "localtunnel" ] && [ -n "$LOCALTUNNEL_PASSWORD" ]; then
    LT_VISIT_HINT="(первый вход в браузере / first browser visit)"
    print_kv_row "$TXT_LBL_LT_PASS" "${YELLOW}${BOLD}$LOCALTUNNEL_PASSWORD${NC} ${GRAY}${LT_VISIT_HINT}${NC}"
fi
print_kv_row "$TXT_LBL_LANG" "${BOLD}${CHOSEN_LANG^^}${NC}"
if [ "$AUTOSTART_ENABLED" -eq 1 ]; then
    print_kv_row "$TXT_LBL_AUTOSTART" "${GREEN}${BOLD}${TXT_ENABLED}${NC}"
else
    print_kv_row "$TXT_LBL_AUTOSTART" "${GRAY}${BOLD}${TXT_DISABLED}${NC}"
fi

echo -e "\n${GREEN}${BOLD}------------------------------------------------------------------${NC}"
echo -e "${YELLOW}${BOLD}  ${TXT_LBL_CLI}:${NC}\n"
print_cli_row "OpenFluxZenServer status" "$TXT_CLI_STATUS"
print_cli_row "OpenFluxZenServer restart" "$TXT_CLI_RESTART"
print_cli_row "OpenFluxZenServer credentials" "$TXT_CLI_CREDS"
print_cli_row "OpenFluxZenServer autostart" "$TXT_CLI_AUTOSTART"
print_cli_row "OpenFluxZenServer help" "$TXT_CLI_HELP"
echo -e "\n${GREEN}${BOLD}==================================================================${NC}\n"

if [ "$HEALTH_OK" -eq 1 ]; then
    echo -e "${GREEN}${TXT_SRV_ACTIVE}${NC}\n"
else
    echo -e "${YELLOW}${TXT_SRV_STARTING}${NC}\n"
fi

exit 0
