# Сборка через GitHub Actions

Workflow: [`.github/workflows/build.yml`](../.github/workflows/build.yml).

Загрузите в корень репозитория весь проект: `.github`, `scripts`, `src`, `go.work`, файлы лицензии и документацию. Папка `dist` и локальные данные панели не нужны. Модули в `src` собираются вместе через `go.work`; версия Go берётся из этого файла.

## Сборка без публикации

Откройте **Actions → Build OpenFlux Zen Server → Run workflow**, выберите ветку и запустите сборку. Она также запускается при push в `main` / `master` и для pull request.

Сначала выполняются тесты и `go vet` на Windows и Linux, затем собираются семь цельных установщиков:

| Платформа | Файл |
|---|---|
| Windows x64 | `openflux-zen-server-installer-windows-amd64.exe` |
| Windows ARM64 | `openflux-zen-server-installer-windows-arm64.exe` |
| Windows x86 | `openflux-zen-server-installer-windows-386.exe` |
| Linux x64 | `openflux-zen-server-installer-linux-amd64` |
| Linux ARM64 | `openflux-zen-server-installer-linux-arm64` |
| Linux x86 | `openflux-zen-server-installer-linux-386` |
| Linux ARMv7 | `openflux-zen-server-installer-linux-armv7` |

Файлы находятся в разделе **Artifacts** завершённого запуска. У каждого установщика есть файл `.sha256`. Артефакты хранятся 14 дней. Каждый установщик содержит ядро, веб-панель и CLI; отдельные бинарники для установки не нужны.

## Публикация релиза

Создайте и отправьте тег, например для текущей версии проекта:

```bash
git tag v2.0.0
git push origin v2.0.0
```

Push тега `v*` запускает проверки и сборку, затем создаёт GitHub Release. Тег должен указывать на коммит с workflow. Для будущих версий сначала обновите версию в исходниках: сам тег не меняет версию программы.

В релиз попадут семь установщиков, их `.sha256`, `SHA256SUMS.txt`, `install.sh`, `install.ps1` и файлы лицензии. Проверка всех семи установщиков обязательна перед публикацией. Релиз создаётся как черновик и публикуется после загрузки файлов. Повторный запуск может завершить оставшийся черновик; файлы уже опубликованного релиза не заменяются.

Тег с дефисом, например `v2.1.0-beta.1`, публикуется как prerelease и не становится Latest. Обычный релиз становится Latest, поэтому существующие скрипты установки могут скачать его без изменений.

Дополнительные secrets или персональный токен не нужны: используется стандартный `GITHUB_TOKEN`. Право `contents: write` есть только у задачи публикации. Если организация запрещает запись из Actions, разрешите её в настройках репозитория/организации.

## Проверка упаковки локально

После сборки всех платформ можно собрать набор файлов релиза без обращения к GitHub:

```bash
sh scripts/build.sh --all
sh scripts/package-release.sh dist
cd dist
sha256sum --check SHA256SUMS.txt
```

На Windows установщики можно собрать командой `./scripts/build.ps1 -All`; для `package-release.sh` подойдёт Git Bash. Linux-файлам, скачанным вручную, перед запуском нужно выдать право на выполнение: `chmod +x <имя-файла>`.
