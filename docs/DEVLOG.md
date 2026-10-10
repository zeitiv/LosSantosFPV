# GTA V Enhanced FPV drone: mod log

Goal: fly a Liftoff-style acro quad in GTA V Enhanced story mode with the TBS Tango 2 (or an Xbox pad), using the
fpv-drone-port skill.

## Setup found (2026-10-10)
- GTA V Enhanced 1.0.1013.34 in `C:\Program Files\Rockstar Games\Grand Theft Auto V Enhanced`.
- Already modded: ScriptHookV, ScriptHookVDotNet Enhanced 3.9.0.5, ASI loader (xinput1_4.dll), NativeTrainer, Rampage,
  LeFixSpeedo, DirectStorageFix. `args.txt` = `-nobattleye` (story mode only).

## Route
- An SHVDN v3 C# script (`src/`, net48), built with `dotnet build` (no Visual Studio needed).
- The script reads the radio in-process via winmm, so no helper and no file IPC (unlike the Witchfire port).
- `Flight.cs` is an engine-neutral port of the flight core: self-integrated at 125 Hz, GTA right-handed metres, K = 1.
- Collision uses a synchronous ray along the motion plus a capsule test. Ground effect uses one down ray.
- The drone is a frozen, collision-less prop (`ch_prop_casino_drone_02a`) whose pose we set every frame with
  SET_ENTITY_QUATERNION. A script camera is HARD_ATTACHed to it so it inherits roll.
- Streaming follows the drone via SET_FOCUS_POS_AND_VEL, with CLEAR_FOCUS on exit.

## Keys
- F5-F8 are GTA's character switch keys and Rampage uses F5, so: **Delete = toggle**, **End = calibration wizard**,
  **Backspace = reset**.

## Bench (bench/, headless, Mr.Steele tune)
| Check | Result |
|---|---|
| Hover throttle | 40.6 % |
| Vertical top speed | 22.6 m/s |
| Roll rate after 24 ms | 1165 deg/s |
| Roll rate, steady | 1194 deg/s |
| Speed at 60° tilt | 24.9 m/s |

All stick directions check out. These match the skill's reference table.

## 2026-10-10: ScriptHookV update, keys, shooting
- The game was patched to 1.0.1158.16. Updated to ScriptHookV v3889.0/1158.13; the log says
  `Success, game version is VER_EN_1_0_1158_13`.
- Updated ScriptHookVDotNet Enhanced to 1.1.0.6.
- Old files are backed up in `backup/scripthook-1013.34`.
- GTA is launched through the Rockstar Launcher (`PlayGTAV.exe`), not Steam.
- The keys are configurable in `scripts\LosSantosFpv.ini` and accept modifiers. Defaults: Delete = toggle,
  Shift+Delete = wizard (the K2 keyboard has no End key), Backspace = reset.
- Radio is now read as raw HID (`HidJoystick.cs`). winmm only exposes 6 axes, and the Tango 2 ("TBS Joystick") has
  8: x = throttle, y, z, rx = sticks; slider, slider1, rz, ry = switches.
- Shooting: wizard step 7 binds a switch (axis threshold) or button, stored as `FireBind`. The Xbox pad uses RT.
  - Fires SHOOT_SINGLE_BULLET_BETWEEN_COORDS along the camera axis.
  - Settings: FireWeapon (default WEAPON_CARBINERIFLE), FireRate (10/s), FireDamage (34).

## 2026-10-10: v1.0.0 release build
- Settings moved to `scripts\LosSantosFpv.json`, read with our own `MiniJson` (net48 has no comment-tolerant JSON).
  - Re-read on every launch.
  - A broken file keeps the last good values and shows the line and column.
  - The old .ini migrates automatically.
- The controller code is generic: raw HID for any joystick-mode radio or HID gamepad, plus XInput fallback. The 7-step
  wizard finds any channel order and inversion.
- Shoot binding is one key press (Ctrl+Delete), then flip the switch; it saves by itself.
- No wanted level while flying, plus a 5 s grace after landing: SET_MAX_WANTED_LEVEL 0 and clear each frame. Earlier
  "pilot hit" landings came from police shooting the pilot after drone fire.
- `Script.Settings` shadows our Settings class inside DroneMod, so it's qualified as LosSantosFpv.Settings.
- `build-release.ps1` writes `dist\LosSantosFpv-1.0.0.zip` (scripts\LosSantosFpv.dll, README.txt, LICENSE.txt [MIT]).
