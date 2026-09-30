// OpenFlux Zen Server - Internationalization (RU / EN)

const I18N_DICTIONARY = {
  ru: {
    app_title: "OpenFlux Zen Server",
    nav_tunnels: "Туннели",
    nav_logs: "Логи",
    nav_config: "Конфигурация",
    nav_settings: "Настройки",
    nav_logout: "Выход",

    login_sub: "Войдите в панель управления туннелями",
    login_user_label: "Имя пользователя",
    login_user_placeholder: "Имя пользователя",
    login_pass_label: "Пароль",
    login_btn: "Войти в панель",

    stat_active_tunnels: "Активные туннели",
    stat_total_created: "Всего создано:",
    stat_host_resources: "Ресурсы хоста",
    stat_network_speed: "Скорость сети",
    stat_total_traffic: "Общий трафик",

    tunnels_title: "Список туннелей",
    btn_refresh: "Обновить",
    btn_add_tunnel: "Добавить туннель",
    no_tunnels: "Туннели ещё не созданы",
    no_tunnels_sub: "Нажмите «Добавить туннель» для создания первого туннеля",

    badge_running: "Активен",
    badge_stopped: "Остановлен",
    badge_starting: "Запуск...",
    badge_failed: "Ошибка",
    badge_conn_error: "Ошибка связи",

    detail_transport: "Транспорт",
    detail_mode: "Режим",
    detail_codec: "Кодек",
    detail_clients: "Клиенты",
    detail_traffic: "Трафик",
    detail_upload: "Отдача",
    detail_download: "Загрузка",

    btn_start: "Запустить",
    btn_stop: "Остановить",
    btn_reset: "Сброс",
    btn_logs: "Логи",
    btn_edit: "Изменить",
    btn_delete: "Удалить",

    logs_title: "Системные логи",
    logs_panel_system: "Лог панели (System)",
    btn_download_logs: "Скачать логи",
    btn_clear_logs: "Очистить",
    logs_loading: "Загрузка логов...",

    config_title: "Резервное копирование и перенос",
    config_export_title: "Экспорт конфигурации (JSON)",
    config_export_desc: "Выгрузите все настройки и конфигурации ваших туннелей для резервной копии или переноса на другой сервер.",
    btn_download_config: "Скачать файл конфигурации",
    btn_copy_json: "Скопировать JSON",
    config_import_title: "Импорт конфигурации (JSON)",
    config_import_desc: "Вставьте экспортированный JSON или выберите файл конфигурации с вашего компьютера. Существующие туннели будут обновлены, новые — добавлены.",
    btn_select_file: "Выбрать файл на ПК (.json)",
    import_placeholder: "Вставьте JSON конфигурацию или выберите файл выше...",
    btn_apply_config: "Применить конфигурацию",

    settings_title: "Настройки панели",
    settings_lang_title: "Язык интерфейса",
    settings_lang_desc: "Выберите язык для веб-интерфейса панели управления.",
    settings_admin_title: "Управление учётными записями",
    setting_user_label: "Имя пользователя (Логин)",
    setting_cur_pass_label: "Текущий пароль (для подтверждения)",
    setting_cur_pass_placeholder: "Введите текущий пароль",
    setting_new_pass_label: "Новый пароль (оставьте пустым, если не меняется)",
    setting_new_pass_placeholder: "Новый пароль (опционально)",
    btn_save_changes: "Сохранить изменения",
    settings_secret_title: "Секретный путь панели",
    settings_secret_desc: "Случайный путь URL обеспечивает защиту от сканеров и ботов. Доступ возможен только по этому пути.",
    btn_regenerate_secret: "Сгенерировать новый",
    settings_network_title: "Сетевое размещение",
    settings_network_desc: "Текущий режим сетевого размещения панели и сервера.",
    settings_network_access_title: "Доступ к панели",
    settings_network_access_desc: "Ссылка для входа в панель с этого компьютера и других устройств.",
    settings_network_url_label: "Адрес панели (URL)",
    settings_secret_path_label: "Секретный путь URL",
    btn_copy: "Копировать",
    btn_regen_secret: "Обновить ссылку",
    settings_secret_note: "В ссылке есть секретный путь для защиты от сканеров и ботов. После обновления прежняя ссылка перестанет работать.",
    settings_change_network_mode_title: "Управление сетевым размещением",
    settings_change_network_mode_desc: "Выберите, откуда разрешён доступ к веб-панели сервера.",
    settings_select_mode_label: "Тип размещения",
    settings_domain_label: "Домен (опционально)",
    btn_apply_network_mode: "Применить",
    confirm_localhost_mode: "Внимание: при выборе 'Только на этом ПК' доступ к панели будет строго ограничен адресом 127.0.0.1. Доступ по локальной сети и интернету будет заблокирован. Продолжить?",
    toast_network_mode_updated: "Сетевое размещение успешно обновлено!",
    toast_network_mode_failed: "Не удалось обновить сетевое размещение",
    mode_domain: "Прямое подключение (домен / публичный IP)",
    mode_localtunnel: "Через Localtunnel (без публичного IP)",
    mode_local: "Через локальную сеть (LAN / Wi-Fi)",
    mode_localhost: "Только на этом ПК (localhost)",
    settings_decoy_title: "Сайт-заглушка (Маскировка)",
    settings_decoy_desc: "Все запросы к серверу на любые адреса, кроме секретного пути, перенаправляются на сайт-заглушку (как в 3X-UI Pro), скрывая сервер от сканеров.",
    settings_decoy_custom_hint: "Вы можете загрузить собственный HTML-сайт в папку data/decoy/ (index.html) или указать внешний URL ниже.",
    settings_decoy_url_label: "URL внешнего перенаправления (опционально)",
    settings_decoy_url_placeholder: "https://example.com (оставьте пустым для встроенной заглушки)",
    btn_save_decoy: "Сохранить настройки заглушки",
    btn_open_decoy: "Открыть сайт-заглушку",

    modal_new_tunnel: "Новый туннель",
    modal_edit_tunnel: "Редактирование туннеля",
    modal_tunnel_name: "Название туннеля",
    modal_tunnel_transport: "Транспорт (Transport)",
    modal_tunnel_mode: "Режим выхода (Mode)",
    modal_tunnel_url: "URL документа (--url)",
    modal_tunnel_codec: "Кодек (Codec)",
    modal_tunnel_egress: "Egress IP (--local-ip, для L3)",
    modal_tunnel_egress_placeholder: "Автоопределение",
    modal_tunnel_key: "Ключ шифрования (AES-256-GCM)",
    modal_tunnel_key_placeholder: "32-байтный hex-ключ (мин. 16 символов)",
    modal_tunnel_key_hint: "Обязателен для клиентов OpenFlux v0.2.0+. Если оставить пустым, ключ генерируется автоматически.",
    btn_generate_key: "Сгенерировать",
    modal_tunnel_direct_listen: "Порт прослушивания (--direct-listen)",
    modal_tunnel_share_host: "Внешний хост / IP (--share-host)",
    modal_tunnel_transports: "Список транспортов с приоритетом (--transports)",
    modal_tunnel_transports_hint: "Пример: direct:100,yandex:50 или boards:90,yandex:50",
    modal_tunnel_enable_share: "Генерировать клиентскую ссылку openflux:// и QR-код (--share)",
    modal_tunnel_advanced_title: "Расширенные параметры OpenFlux (Advanced)",
    modal_tunnel_context: "KDF Контекст (--session-context)",
    modal_tunnel_max_packet: "Макс. размер пакета (--max-packet-size)",
    modal_tunnel_negotiate: "Строгое согласование (--negotiate)",
    modal_qr_title: "Подключение клиента OpenFlux",
    modal_qr_subtitle: "Отсканируйте QR-код в приложении OpenFlux или скопируйте ссылку для быстрого импорта конфигурации:",
    modal_qr_link_label: "Клиентская ссылка OpenFlux:",
    btn_copy_openflux: "Скопировать openflux://",
    btn_show_qr: "QR-код",
    btn_connect_qr: "Подключение",
    toast_link_copied: "Ссылка openflux:// скопирована в буфер обмена",
    badge_share_ready: "Ссылка готова",
    modal_tunnel_clients: "Лимит клиентов (0 = без лимита)",
    modal_tunnel_traffic: "Лимит трафика в МБ (0 = без лимита)",
    modal_tunnel_extra: "Дополнительные параметры CLI",
    btn_cancel: "Отмена",
    btn_close: "Закрыть",
    btn_save: "Сохранить",

    toast_copied: "Скопировано в буфер обмена",
    toast_saved: "Успешно сохранено",
    toast_imported: "Конфигурация успешно импортирована",
    toast_deleted: "Туннель удалён",
    toast_reset: "Счётчики трафика сброшены",
    toast_error: "Произошла ошибка",
    toast_fill_login: "Введите имя пользователя и пароль",
    toast_login_success: "Успешный вход в панель",
    toast_invalid_credentials: "Неверное имя пользователя или пароль",
    toast_server_error: "Ошибка соединения с сервером",
    toast_logs_cleared: "Лог туннеля очищен",
    toast_logs_fetch_fail: "Не удалось получить логи для скачивания",
    toast_logs_empty: "Логи пусты, нечего скачивать",
    toast_logs_download_success: "Логи успешно сохранены в файл",
    toast_logs_download_error: "Ошибка при сохранении логов",
    logs_empty: "Нет записей в журнале логов.",
    confirm_delete: "Вы уверены, что хотите удалить туннель \"{name}\"?",
    confirm_reset: "Сбросить счётчики трафика для туннеля \"{name}\"?",
    confirm_clear_logs: "Очистить окно логов?",
    settings_core_title: "OpenFlux",
    settings_core_desc: "Официальное ядро сетевого туннелирования OpenFlux (upstream p1neappleXpress/OpenFlux). Отвечает за создание зашифрованных сетевых соединений, туннелей и передачу трафика.",
    settings_core_version: "Установленная версия:",
    settings_core_latest_version: "Доступная версия:",
    settings_core_published: "Дата релиза:",
    settings_core_checked: "Проверено:",
    settings_core_release_notes: "Что нового в обновлении:",
    settings_core_github_link: "Релиз на GitHub",
    btn_check_core_updates: "Проверить обновления",
    btn_update_core: "Обновить",
    btn_rollback_core: "Сменить версию",
    btn_change_version_core: "Сменить версию",
    btn_view_releases: "Релизы GitHub",
    core_up_to_date: "Актуальная версия",
    core_update_available: "Доступно обновление",
    core_not_checked: "Не проверялось",
    toast_core_up_to_date: "OpenFlux обновлен до последней версии",
    toast_core_update_available: "Доступна новая версия OpenFlux",
    toast_core_check_failed: "Не удалось проверить обновления",
    toast_core_updating: "Скачивание и обновление OpenFlux...",
    toast_core_update_success: "OpenFlux успешно обновлен!",
    toast_core_update_failed: "Ошибка при обновлении OpenFlux",
    confirm_update_core: "Обновить OpenFlux до версии {version}? Все активные туннели будут перезапущены автоматически.",
    settings_panel_title: "OpenFlux Zen Server",
    settings_panel_desc: "Веб-панель управления и служба сервера OpenFlux Zen Server (BizhQwe/openflux-zen-server). Отвечает за интерфейс управления, учётные записи администратора, фоновую службу системы и настройку туннелей.",
    settings_panel_version: "Установленная версия:",
    settings_panel_latest_version: "Доступная версия:",
    settings_panel_published: "Дата релиза:",
    settings_panel_checked: "Проверено:",
    settings_panel_release_notes: "Что нового в обновлении:",
    settings_panel_github_link: "Релиз на GitHub",
    btn_check_panel_updates: "Проверить обновления",
    btn_update_panel: "Обновить",
    btn_rollback_panel: "Сменить версию",
    btn_change_version_panel: "Сменить версию",
    btn_view_panel_releases: "Релизы GitHub",
    panel_up_to_date: "Актуальная версия",
    panel_update_available: "Доступно обновление",
    panel_not_checked: "Не проверялось",
    toast_panel_up_to_date: "Установлена самая актуальная версия панели OpenFlux Zen Server",
    toast_panel_update_available: "Доступна новая версия OpenFlux Zen Server",
    toast_panel_check_failed: "Не удалось проверить обновления панели",
    toast_panel_updating: "Скачивание и установка обновления панели... Сервер перезагрузится через несколько секунд.",
    toast_panel_update_success: "Обновление запущено! Перезагрузка страницы...",
    toast_panel_update_failed: "Ошибка при обновлении панели OpenFlux Zen Server",
    toast_panel_restarted: "Сервер успешно обновлён и перезапущен!",
    confirm_update_panel: "Обновить OpenFlux Zen Server до версии {version}? Служба сервера будет автоматически перезапущена с сохранением всех настроек и данных.",
    confirm_regen_secret: "Сгенерировать новый секретный URL? Вам потребуется перейти по новому адресу.",

    modal_rollback_title: "Смена версии",
    modal_rollback_select_label: "Выберите версию для установки:",
    modal_rollback_warn_title: "Внимание:",
    modal_rollback_warn_text: "При смене версии будет загружен и применен выбранный релиз.",
    btn_apply_version: "Установить выбранную версию",
    toast_fetching_releases: "Загрузка списка релизов с GitHub...",
    toast_releases_failed: "Не удалось загрузить список релизов с GitHub",
    confirm_rollback_core: "Установить ядро OpenFlux версии {version}? Все активные туннели будут перезапущены.",
    confirm_rollback_panel: "Установить панель OpenFlux Zen Server версии {version}? Служба сервера будет перезапущена с сохранением настроек.",
    header_status_up_to_date: "Обновлено",
    header_status_update_available: "Требуется обновление"
  },

  en: {
    app_title: "OpenFlux Zen Server",
    nav_tunnels: "Tunnels",
    nav_logs: "Logs",
    nav_config: "Configuration",
    nav_settings: "Settings",
    nav_logout: "Sign Out",

    login_sub: "Sign in to the tunnel management panel",
    login_user_label: "Username",
    login_user_placeholder: "Username",
    login_pass_label: "Password",
    login_btn: "Sign In",

    stat_active_tunnels: "Active Tunnels",
    stat_total_created: "Total created:",
    stat_host_resources: "Host Resources",
    stat_network_speed: "Network Speed",
    stat_total_traffic: "Total Traffic",

    tunnels_title: "Tunnel List",
    btn_refresh: "Refresh",
    btn_add_tunnel: "Add Tunnel",
    no_tunnels: "No tunnels created yet",
    no_tunnels_sub: "Click \"Add Tunnel\" to configure your first tunnel",

    badge_running: "Running",
    badge_stopped: "Stopped",
    badge_starting: "Starting...",
    badge_failed: "Failed",
    badge_conn_error: "Connection Error",

    detail_transport: "Transport",
    detail_mode: "Mode",
    detail_codec: "Codec",
    detail_clients: "Clients",
    detail_traffic: "Traffic",
    detail_upload: "Upload",
    detail_download: "Download",

    btn_start: "Start",
    btn_stop: "Stop",
    btn_reset: "Reset",
    btn_logs: "Logs",
    btn_edit: "Edit",
    btn_delete: "Delete",

    logs_title: "System Logs",
    logs_panel_system: "Panel Log (System)",
    btn_download_logs: "Download Logs",
    btn_clear_logs: "Clear",
    logs_loading: "Loading logs...",

    config_title: "Backup & Migration",
    config_export_title: "Export Configuration (JSON)",
    config_export_desc: "Export all tunnel settings and configurations for backup or server migration.",
    btn_download_config: "Download Config File",
    btn_copy_json: "Copy JSON",
    config_import_title: "Import Configuration (JSON)",
    config_import_desc: "Paste exported JSON or choose a configuration file from your computer. Existing tunnels will be updated, new ones added.",
    btn_select_file: "Choose file on PC (.json)",
    import_placeholder: "Paste JSON configuration or choose a file above...",
    btn_apply_config: "Apply Configuration",

    settings_title: "Panel Settings",
    settings_lang_title: "Interface Language",
    settings_lang_desc: "Choose the display language for the web management panel.",
    settings_admin_title: "Account Management",
    setting_user_label: "Username (Login)",
    setting_cur_pass_label: "Current Password (to verify)",
    setting_cur_pass_placeholder: "Enter current password",
    setting_new_pass_label: "New Password (leave empty to keep current)",
    setting_new_pass_placeholder: "New password (optional)",
    btn_save_changes: "Save Changes",
    settings_secret_title: "Secret Panel Path",
    settings_secret_desc: "A random URL path protects against automated scans and bots. Access is only permitted via this path.",
    btn_regenerate_secret: "Generate New Path",
    settings_network_title: "Network Placement",
    settings_network_desc: "Current server and panel network accessibility mode.",
    settings_network_access_title: "Panel Access",
    settings_network_access_desc: "Sign-in link for this computer and other devices.",
    settings_network_url_label: "Panel Address (URL)",
    settings_secret_path_label: "Secret URL Path",
    btn_copy: "Copy",
    btn_regen_secret: "Refresh link",
    settings_secret_note: "The link contains a secret path that protects the panel from scanners and bots. The previous link stops working after refresh.",
    settings_change_network_mode_title: "Manage Network Placement",
    settings_change_network_mode_desc: "Choose where the server web panel is accessible from.",
    settings_select_mode_label: "Placement Type",
    settings_domain_label: "Domain (optional)",
    btn_apply_network_mode: "Apply",
    confirm_localhost_mode: "Warning: selecting 'Only on this PC' restricts panel access strictly to 127.0.0.1. LAN and Internet access will be blocked. Continue?",
    toast_network_mode_updated: "Network placement updated successfully!",
    toast_network_mode_failed: "Failed to update network placement",
    mode_domain: "Direct connection (domain / public IP)",
    mode_localtunnel: "Via Localtunnel (no public IP required)",
    mode_local: "Local network (LAN / Wi-Fi)",
    mode_localhost: "Only on this PC (localhost)",
    settings_decoy_title: "Decoy Site (Stealth Masking)",
    settings_decoy_desc: "All incoming requests outside the secret path land on the decoy site (like in 3X-UI Pro), masking your server from scanners.",
    settings_decoy_custom_hint: "You can place custom site files into data/decoy/ (index.html) or specify an external URL below.",
    settings_decoy_url_label: "External Redirect URL (optional)",
    settings_decoy_url_placeholder: "https://example.com (leave empty for built-in decoy site)",
    btn_save_decoy: "Save Decoy Settings",
    btn_open_decoy: "Open Decoy Site",

    modal_new_tunnel: "New Tunnel",
    modal_edit_tunnel: "Edit Tunnel",
    modal_tunnel_name: "Tunnel Name",
    modal_tunnel_transport: "Transport",
    modal_tunnel_mode: "Egress Mode",
    modal_tunnel_url: "Document URL (--url)",
    modal_tunnel_codec: "Codec",
    modal_tunnel_egress: "Egress IP (--local-ip, for L3)",
    modal_tunnel_egress_placeholder: "Auto-detect",
    modal_tunnel_key: "Encryption Key (AES-256-GCM)",
    modal_tunnel_key_placeholder: "32-byte hex key (min. 16 chars)",
    modal_tunnel_key_hint: "Required for OpenFlux v0.2.0+ clients. If left empty, a secure key is auto-generated.",
    btn_generate_key: "Generate",
    modal_tunnel_direct_listen: "Listen Port (--direct-listen)",
    modal_tunnel_share_host: "Public Host / IP (--share-host)",
    modal_tunnel_transports: "Transports with Priority (--transports)",
    modal_tunnel_transports_hint: "Example: direct:100,yandex:50 or boards:90,yandex:50",
    modal_tunnel_enable_share: "Generate client openflux:// share link and QR code (--share)",
    modal_tunnel_advanced_title: "OpenFlux Advanced Parameters",
    modal_tunnel_context: "KDF Context (--session-context)",
    modal_tunnel_max_packet: "Max Packet Size (--max-packet-size)",
    modal_tunnel_negotiate: "Strict Negotiation (--negotiate)",
    modal_qr_title: "Connect OpenFlux Client",
    modal_qr_subtitle: "Scan this QR code in the OpenFlux app or copy the link to import the configuration:",
    modal_qr_link_label: "OpenFlux client link:",
    btn_copy_openflux: "Copy openflux://",
    btn_show_qr: "QR Code",
    btn_connect_qr: "Connect",
    toast_link_copied: "openflux:// link copied to clipboard",
    badge_share_ready: "Link Ready",
    modal_tunnel_clients: "Client Limit (0 = unlimited)",
    modal_tunnel_traffic: "Traffic Limit in MB (0 = unlimited)",
    modal_tunnel_extra: "Extra CLI Arguments",
    btn_cancel: "Cancel",
    btn_close: "Close",
    btn_save: "Save",
 
    toast_copied: "Copied to clipboard",
    toast_saved: "Successfully saved",
    toast_imported: "Configuration imported successfully",
    toast_deleted: "Tunnel deleted",
    toast_reset: "Traffic counters reset",
    toast_error: "An error occurred",
    toast_fill_login: "Please enter username and password",
    toast_login_success: "Signed in successfully",
    toast_invalid_credentials: "Invalid username or password",
    toast_server_error: "Server connection error",
    toast_logs_cleared: "Tunnel log cleared",
    toast_logs_fetch_fail: "Failed to fetch logs for download",
    toast_logs_empty: "Logs are empty, nothing to download",
    toast_logs_download_success: "Logs saved to file successfully",
    toast_logs_download_error: "Error saving logs",
    logs_empty: "No log entries found.",
    confirm_delete: "Are you sure you want to delete tunnel \"{name}\"?",
    confirm_reset: "Reset traffic counters for tunnel \"{name}\"?",
    confirm_clear_logs: "Clear the log output window?",
    settings_core_title: "OpenFlux",
    settings_core_desc: "Official OpenFlux network tunneling core engine (upstream p1neappleXpress/OpenFlux). Responsible for tunnel creation, network transports, traffic encryption, and data transmission.",
    settings_core_version: "Installed version:",
    settings_core_latest_version: "Available version:",
    settings_core_published: "Release date:",
    settings_core_checked: "Checked:",
    settings_core_release_notes: "What's new in this release:",
    settings_core_github_link: "GitHub Release",
    btn_check_core_updates: "Check for Updates",
    btn_update_core: "Update",
    btn_rollback_core: "Change Version",
    btn_change_version_core: "Change Version",
    btn_view_releases: "GitHub Releases",
    core_up_to_date: "Up to date",
    core_update_available: "Update Available",
    core_not_checked: "Not checked",
    toast_core_up_to_date: "OpenFlux is already up to date",
    toast_core_update_available: "A new version of OpenFlux is available",
    toast_core_check_failed: "Failed to check for updates",
    toast_core_updating: "Downloading and updating OpenFlux...",
    toast_core_update_success: "OpenFlux updated successfully!",
    toast_core_update_failed: "Failed to update OpenFlux",
    confirm_update_core: "Update OpenFlux to version {version}? Active tunnels will be restarted automatically.",
    settings_panel_title: "OpenFlux Zen Server",
    settings_panel_desc: "Web management panel and server service for OpenFlux Zen Server (BizhQwe/openflux-zen-server). Responsible for web UI, administrator accounts, system background service, and tunnel configuration.",
    settings_panel_version: "Installed version:",
    settings_panel_latest_version: "Available version:",
    settings_panel_published: "Release date:",
    settings_panel_checked: "Checked:",
    settings_panel_release_notes: "What's new in this release:",
    settings_panel_github_link: "GitHub Release",
    btn_check_panel_updates: "Check for Updates",
    btn_update_panel: "Update",
    btn_rollback_panel: "Change Version",
    btn_change_version_panel: "Change Version",
    btn_view_panel_releases: "GitHub Releases",
    panel_up_to_date: "Up to date",
    panel_update_available: "Update Available",
    panel_not_checked: "Not checked",
    toast_panel_up_to_date: "OpenFlux Zen Server panel is already up to date",
    toast_panel_update_available: "A new version of OpenFlux Zen Server is available",
    toast_panel_check_failed: "Failed to check for panel updates",
    toast_panel_updating: "Downloading and applying panel update... The server will restart in a few moments.",
    toast_panel_update_success: "Update initiated! Reloading the page...",
    toast_panel_update_failed: "Failed to update OpenFlux Zen Server panel",
    toast_panel_restarted: "Server successfully updated and restarted!",
    confirm_update_panel: "Update OpenFlux Zen Server to version {version}? The server service will be restarted automatically, preserving all configurations and data.",
    confirm_regen_secret: "Generate a new secret URL? You will need to reload using the new address.",

    modal_rollback_title: "Change Version",
    modal_rollback_select_label: "Select version to install:",
    modal_rollback_warn_title: "Warning:",
    modal_rollback_warn_text: "Switching versions will download and apply the selected release.",
    btn_apply_version: "Install Selected Version",
    toast_fetching_releases: "Loading release list from GitHub...",
    toast_releases_failed: "Failed to load release list from GitHub",
    confirm_rollback_core: "Install OpenFlux core version {version}? Active tunnels will be restarted.",
    confirm_rollback_panel: "Install OpenFlux Zen Server panel version {version}? The server service will restart.",
    header_status_up_to_date: "Updated",
    header_status_update_available: "Update required"
  }
};

