# OpenFlux Zen Server

Кроссплатформенная панель ASP.NET Core/.NET 10 для управления OpenFlux. Использует SQLite, NLog и поставляемые runtime-бинарники OpenFlux для Windows/Linux x64/arm64.

## Локальный запуск

```powershell
$env:OPENFLUX_ADMIN_USER='admin'; $env:OPENFLUX_ADMIN_PASSWORD='change-me'; dotnet run --project src/OpenFlux.Zen.Server
```

По умолчанию сервер слушает только localhost. Данные хранятся в `data/openflux.db`, логи панели в `logs/panel.log`, процессы туннелей запускаются с `-debug`.

## Установка

Windows: `installers/install.bat` (опционально `OPENFLUX_ADMIN_USER` и `OPENFLUX_ADMIN_PASSWORD`). Linux: `sudo bash installers/install.sh`. Скрипты скачивают репозиторий, публикуют приложение и регистрируют автозапуск службы. Для внешней публикации используйте reverse proxy с существующим сертификатом; установщик не изменяет чужие порты и сертификаты.

## CLI

`OpenFluxZenServer help`, `OpenFluxZenServer credentials`, `OpenFluxZenServer uninstall`.
