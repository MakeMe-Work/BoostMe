# BoostMe

BoostMe is a lightweight Windows tray utility for controlling the volume of active applications independently.

## Features

- Runs quietly in the Windows notification area.
- Right-click the tray icon to see active audio applications.
- Choose a per-application volume level from 25%, 50%, 75%, or 100%.
- Detects and blocks unsafe above-100% settings instead of creating an audio feedback loop.
- Settings are persisted by executable name.
- Reapplies saved levels as applications start producing audio.
- Opens the settings file directly from the tray menu.

## Requirements

- Windows 10 or later
- .NET 10 SDK or runtime
- An active Windows playback device

## Build and run

```powershell
dotnet restore
dotnet build
dotnet run
```

To publish a self-contained Windows build:

```powershell
dotnet publish -c Release -r win-x64 --self-contained true
```

The published executable will be under `bin/Release/net10.0-windows/win-x64/publish/`.

## Settings

BoostMe stores its settings at:

```text
C:\Users\<your-user>\Program Files\BoostMe\settings.json
```

The file maps executable names to Windows per-application volume percentages. The directory is created automatically when the first setting is saved.

## Amplification above 100%

Windows' standard per-session volume API is limited to 100%. A previous loopback approach captured the default playback mix and replayed it through that same device, which creates recursive audio feedback and repeated sound. BoostMe now blocks persisted values above 100% rather than risking that behavior.

Real amplification above 100% requires routing audio through a separate virtual audio device, applying gain there, and selecting that device as the playback output. That routing layer is not included yet.

## Open source development

The project is intentionally small and uses the MIT license. Contributions should keep the tray workflow lightweight and avoid changing the global Windows master volume when adjusting an individual application.

# built with GPT-5.6 Luna

## License

MIT
