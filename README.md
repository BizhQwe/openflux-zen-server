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
curl -fsSL https://raw.githubusercontent.com/BizhQwe/openflux-zen-server/main/scripts/install.sh | bash
```

Или клонированием:
```bash
git clone https://github.com/BizhQwe/openflux-zen-server.git /opt/openflux-zen-server
bash /opt/openflux-zen-server/scripts/install.sh
```

В процессе скрипт:
1. Автоматически проверит и при необходимости установит .NET 10.
2. Спросит о необходимости сетевой доступности (Локально / Домен с HTTPS / Zrok).
3. Сгенерирует случайный секретный URL и учётные данные.
4. Настроит автозапуск службы `systemd` (`openflux-zen-server.service`).
5. Установит команду `OpenFluxZenServer` в `PATH`.
6. Выведет итоговую защищённую ссылку и данные для входа.

### Windows (10 / 11 / Windows Server)

Установка в одну команду (PowerShell от Администратора):
```powershell
irm https://raw.githubusercontent.com/BizhQwe/openflux-zen-server/main/scripts/install.ps1 | iex
```

Либо из стандартного CMD от имени Администратора:
```cmd
powershell -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/BizhQwe/openflux-zen-server/main/scripts/install.ps1 | iex"
```

Скрипт автоматически:
1. Проверит и при необходимости сам установит .NET 10 SDK через официальный установщик Microsoft.
2. Склонирует или загрузит актуальный архив репозитория.
3. Опубликует бинарники Web-панели и CLI.
4. Настроит автозапуск в Планировщике заданий (Task Scheduler).
5. Добавит `OpenFluxZenServer` в системный `PATH`.
6. Запустит сервер и выведет все ссылки и данные для входа.

---

## Использование CLI (`OpenFluxZenServer`)

Команда доступна из любого терминала (PowerShell / CMD / Bash):

```bash
# Просмотр статуса службы
OpenFluxZenServer status

# Перезапуск службы
OpenFluxZenServer restart

# Просмотр текущих учётных данных и ссылки на панель
OpenFluxZenServer credentials

# Управление автозапуском (enable | disable | status)
OpenFluxZenServer autostart status

# Справка по всем командам
OpenFluxZenServer help

# Полное удаление панели из системы
OpenFluxZenServer uninstall
```

---

## Архитектура проекта

Решение разделено на 3 специализированных проекта .NET 10:

```
openflux-zen-server/
├── scripts/   
│   ├── install.sh                              # Единый установщик Linux
│   └── install.bat                             # Единый установщик Windows
├── runtimes/                                   # Нативные скомпилированные бинарники OpenFlux
│   ├── openflux-linux-amd64
│   ├── openflux-linux-arm64
│   ├── openflux-windows-amd64.exe
│   └── openflux-windows-arm64.exe
├── src/
│   ├── OpenFlux.Zen.Server.Core/               # Ядро системы: модели, БД, авторизация, пути
│   │   ├── Common/ (AppPaths.cs)
│   │   ├── Data/ (AppDbContext.cs)
│   │   ├── Migrations/ (EF Core миграции SQLite)
│   │   ├── Models/ (Tunnel, AppSettings, DTO)
│   │   └── Services/ (AuthService)
│   │
│   ├── OpenFlux.Zen.Server.Web/                # Веб-служба ASP.NET Core 10 / Kestrel
│   │   ├── Middleware/ (SecretPathMiddleware.cs)
│   │   ├── Services/ (Supervision, LogService, SystemStats, ExportImport, Uninstaller)
│   │   ├── wwwroot/ (SPA панель управления)
│   │   └── Program.cs
│   │
│   └── OpenFlux.Zen.Server.Cli/                # Консольная утилита OpenFluxZenServer
│       └── Program.cs                          # Реализация команд help, credentials, uninstall
│
├── openflux-zen-server.slnx                    # Решение Visual Studio
└── README.md
```

---

## Лицензия

GPLv3 / OpenFlux
