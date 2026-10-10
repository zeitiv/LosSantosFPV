# Los Santos FPV

A real-feeling FPV racing/freestyle quad for **GTA V story mode**. It flies in acro mode like Liftoff: rates with
super expo, airmode, prop thrust that drops off with speed, ground effect and drag. Fly it with **your own RC radio**
over USB, any USB joystick or gamepad, or an **Xbox controller**, and shoot any GTA weapon from it.

**[Download the latest release](../../releases/latest)**

## Features
- Acro flight model at 125 Hz, a clean-room reimplementation of Liftoff's physics. A 5" freestyle quad by default;
  build, rates and camera are all configurable.
- **Any controller**: EdgeTX/OpenTX/TBS radios in USB-joystick mode (RadioMaster, Jumper, FrSky, TBS Tango/Mambo...),
  HID gamepads and joysticks, read as raw HID so every switch is usable. An in-game wizard maps any channel order.
  Xbox controllers work with no setup (mode 2).
- **Shooting**: bind any switch or button and fire through the crosshair with any weapon. Optionally no police
  stars while flying.
- Rams knock people over. Auto-lands if your character gets hurt.
- On-screen OSD: arm state, speed, throttle and hover point, altitude, RPM.
- One `LosSantosFpv.json` for everything. It's re-read every time you launch the drone, and mistakes are reported with
  the line number.
- Optional import of rates and camera angle from your saved Liftoff drones.

## Install
1. Install [Script Hook V](https://www.dev-c.com/gtav/scripthookv/) (newest version for your game) and
   [ScriptHookVDotNet Enhanced](https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases).
2. Copy the `scripts` folder from the release zip into your GTA V folder (next to `GTA5_Enhanced.exe` / `GTA5.exe`).
3. Load story mode and press **Delete**.

| Key | Action |
|---|---|
| Delete | launch / land the drone |
| Shift + Delete | controller calibration wizard |
| Ctrl + Delete | bind the shoot switch/button (press, then flip it) |
| Backspace | reset the drone |

Pull the throttle all the way down once to arm. Every option is described in [`release/README.txt`](release/README.txt).

Story mode only. Don't use mods in GTA Online.

## Building
Requirements: Windows and the .NET 8 SDK. It builds a .NET Framework 4.8 DLL; no Visual Studio needed.

```powershell
dotnet run --project tests                 # headless flight-model and settings checks
.\build-release.ps1                        # dist\LosSantosFpv-1.0.0-dev.zip, using your game folder's ScriptHookVDotNet3.dll
.\build-release.ps1 -ShvdnDir C:\shvdn     # ...or any folder that contains ScriptHookVDotNet3.dll
dotnet build src -c Release                # plain build; copies LosSantosFpv.dll into <GTA V>\scripts if it exists
```

If GTA isn't in `C:\Program Files\Rockstar Games\Grand Theft Auto V Enhanced`, add `-p:GtaDir="D:\path\to\GTA V"`.

| Path | What's there |
|---|---|
| `src/DroneMod.cs` | the script: camera, drone prop, keys, OSD, shooting, world collision |
| `src/Flight.cs` | engine-neutral flight model |
| `src/Tune.cs` | motor/prop math and Liftoff import |
| `src/Radio.cs`, `src/HidJoystick.cs` | controller input and the calibration wizard |
| `src/Settings.cs`, `src/MiniJson.cs` | `LosSantosFpv.json` |

## Releases
[`.github/workflows/release.yml`](.github/workflows/release.yml) releases automatically. Every push to `main` that
changes `src/`, `release/` or the build script:
1. runs the tests;
2. bumps the patch version from the latest `vX.Y.Z` tag;
3. builds the zip and publishes it as a GitHub Release with generated notes.

To bump the minor or major version, run **Actions → Release → Run workflow**. To push without releasing, put
`[skip release]` in the commit message. ScriptHookVDotNet is downloaded during the build as a compile reference only
and is never committed or shipped.

## Credits
Made by zeitiv, with AI assistance. Uses Script Hook V (Alexander Blade) and ScriptHookVDotNet Enhanced
(Chiheb-Bacha and the SHVDN team). The flight model follows Liftoff's behaviour; no code or assets from Liftoff or
GTA V are included. [MIT license](LICENSE).
