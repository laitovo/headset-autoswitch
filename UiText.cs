using System.Globalization;

namespace HeadphoneAutoSwitch;

public static class UiText
{
    // CurrentUICulture is the user's Windows display language, not the regional number/date format.
    // Other display languages fall back to English until a translation is available.
    public static string LanguageFor(CultureInfo culture) =>
        culture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase) ? "ru" : "en";

    public static string Choose(string english, string russian) =>
        LanguageFor(CultureInfo.CurrentUICulture) == "ru" ? russian : english;

    public static string Settings => Choose("Settings", "Настройки");
    public static string Exit => Choose("Exit", "Выход");
    public static string StartWithWindows => Choose("Start with Windows", "Запускать с Windows");
    public static string StartupReadError => Choose("Could not read the Windows startup setting", "Не удалось прочитать настройку автозапуска");
    public static string StartupWriteError => Choose("Could not change the Windows startup setting", "Не удалось изменить настройку автозапуска");
    public static string StartupEnabled => Choose("Windows startup enabled. The app will start in the tray.", "Автозапуск включён. Приложение будет запускаться в трее.");
    public static string StartupDisabled => Choose("Windows startup disabled.", "Автозапуск выключен.");
    public static string StartupExeMissing => Choose("The application EXE was not found. Run the app from its complete published folder.", "EXE приложения не найден. Запустите приложение из полной папки сборки.");
    public static string StartupKeyUnavailable => Choose("The Windows startup registry key is unavailable.", "Раздел реестра для автозапуска Windows недоступен.");
    public static string Pause => Choose("Pause automatic switching", "Приостановить автоматику");
    public static string Resume => Choose("Enable automatic switching", "Включить автоматику");
    public static string AutoSwitch => Choose("Automatically switch audio", "Автоматически переключать звук");
    public static string Communications => Choose("Also switch the output for calls", "Также переключать выход для звонков");
    public static string Waiting => Choose("Waiting for the first poll…", "Ожидание первого опроса…");
    public static string Heading => Choose("Audio follows your headphones", "Звук следует за наушниками");
    public static string Device => Choose("Razer BlackShark V2 Pro 2023 · 2.4 GHz USB receiver", "Razer BlackShark V2 Pro 2023 · USB-передатчик 2,4 ГГц");
    public static string WhenOnline => Choose("When the headset is connected", "Когда гарнитура на связи");
    public static string WhenOffline => Choose("When the headset connection is lost", "Когда связь с гарнитурой потеряна");
    public static string Save => Choose("Save and apply", "Сохранить и применить");
    public static string Refresh => Choose("Refresh devices", "Обновить устройства");
    public static string Help => Choose("First run: turn on the headset to verify its connection.\nClosing this window keeps the app in the tray. Choose Exit to quit.", "Первый запуск: включите гарнитуру, чтобы проверить канал связи.\nЗакрытие окна оставляет утилиту в трее. Для завершения выберите «Выход».");
    public static string LoadError => Choose("Could not read settings. Automatic switching is disabled.", "Не удалось прочитать настройки. Автопереключение отключено.");
    public static string GetDevicesError => Choose("Could not list audio devices", "Не удалось получить аудиоустройства");
    public static string MissingDevice => Choose("Saved device is unavailable", "Сохранённое устройство недоступно");
    public static string ChooseDevices => Choose("Choose two different devices: headphones and speakers.", "Выберите два разных устройства: наушники и колонки.");
    public static string SavedEnabled => Choose("Settings saved. Waiting for a confirmed connection state.", "Настройки сохранены. Ждём подтверждения состояния.");
    public static string SavedDisabled => Choose("Settings saved. Automatic switching is off.", "Настройки сохранены. Автоматика выключена.");
    public static string SaveError => Choose("Could not save settings", "Не удалось сохранить настройки");
    public static string Online => Choose("Headset connected", "Наушники на связи");
    public static string Offline => Choose("Headset connection lost", "Нет связи с наушниками");
    public static string Unknown => Choose("Connection state unknown", "Состояние неизвестно");
    public static string SwitchedHeadphones => Choose("Audio switched to headphones.", "Звук переключён на наушники.");
    public static string SwitchedSpeakers => Choose("Audio switched to speakers.", "Звук переключён на колонки.");
    public static string SwitchError => Choose("Could not switch audio: ", "Переключение не выполнено: ");
    public static string PollError => Choose("Polling error: ", "Ошибка опроса: ");
    public static string NotSelected => Choose("none", "не выбран");
    public static string CurrentOutput => Choose("Windows output: ", "Выход Windows: ");
    public static string Enabled => Choose("Automatic switching enabled.", "Автоматика включена.");
    public static string Paused => Choose("Automatic switching paused.", "Автоматика приостановлена.");
    public static string AlreadyRunning => Choose("The app is already running. Open settings from its tray icon.", "Утилита уже работает. Откройте настройки через её значок в трее.");
    public static string ErrorTitle => Choose("Headset AutoSwitch — error", "Headset AutoSwitch — ошибка");
    public static string DiagnoseNote => Choose("Diagnostics do not switch audio. Turn on the headset to verify the HID connection for the first time.", "Диагностика не переключает звук. Для первого подтверждения HID включите гарнитуру.");
    public static string DiagnoseSaveError => Choose("Could not save diagnostics: ", "Не удалось сохранить диагностику: ");
    public static string HidListError => Choose("Could not list HID devices: ", "Не удалось перечислить HID: ");
    public static string ReceiverMissing => Choose("The BlackShark V2 Pro 2023 USB receiver is not connected", "USB-передатчик BlackShark V2 Pro 2023 не подключён");
    public static string ReceiverUnknown => Choose("Receiver found, connection state unconfirmed. ", "Передатчик найден, состояние не подтверждено. ");
    public static string HidCollectionMissing => Choose("A suitable HID collection is unavailable.", "Подходящая HID-коллекция недоступна.");
    public static string Battery(int battery) => Choose($"Headset responding · battery {battery}%", $"Гарнитура отвечает · заряд {battery}%");
    public static string NoReply => Choose("No headset reply on the previously verified channel", "Нет ответа гарнитуры на ранее проверенном канале");
    public static string FirstConnection => Choose("No reply; turn on the headset to verify the channel for the first time", "Нет ответа; включите гарнитуру для первой проверки канала");
    public static string HidCleanup => Choose("Finishing HID request: ", "Завершение запроса HID: ");
    public static string HidWriteTimeout => Choose("HID write timed out; connection state unknown", "Истекло время записи HID; состояние неизвестно");
    public static string HidClosed => Choose("The device closed the HID channel", "HID-канал закрыт устройством");
    public static string HidListChanging => Choose("The HID device list is changing; polling will retry", "Список HID меняется; опрос будет повторён");
    public static string OutputUnavailable => Choose("The selected audio output is unavailable. Refresh the device list.", "Выбранный аудиовыход сейчас недоступен. Обновите список устройств.");
    public static string WindowsSwitchError => Choose("Windows could not switch the audio output: ", "Windows не удалось переключить аудиовыход: ");
    public static string RestoreRoleError(int role, int hr) => Choose($"Could not restore role {role}: 0x{hr:X8}", $"Не удалось восстановить роль {role}: 0x{hr:X8}");
}
