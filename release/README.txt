LOS SANTOS FPV  v{{VERSION}}
==========================
Fly a real-feeling FPV racing/freestyle quad in GTA V story mode. It flies in acro mode like Liftoff:
rates with super expo, airmode, prop thrust that drops with speed, ground effect and drag.
Fly it with your own RC radio over USB, any USB joystick, or an Xbox controller.
It can also shoot a GTA weapon of your choice.

Made for GTA V Enhanced (story mode only). GTA V Legacy should also work with the same requirements.


REQUIREMENTS
------------
1. Script Hook V                    https://www.dev-c.com/gtav/scripthookv/
   Use the newest version. It has to match your game version.
2. ScriptHookVDotNet Enhanced       https://github.com/Chiheb-Bacha/ScriptHookVDotNetEnhanced/releases
   Works for both Enhanced and Legacy.
3. .NET Framework 4.8. Windows 10 and 11 already have it.


INSTALL
-------
1. Install the two requirements above (copy their files next to GTA5_Enhanced.exe / GTA5.exe).
2. Copy the "scripts" folder from this zip into the same game folder. If you already have a "scripts"
   folder, merge it. The result should be:
       <GTA V folder>\scripts\LosSantosFpv.dll
3. Start the game and load story mode. A "Los Santos FPV {{VERSION}}" message appears at the top of the screen.

LosSantosFpv.json (all settings) and LosSantosFpv.log are created in the scripts folder the first time the game runs.

To uninstall, delete scripts\LosSantosFpv.dll, LosSantosFpv.json and LosSantosFpv.log.


CONTROLS (KEYBOARD)
-------------------
  Delete           launch / land the drone. It starts just in front of you.
  Shift + Delete   calibrate your controller (step-by-step wizard on screen)
  Ctrl + Delete    set the shoot switch/button only (press, then flip the switch)
  Backspace        reset the drone to the launch spot
  Esc              cancel the wizard

All keys can be changed in LosSantosFpv.json, in the "keys" section.

The drone is DISARMED until the throttle has been all the way down once. Pull the throttle down to arm.
While you fly, your character stands still and can't be controlled. The drone lands automatically if
your character is hurt, dies or moves away.


CONTROLLERS
-----------
RC radio (EdgeTX / OpenTX / TBS: RadioMaster, Jumper, FrSky, TBS Tango/Mambo, BetaFPV...)
  1. Plug the radio in over USB and choose "USB Joystick (HID)" when the radio asks.
  2. In game, press Shift + Delete and follow the 7 steps:
       sticks around -> throttle down -> throttle up -> roll right -> pitch forward -> yaw right -> shoot switch
     Any channel order (AETR, TAER...) and any reversed channel works. The wizard finds them.
  3. Done. The mapping is saved in LosSantosFpv.json.

Other USB joysticks and HID gamepads (PlayStation, generic, sim controllers)
  Same as above: press Shift + Delete and follow the wizard.

Xbox controller (no setup needed), mode 2:
  Left stick up/down = throttle (centre = half throttle)   Left stick left/right = yaw
  Right stick = pitch and roll                              RT = shoot   Y = reset

If you have several devices, put part of your controller's name in "controller.device" (for example "Tango"
or "RadioMaster"). The name is shown on the wizard screen.


SHOOTING
--------
While your shoot switch/button is on, the drone fires straight through the crosshair. The crosshair turns
red while firing.
  Ctrl + Delete, then flip the switch you want   = bind it (it saves by itself)
  "shooting.weapon"       any GTA weapon name: WEAPON_CARBINERIFLE, WEAPON_MINIGUN, WEAPON_RPG,
                          WEAPON_RAILGUN, WEAPON_SNIPERRIFLE, WEAPON_FIREWORK...
  "shooting.noWantedLevel" true = no police stars while flying (and a few seconds after landing)


LosSantosFpv.json REFERENCE
---------------------
Edit it with any text editor, even while the game is running. Changes apply the next time you launch the
drone. If the file has a mistake, a message tells you the line, and the mod keeps your last good settings.
Comments (// ...) are allowed, but the calibration wizard and shoot binding rewrite the file without them.

keys
  toggleDrone, calibrateController, bindShootControl, resetDrone
      Key names, optionally with a modifier: "Delete", "Shift+Delete", "Ctrl+D", "Home", "PageUp", "F12".
      Don't use F5-F8 (GTA's character switch keys) or F4 (the ScriptHookVDotNet console).

controller
  device          part of the controller name to use ("" = the first joystick found)
  throttle/roll/pitch/yaw
                  written by the wizard: axis name, centre, min, max, invert
  deadband        stick centre deadband, 0.01 = 1 %
  shootControl    written by the shoot binding: "button:N" or "axis:<name>:<threshold>:above|below"
  resetButton     button number that resets the drone (-1 = none)
  xboxPadFallback use an Xbox controller when no joystick/radio is plugged in

drone             (a 5" freestyle quad by default)
  massKg, propDiameterInch, propPitchInch, propBlades, motorKv, batteryCells
                  the build. More Kv/cells or less mass = more power. propPitchInch is the main
                  top-speed knob.
  cameraAngleDeg  FPV camera uptilt (20-40 is typical)
  fovDeg          camera field of view (30-130)
  rates.roll / pitch / yaw
                  Betaflight-style "rcRate", "expo", "superRate". _maxDegPerSec shows the result at
                  full stick (it is recalculated, so don't edit it).
  liftoffImport   enabled + folder + droneName: copy rates, camera angle and parts from one of your
                  saved Liftoff drones (Liftoff_Data\DroneConfigurations). The file is only read.

shooting
  enabled, weapon, shotsPerSecond, damage, noWantedLevel

gameplay
  ramDamage          flying into people knocks them over
  landWhenPilotHurt  land automatically when your character takes damage
  showOsd            show speed / throttle / altitude
  showDroneModel     draw the drone model (only visible from other cameras)
  droneModel         GTA prop used as the drone (default ch_prop_casino_drone_02a)


TROUBLESHOOTING
---------------
No "Los Santos FPV" message at all
  - Script Hook V is out of date for your game version (check ScriptHookV.log in the game folder), or
    ScriptHookVDotNet isn't installed. Check ScriptHookVDotNet.log.
The radio isn't found / the sticks don't move anything
  - Select USB Joystick (HID) mode on the radio, not storage or serial mode. Then run the wizard (Shift + Delete).
  - The wizard screen lists every axis it sees with live values. If they don't change, Windows doesn't see
    the radio.
A stick moves the wrong way
  - Run the wizard again, or flip "invert" for that channel in LosSantosFpv.json.
The drone sinks or climbs at the hover throttle shown on the OSD
  - That's normal near the ground (ground effect). Higher up it should hold within a few percent.
Anything else
  - Send scripts\LosSantosFpv.log with your report.


CREDITS
-------
Mod by Argex. Built with AI assistance.
Flight model is a clean-room reimplementation of Liftoff's drone physics. No game code or assets are
included. Uses Script Hook V (Alexander Blade) and ScriptHookVDotNet Enhanced (Chiheb-Bacha and the SHVDN
team).
Story mode only. Don't use mods in GTA Online.

RELEASE NOTES / UPDATES / SOURCE
--------------------------------
{{REPO_URL}}
