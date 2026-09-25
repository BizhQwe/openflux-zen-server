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
    settings_lang_title: "Язык интерфейса / Interface Language",
    settings_lang_desc: "Выберите язык для веб-интерфейса панели управления.",
    settings_admin_title: "Учётные данные администратора",
    setting_user_label: "Имя пользователя (Логин)",
    setting_cur_pass_label: "Текущий пароль (для подтверждения)",
    setting_cur_pass_placeholder: "Введите текущий пароль",
    setting_new_pass_label: "Новый пароль (оставьте пустым, если не меняется)",
    setting_new_pass_placeholder: "Новый пароль (опционально)",
    btn_save_changes: "Сохранить изменения",
    settings_secret_title: "Секретный путь панели",
    settings_secret_desc: "Случайный путь URL обеспечивает защиту от сканеров и ботов. Доступ возможен только по этому пути.",
    btn_regenerate_secret: "Сгенерировать новый",

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
    modal_tunnel_key_placeholder: "Необязательно (общий секрет)",
    modal_tunnel_clients: "Лимит клиентов (0 = без лимита)",
    modal_tunnel_traffic: "Лимит трафика в МБ (0 = без лимита)",
    modal_tunnel_extra: "Дополнительные параметры CLI",
    btn_cancel: "Отмена",
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
    confirm_regen_secret: "Сгенерировать новый секретный URL? Вам потребуется перейти по новому адресу."
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
    settings_lang_title: "Interface Language / Язык интерфейса",
    settings_lang_desc: "Choose the display language for the web management panel.",
    settings_admin_title: "Administrator Credentials",
    setting_user_label: "Username (Login)",
    setting_cur_pass_label: "Current Password (to verify)",
    setting_cur_pass_placeholder: "Enter current password",
    setting_new_pass_label: "New Password (leave empty to keep current)",
    setting_new_pass_placeholder: "New password (optional)",
    btn_save_changes: "Save Changes",
    settings_secret_title: "Secret Panel Path",
    settings_secret_desc: "A random URL path protects against automated scans and bots. Access is only permitted via this path.",
    btn_regenerate_secret: "Generate New Path",

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
    modal_tunnel_key_placeholder: "Optional (pre-shared key)",
    modal_tunnel_clients: "Client Limit (0 = unlimited)",
    modal_tunnel_traffic: "Traffic Limit in MB (0 = unlimited)",
    modal_tunnel_extra: "Extra CLI Arguments",
    btn_cancel: "Cancel",
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
    confirm_regen_secret: "Generate a new secret URL? You will need to reload using the new address."
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

  // Re-render tunnels if active
  if (typeof tunnelsData !== 'undefined' && Array.isArray(tunnelsData) && tunnelsData.length > 0) {
    if (typeof renderTunnels === 'function') {
      renderTunnels(tunnelsData);
    }
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
