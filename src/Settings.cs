using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using O = LosSantosFpv.MiniJson.Obj;

namespace LosSantosFpv
{
    /// <summary>One controller channel: which axis, and its calibrated centre / ends / direction.</summary>
    public class Channel
    {
        public string Axis = "";
        public float Center, Min = -1f, Max = 1f;
        public bool Invert;

        public Channel() { }
        public Channel(string axis, float center = 0f, bool invert = false) { Axis = axis; Center = center; Invert = invert; }

        /// <summary>-1..1 around the calibrated centre.</summary>
        public float Bipolar(float raw)
        {
            float v = raw >= Center ? (raw - Center) / Math.Max(Max - Center, 1e-4f) : (raw - Center) / Math.Max(Center - Min, 1e-4f);
            v = M.Clamp(v, -1f, 1f);
            return Invert ? -v : v;
        }

        /// <summary>0..1 across the calibrated range; Invert when the axis reads high at throttle-low.</summary>
        public float Unipolar(float raw)
        {
            float span = Max - Min;
            if (span < 1e-4f) return 0f;
            float t = M.Clamp((raw - Min) / span, 0f, 1f);
            return Invert ? 1f - t : t;
        }

        public O ToJson() => new O { ["axis"] = Axis, ["center"] = R(Center), ["min"] = R(Min), ["max"] = R(Max), ["invert"] = Invert };
        public static Channel FromJson(object o, Channel def)
        {
            if (!(o is O j)) return def;
            return new Channel
            {
                Axis = Settings.Str(j, "axis", def.Axis), Center = Settings.Num(j, "center", def.Center),
                Min = Settings.Num(j, "min", def.Min), Max = Settings.Num(j, "max", def.Max), Invert = Settings.Bool(j, "invert", def.Invert),
            };
        }
        static double R(float v) => Math.Round(v, 4);
        public override string ToString() => Axis + (Invert ? " (inverted)" : "");
    }

