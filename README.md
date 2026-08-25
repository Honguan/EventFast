# EventFast

<img src="Assets/EventFast-logo.png" alt="EventFast logo" width="160">

[繁體中文](README.zh-TW.md)

EventFast is a portable Windows Event Log query tool. It reads the native Windows Event Log API to search, group, inspect, and export System, Application, and offline `.evtx` events.

## Download and installation

Download `EventFast-v1.0.3-win-x64.exe` from the [EventFast v1.0.3 release](https://github.com/Honguan/EventFast/releases/tag/v1.0.3). It supports Windows 10 and 11 x64, is self-contained, and requires neither an installer nor a separate .NET runtime.

## Usage

Open the EXE, select a time range, severity, and channel, then search by Event ID, provider, problem name, or keyword. You can also drop an `.evtx` file onto the window.

```powershell
EventFast.exe --today
EventFast.exe --hours 24
EventFast.exe --event-id 51
EventFast.exe --provider disk
EventFast.exe --query "disk 153"
EventFast.exe C:\Logs\system.evtx
```

Select a problem to inspect each occurrence and complete message; use the dedicated **Parsed XML** tab for structured event XML. Use **Export Excel** to create problem-summary and complete-event worksheets.

## Language setting

English is the default. Use the **Language** selector in the main window to switch between English and Traditional Chinese. The selection is saved in `%LOCALAPPDATA%\EventFast\settings.json` and restored at the next launch.

## Development

```powershell
dotnet run
dotnet run -- --self-test
dotnet publish -p:PublishProfile=win-x64
./scripts/release.ps1 -OutputDirectory artifacts/release-candidate
dotnet run --project tests/EventFast.Tests -c Release -- --integration --ui
```

## Privacy

EventFast processes event data locally, collects no telemetry, and never uploads EVTX or export files. A network connection is made only when you explicitly choose **Search possible causes**, which opens a search query in your default browser.

## Troubleshooting / FAQ

- **A channel cannot be read:** restart as administrator when EventFast offers that action.
- **A provider message is unavailable:** the original event fields and XML remain available.
- **Too many results:** shorten the time range or add Event ID, provider, or keyword filters. A query is limited to 1,000,000 events.
- **The selected language was not restored:** check that `%LOCALAPPDATA%\EventFast` is writable. Invalid settings fall back to English.

## License

No open-source license has been specified. Redistribution or modification requires the copyright holder's permission.
