# Headset AutoSwitch

## English

A small Windows 10/11 tray utility that automatically switches audio to your wireless headset when it is powered on and responds through its USB receiver, then switches back to your selected speakers when the connection is lost.

**Tested with only one headset so far: Razer BlackShark V2 Pro (2023), using its 2.4 GHz USB receiver (`1532:0555`).** Other models, 2020/Xbox/PlayStation variants and Bluetooth are not supported yet.

- Tray operation, selectable headphones and speakers, and an automation pause.
- English and Russian UI, selected automatically from the Windows display language.
- “Start with Windows” checkbox in the main window; login launches directly to the tray.
- No network requests or telemetry. The microphone is not switched.

For the first run, turn on the headset, select both audio outputs and enable automatic switching. Closing the window keeps the app in the tray. Radio link loss is treated like powering off; applications pinned to a specific output may not follow the system default.

## Build 

Windows + .NET SDK 10. No external NuGet packages / внешние NuGet-пакеты не нужны.

```powershell
.\build.ps1 -RunTests
.\dist\HeadsetAutoSwitch.exe
```

The published app needs .NET Desktop Runtime 10. Keep the entire `dist` folder together.
Для запуска нужен .NET Desktop Runtime 10; переносите всю папку `dist`, а не отдельный EXE.

Settings and logs stay in `data/` next to the EXE. Build output, local settings and diagnostics are excluded from this repository.
Настройки и логи хранятся в `data/` рядом с EXE. Сборки, локальные настройки и диагностика в репозиторий не включены.

## License

[MIT](LICENSE). PA packet layout and the request sequence were adapted from [HaloBattery](https://github.com/HeyOkay/HaloBattery/blob/main/providers/blackshark.py) (MIT).
 Attribution and its license: [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