    /// <summary>Betaflight-style rates for one axis, evaluated the way Liftoff evaluates them (expo uses x^4).</summary>
    public struct AxisRates
    {
        public float Rate, Expo, SuperExpo;
        public AxisRates(float r, float e, float s) { Rate = r; Expo = e; SuperExpo = s; }
        public float DegPerSec(float x)
        {
            if (Expo != 0f) x += (Math.Sign(x) * x * x * x * x - x) * Expo;
            float deg = 200f * Rate * x;
            if (SuperExpo != 0f) deg /= M.Clamp(1f - Math.Abs(x) * SuperExpo, 0.01f, 1f);
            return M.Clamp(deg, -2000f, 2000f);
        }
        public O ToJson() => new O { ["rcRate"] = Math.Round(Rate, 3), ["expo"] = Math.Round(Expo, 3), ["superRate"] = Math.Round(SuperExpo, 3), ["_maxDegPerSec"] = Math.Round(DegPerSec(1f)) };
        public static AxisRates FromJson(object o, AxisRates d) =>
            o is O j ? new AxisRates(Settings.Num(j, "rcRate", d.Rate), Settings.Num(j, "expo", d.Expo), Settings.Num(j, "superRate", d.SuperExpo)) : d;
        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0:0.00}/{1:0.00}/{2:0.00} ({3:0} deg/s)", Rate, Expo, SuperExpo, DegPerSec(1f));
    }

    /// <summary>
    /// Everything configurable, stored in scripts\LosSantosFpv.json. Missing keys get defaults and the file is rewritten
    /// complete, so new options show up after an update. Re-read every time the drone is launched.
    /// </summary>
    public class Settings
    {
        // keys
        public string KeyToggle = "Delete", KeyCalibrate = "Shift+Delete", KeyBindShoot = "Control+Delete", KeyReset = "Back";
        // controller
        public string Device = "";
        public Channel Throttle = new Channel("x", -1f), Roll = new Channel("y"), Pitch = new Channel("z"), Yaw = new Channel("rx");
        public float Deadband = 0.01f;
        public string ShootControl = "";
        public int ResetButton = -1;
        public bool XboxPadFallback = true;
        // drone
        public float MassKg = 0.65f, PropDiameterIn = 5f, PropPitchIn = 4f, MotorKv = 2450f, CameraAngleDeg = 30f, FovDeg = 100f;
        public int PropBlades = 3, BatteryCells = 4;
        public AxisRates RatesRoll = new AxisRates(1.55f, 0.30f, 0.73f), RatesPitch = new AxisRates(1.55f, 0.30f, 0.73f), RatesYaw = new AxisRates(1.0f, 0.30f, 0.73f);
        public bool LiftoffImport = false;
        public string LiftoffFolder = @"C:\Program Files (x86)\Steam\steamapps\common\Liftoff\Liftoff_Data\DroneConfigurations", LiftoffDrone = "";
        // weapon
        public bool ShootEnabled = true;
        public string Weapon = "WEAPON_CARBINERIFLE";
        public float ShotsPerSecond = 10f;
        public int Damage = 34;
        public bool NoWantedLevel = true;
        // gameplay
        public bool RamDamage = true, LandWhenPilotHurt = true, ShowDroneModel = false, ShowOsd = true;
        public string DroneModel = "ch_prop_casino_drone_02a";

        public string Path { get; private set; }
        public string LoadError { get; private set; }

        public static Settings Load(string path, Action<string> log)
        {
            var s = new Settings { Path = path };
            try
            {
                if (File.Exists(path))
                {
                    if (MiniJson.Parse(File.ReadAllText(path)) is O root) s.Read(root);
                }
                else
                {
                    // Earlier versions were called "GTA FPV": GtaFpv.json (1.0), GtaFpv.ini (pre-1.0).
                    string dir = System.IO.Path.GetDirectoryName(path) ?? ".";
                    string oldJson = System.IO.Path.Combine(dir, "GtaFpv.json"), oldIni = System.IO.Path.Combine(dir, "GtaFpv.ini");
                    if (File.Exists(oldJson) && MiniJson.Parse(File.ReadAllText(oldJson)) is O old) { s.Read(old); log("Migrated settings from " + oldJson); }
                    else if (File.Exists(oldIni)) { s.MigrateIni(oldIni); log("Migrated settings from " + oldIni); }
                    s.Save();   // only when creating it; a hand-edited file (with comments) is left alone
                }
            }
            catch (Exception e)
            {
                // Keep the user's file as-is (don't overwrite a typo with defaults); run on defaults and say why.
                s.LoadError = e.Message;
                log("Settings: " + e.Message + " - using defaults for this session");
            }
            return s;
        }

        /// <summary>Re-read the file into this object (picked up each time the drone launches). False + LoadError on a bad file.</summary>
        public bool Reload(Action<string> log)
        {
            try
            {
                if (File.Exists(Path) && MiniJson.Parse(File.ReadAllText(Path)) is O root) Read(root);
                LoadError = null;
                return true;
            }
            catch (Exception e)
            {
                LoadError = e.Message;
                log("Settings: " + e.Message + " - keeping the previous values");
                return false;
            }
        }

        public void Save()
        {
            try { File.WriteAllText(Path, MiniJson.Write(ToJson())); } catch { }
        }

        O ToJson() => new O
        {
            ["_readme"] = "Los Santos FPV settings. Edit while the game runs; changes apply the next time you launch the drone. See README.txt for every option.",
            ["keys"] = new O
            {
                ["toggleDrone"] = KeyToggle, ["calibrateController"] = KeyCalibrate, ["bindShootControl"] = KeyBindShoot, ["resetDrone"] = KeyReset,
            },
            ["controller"] = new O
            {
                ["device"] = Device,
                ["throttle"] = Throttle.ToJson(), ["roll"] = Roll.ToJson(), ["pitch"] = Pitch.ToJson(), ["yaw"] = Yaw.ToJson(),
                ["deadband"] = D(Deadband),
                ["shootControl"] = ShootControl,
                ["resetButton"] = ResetButton,
                ["xboxPadFallback"] = XboxPadFallback,
            },
            ["drone"] = new O
            {
                ["massKg"] = D(MassKg), ["propDiameterInch"] = D(PropDiameterIn), ["propPitchInch"] = D(PropPitchIn), ["propBlades"] = PropBlades,
                ["motorKv"] = D(MotorKv), ["batteryCells"] = BatteryCells, ["cameraAngleDeg"] = D(CameraAngleDeg), ["fovDeg"] = D(FovDeg),
                ["rates"] = new O { ["roll"] = RatesRoll.ToJson(), ["pitch"] = RatesPitch.ToJson(), ["yaw"] = RatesYaw.ToJson() },
                ["liftoffImport"] = new O { ["enabled"] = LiftoffImport, ["folder"] = LiftoffFolder, ["droneName"] = LiftoffDrone },
            },
            ["shooting"] = new O
            {
                ["enabled"] = ShootEnabled, ["weapon"] = Weapon, ["shotsPerSecond"] = D(ShotsPerSecond), ["damage"] = Damage,
                ["noWantedLevel"] = NoWantedLevel,
            },
            ["gameplay"] = new O
            {
                ["ramDamage"] = RamDamage, ["landWhenPilotHurt"] = LandWhenPilotHurt, ["showOsd"] = ShowOsd,
                ["showDroneModel"] = ShowDroneModel, ["droneModel"] = DroneModel,
            },
        };

        void Read(O r)
        {
            var k = Sub(r, "keys");
            KeyToggle = Str(k, "toggleDrone", KeyToggle); KeyCalibrate = Str(k, "calibrateController", KeyCalibrate);
            KeyBindShoot = Str(k, "bindShootControl", KeyBindShoot); KeyReset = Str(k, "resetDrone", KeyReset);
            var c = Sub(r, "controller");
            Device = Str(c, "device", Device);
            Throttle = Channel.FromJson(c?["throttle"], Throttle); Roll = Channel.FromJson(c?["roll"], Roll);
            Pitch = Channel.FromJson(c?["pitch"], Pitch); Yaw = Channel.FromJson(c?["yaw"], Yaw);
            Deadband = Num(c, "deadband", Deadband); ShootControl = Str(c, "shootControl", ShootControl);
            ResetButton = (int)Num(c, "resetButton", ResetButton); XboxPadFallback = Bool(c, "xboxPadFallback", XboxPadFallback);
            var d = Sub(r, "drone");
            MassKg = Num(d, "massKg", MassKg); PropDiameterIn = Num(d, "propDiameterInch", PropDiameterIn); PropPitchIn = Num(d, "propPitchInch", PropPitchIn);
            PropBlades = (int)Num(d, "propBlades", PropBlades); MotorKv = Num(d, "motorKv", MotorKv); BatteryCells = (int)Num(d, "batteryCells", BatteryCells);
            CameraAngleDeg = Num(d, "cameraAngleDeg", CameraAngleDeg); FovDeg = Num(d, "fovDeg", FovDeg);
            var rt = Sub(d, "rates");
            RatesRoll = AxisRates.FromJson(rt?["roll"], RatesRoll); RatesPitch = AxisRates.FromJson(rt?["pitch"], RatesPitch); RatesYaw = AxisRates.FromJson(rt?["yaw"], RatesYaw);
            var lf = Sub(d, "liftoffImport");
            LiftoffImport = Bool(lf, "enabled", LiftoffImport); LiftoffFolder = Str(lf, "folder", LiftoffFolder); LiftoffDrone = Str(lf, "droneName", LiftoffDrone);
            var w = Sub(r, "shooting");
            ShootEnabled = Bool(w, "enabled", ShootEnabled); Weapon = Str(w, "weapon", Weapon); ShotsPerSecond = Num(w, "shotsPerSecond", ShotsPerSecond);
            Damage = (int)Num(w, "damage", Damage); NoWantedLevel = Bool(w, "noWantedLevel", NoWantedLevel);
            var g = Sub(r, "gameplay");
            RamDamage = Bool(g, "ramDamage", RamDamage); LandWhenPilotHurt = Bool(g, "landWhenPilotHurt", LandWhenPilotHurt); ShowOsd = Bool(g, "showOsd", ShowOsd);
            ShowDroneModel = Bool(g, "showDroneModel", ShowDroneModel); DroneModel = Str(g, "droneModel", DroneModel);
        }

        /// <summary>The pre-1.0 GtaFpv.ini (key = value) from before the JSON file.</summary>
        void MigrateIni(string ini)
        {
            var kv = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in File.ReadAllLines(ini))
            {
                int eq = line.IndexOf('=');
                if (line.TrimStart().StartsWith("#") || eq < 0) continue;
                kv[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
            Channel Ch(string key, Channel def)
            {
                if (!kv.TryGetValue(key, out var v)) return def;
                var p = v.Split(';');
                var c = new Channel(p[0]);
                if (p.Length >= 5)
                {
                    c.Center = float.Parse(p[1], CultureInfo.InvariantCulture); c.Min = float.Parse(p[2], CultureInfo.InvariantCulture);
                    c.Max = float.Parse(p[3], CultureInfo.InvariantCulture); c.Invert = bool.Parse(p[4]);
                }
                return c;
            }
            string S(string key, string def) => kv.TryGetValue(key, out var v) && v.Length > 0 ? v : def;
            float F(string key, float def) => kv.TryGetValue(key, out var v) && float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : def;
            Throttle = Ch("Throttle", Throttle); Roll = Ch("Roll", Roll); Pitch = Ch("Pitch", Pitch); Yaw = Ch("Yaw", Yaw);
            Device = S("JoystickName", Device); ShootControl = S("FireBind", ShootControl); Deadband = F("Deadband", Deadband);
            KeyToggle = S("ToggleKey", KeyToggle); KeyCalibrate = S("CalibrateKey", KeyCalibrate); KeyReset = S("ResetKey", KeyReset); KeyBindShoot = S("FireBindKey", KeyBindShoot);
            Weapon = S("FireWeapon", Weapon); Damage = (int)F("FireDamage", Damage); ShotsPerSecond = F("FireRate", ShotsPerSecond);
            DroneModel = S("PropModel", DroneModel); ShowDroneModel = S("ShowModel", "false") == "true";
            FovDeg = F("FovDegrees", FovDeg);
            if (F("CameraAngle", -1) >= 0) CameraAngleDeg = F("CameraAngle", -1);
            string lf = S("LiftoffDroneName", "");
            if (lf.Length > 0) { LiftoffImport = true; LiftoffDrone = lf; LiftoffFolder = S("LiftoffDroneFolder", LiftoffFolder); }
        }

        static double D(float v) => Math.Round((double)v, 4);

        // ---------------- tolerant getters ----------------

        internal static O Sub(O o, string k) => o?[k] as O;
        internal static string Str(O o, string k, string d) => o?[k] is string s ? s : d;
        internal static float Num(O o, string k, float d) => o?[k] is double v ? (float)v : o?[k] is string s && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : d;
        internal static bool Bool(O o, string k, bool d) => o?[k] is bool b ? b : o?[k] is string s ? s.Equals("true", StringComparison.OrdinalIgnoreCase) : d;
    }
}