let currentLanguage = localStorage.getItem('zen_lang') || 'ru';

function t(key, vars = {}) {
  const dict = I18N_DICTIONARY[currentLanguage] || I18N_DICTIONARY.ru;
  let text = dict[key] || I18N_DICTIONARY.ru[key] || key;
  for (const [k, v] of Object.entries(vars)) {
    text = text.replace(new RegExp(`\\{${k}\\}`, 'g'), v);
  }
  return text;
}

function setLanguage(lang, syncServer = true) {
  if (lang !== 'ru' && lang !== 'en') lang = 'ru';
  currentLanguage = lang;
  localStorage.setItem('zen_lang', lang);
  document.documentElement.lang = lang;

  // Update header quick toggle button text
  const labelEl = document.getElementById('current-lang-label');
  if (labelEl) labelEl.textContent = lang.toUpperCase();

  // Update Settings select if present
  const selectEl = document.getElementById('setting-language');
  if (selectEl && selectEl.value !== lang) {
    selectEl.value = lang;
  }

  // Update all elements with data-i18n
  document.querySelectorAll('[data-i18n]').forEach(el => {
    const key = el.getAttribute('data-i18n');
    if (key) {
      el.textContent = t(key);
    }
  });

  // Update all placeholders
  document.querySelectorAll('[data-i18n-placeholder]').forEach(el => {
    const key = el.getAttribute('data-i18n-placeholder');
    if (key) {
      el.placeholder = t(key);
    }
  });

  // Update all titles
  document.querySelectorAll('[data-i18n-title]').forEach(el => {
    const key = el.getAttribute('data-i18n-title');
    if (key) {
      el.title = t(key);
    }
  });

  if (typeof updateHeaderStatusBadge === 'function') {
    updateHeaderStatusBadge();
  }

  // Re-render tunnels if active
  if (typeof tunnelsData !== 'undefined' && Array.isArray(tunnelsData) && tunnelsData.length > 0) {
    if (typeof renderTunnels === 'function') {
      renderTunnels(tunnelsData);
    }
  }

  // Re-render panel version info if loaded
  if (typeof renderPanelVersionInfo === 'function' && typeof panelInfoCache !== 'undefined' && panelInfoCache) {
    renderPanelVersionInfo(panelInfoCache);
  }

  // Re-render core version info if loaded
  if (typeof renderCoreVersionInfo === 'function' && typeof coreInfoCache !== 'undefined' && coreInfoCache) {
    renderCoreVersionInfo(coreInfoCache);
  }

  // Sync to server settings if requested and authed
  if (syncServer && localStorage.getItem('zen_token')) {
    api('api/settings/language', {
      method: 'POST',
      body: JSON.stringify({ language: lang })
    }).catch(() => {});
  }
}

function toggleLanguage() {
  const nextLang = currentLanguage === 'ru' ? 'en' : 'ru';
  setLanguage(nextLang, true);
  if (typeof showToast === 'function') {
    showToast(nextLang === 'ru' ? 'Язык изменён на Русский' : 'Language switched to English', 'info');
  }
}
