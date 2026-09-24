# OpenFlux Zen Server

Production-ready кроссплатформенная web-панель управления серверной частью **OpenFlux** на **ASP.NET Core / .NET 10**.

Поддерживает **Windows** и **Linux** (архитектуры **x64** и **arm64**).

---

## Возможности

- **Полная поддержка всех типов туннелей и параметров OpenFlux:**
  - Роли: `exit` (выходная нода), `client` (клиент), `bench-send`, `bench-sink`
  - Транспорты: `yandex` (WebSocket), `vyandex` (HTTP relay), `oneme` (WebRTC), `cupsonline` (Centrifugo rooms), `mailru` (WebSocket)
  - Режимы выхода: `l4` (stream proxy via gVisor, универсальный) и `l3` (raw SNAT/DNAT, Linux root)
  - Клиентские интерфейсы: `socks5` (с адресом, по умолчанию `:1080`) и `tun` (utun)
  - Кодеки: `batched` (zstd + склейка пакетов) и `legacy` (LZ4)
  - Шифрование: опциональный общий ключ AES-256-GCM
  - Бенчмарки: `--bench-bytes` и `--bench-compressible`
  - Каждый туннель запускается с обязательным флагом `-debug`
- **Жизненный цикл и State Machine:**
  - Создание, редактирование, удаление, запуск (`Start`), остановка (`Stop`)
  - Включение/отключение туннелей (`IsEnabled` toggle)
  - **Восстановление после перезапуска хоста или сбоя:** при старте панели автоматически запускаются **только те туннели, которые были включены (`IsEnabled == true`)** до остановки
  - Автоматический перезапуск туннеля при неожиданном падении процесса
- **Статистика и лимиты:**
  - Статистика по каждому туннелю: Upload/Download трафик, число подключённых клиентов
  - Настраиваемые лимиты клиентов и лимиты трафика (с автоматической остановкой при исчерпании лимита)
  - Общая статистика сервера: число туннелей, активные туннели, общий суммарный трафик, загрузка CPU, использование RAM, аптайм
- **Логирование:**
  - Структурированные логи через `NLog.Extensions.Logging` (консоль и ротация файлов)
  - Отдельный изолированный файл лога для каждого туннеля (`logs/tunnels/{id}.log`)
  - Просмотр live-логов в реальном времени через веб-интерфейс
- **Резервное копирование:**
  - Экспорт всей конфигурации в JSON
  - Импорт конфигурации JSON для быстрого переноса на другой сервер
- **Скрытность и безопасность (Stealth Access):**
  - По умолчанию панель не светится в сети (`127.0.0.1`)
  - Случайный секретный путь URL (например, `https://domain.com/zen-a1b2c3d4/` или через Zrok)
  - Корневой URL `/` и любые неавторизованные пути возвращают `404 Not Found`, полностью скрывая наличие панели
  - Пароли хешируются с использованием PBKDF2 (SHA256, 100 000 итераций)
- **Публикация панели:**
  - **Режим 1 (Открытые порты / Домен):** автоматическая настройка HTTPS, поиск и переиспользование действующих SSL-сертификатов без повторного выпуска, проверка конфликтов портов с 3x-ui / Nginx
  - **Режим 2 (Zrok):** защищённый туннель через Zrok без открытия входящих портов наружу
- **Фирменный CLI (`OpenFluxZenServer`):**
  - Доступен из любого каталога через системный `PATH`
  - Содержит строго три команды: `help`, `credentials` и `uninstall`
  - Полное удаление программы из системы без каких-либо остатков (удаление служб, бинарников, БД, логов, данных и записей автозапуска с сохранением всех SSL-сертификатов сервера)

---

## Быстрая установка

### Linux (Ubuntu / Debian / AlmaLinux / CentOS)

Установка в одну команду:
```bash
curl -fsSL https://raw.githubusercontent.com/BizhQwe/openflux-zen-server/main/installers/install.sh | bash
```

Или клонированием:
```bash
git clone https://github.com/BizhQwe/openflux-zen-server.git /opt/openflux-zen-server
bash /opt/openflux-zen-server/installers/install.sh
```

В процессе скрипт:
1. Автоматически проверит и при необходимости установит .NET 10.
2. Спросит о необходимости сетевой доступности (Локально / Домен с HTTPS / Zrok).
3. Сгенерирует случайный секретный URL и учётные данные.
4. Настроит автозапуск службы `systemd` (`openflux-zen-server.service`).
5. Установит команду `OpenFluxZenServer` в `PATH`.
6. Выведет итоговую защищённую ссылку и данные для входа.

### Windows

1. Скачайте или клонируйте репозиторий:
   ```cmd
   git clone https://github.com/BizhQwe/openflux-zen-server.git
   ```
2. Запустите от имени Администратора:
   ```cmd
   installers\install.bat
   ```

Скрипт опубликует бинарники, создаст задание автозапуска, добавит `OpenFluxZenServer` в `PATH` и запустит сервер.

---

## Использование CLI (`OpenFluxZenServer`)

Команда доступна из любого терминала:

```bash
# 1. Справка
OpenFluxZenServer help

# 2. Просмотр текущих учётных данных и секретной ссылки на панель
OpenFluxZenServer credentials

# 3. Полное удаление панели из системы
OpenFluxZenServer uninstall
```

---

## Архитектура проекта

```
openflux-zen-server/
├── cli/
│   ├── OpenFluxZenServer.bat       # Windows CLI обёртка в PATH
│   └── OpenFluxZenServer.sh        # Linux CLI обёртка в PATH
├── installers/
│   ├── install.sh                  # Единый установщик Linux
│   ├── install.bat                 # Единый установщик Windows
│   └── uninstall.bat               # Деинсталлятор Windows
├── runtimes/                       # Нативные скомпилированные бинарники OpenFlux
│   ├── openflux-linux-amd64
│   ├── openflux-linux-arm64
│   ├── openflux-windows-amd64.exe
│   └── openflux-windows-arm64.exe
├── src/
│   └── OpenFlux.Zen.Server/        # Основной проект панели ASP.NET Core 10
│       ├── Data/                   # AppDbContext и миграции SQLite
│       │   ├── AppDbContext.cs
│       │   └── Migrations/         # EF Core миграции
│       ├── Middleware/
│       │   └── SecretPathMiddleware.cs # Stealth защита и маршрутизация
│       ├── Models/                 # Доменные модели (Tunnel, AppSettings, DTO)
│       ├── Services/               # Бизнес-логика, State Machine, Supervisor
│       │   ├── ITunnelManager.cs & TunnelManager.cs
│       │   ├── ITunnelProcessSupervisor.cs & TunnelProcessSupervisor.cs
│       │   ├── ITunnelLogService.cs & TunnelLogService.cs
│       │   ├── ISystemStatsService.cs & SystemStatsService.cs
│       │   ├── IAuthService.cs & AuthService.cs
│       │   ├── IExportImportService.cs & ExportImportService.cs
│       │   ├── IOpenFluxBinaryResolver.cs & OpenFluxBinaryResolver.cs
│       │   ├── IUninstallerService.cs & UninstallerService.cs
│       │   └── HostedRestoreService.cs # Восстановление туннелей на старте
│       ├── nlog.config             # Конфигурация NLog
│       └── wwwroot/                # SPA веб-интерфейс и логотипы
└── README.md
```

---

## Лицензия

GPLv3 / OpenFlux
