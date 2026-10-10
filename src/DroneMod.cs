using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using GTA;
using GTA.Math;
using GTA.Native;
using GTA.UI;

namespace LosSantosFpv
{
    /// <summary>
    /// FPV drone for GTA V (ScriptHookVDotNet v3, story mode). The quad is self-integrated (Flight.cs) and drawn as a
    /// frozen, collision-less prop whose pose we set every frame; a script camera is hard-attached to it so it inherits
    /// roll. Everything is configured in scripts\LosSantosFpv.json, re-read on every launch.
    /// </summary>
    public class DroneMod : Script
    {
        /// <summary>From the assembly (set by the release build: -p:Version=x.y.z).</summary>
        public static readonly string Version =
            typeof(DroneMod).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "dev";

        readonly LosSantosFpv.Settings cfg;
        readonly RadioInput radio;
        readonly StreamWriter logFile;
        DroneSetup setup;
        Flight fl;
        Prop prop;
        Camera cam;
        bool active;
        Vec launchPos, launchFwd;
        int pedHealth;
        Vector3 pedPos;
        string flash = "";
        long flashUntil, wantedGraceUntil;
        Keys keyToggle, keyCalibrate, keyBindShoot, keyReset;
        int weaponHash, prevMaxWanted = 5;
        float fireCooldown;
        Vec camFwd = new Vec(0, 1, 0), camPos;

        public DroneMod()
        {
            Interval = 0;
            string dir = BaseDirectory ?? "scripts";
            try { logFile = new StreamWriter(Path.Combine(dir, "LosSantosFpv.log"), false) { AutoFlush = true }; } catch { }
            Log("Los Santos FPV " + Version);
            cfg = LosSantosFpv.Settings.Load(Path.Combine(dir, "LosSantosFpv.json"), Log);
            radio = new RadioInput(cfg, Log);
            ApplyKeys();
            Tick += OnTick;
            KeyDown += OnKeyDown;
            Aborted += (s, e) => Deactivate("script unloaded");
            Flash(cfg.LoadError != null ? "LosSantosFpv.json has an error (using defaults): " + cfg.LoadError
                : $"Los Santos FPV {Version}:  {cfg.KeyToggle} = fly drone   {cfg.KeyCalibrate} = calibrate controller   {cfg.KeyBindShoot} = set shoot control", 10);
        }

        void ApplyKeys()
        {
            keyToggle = ParseKey(cfg.KeyToggle, Keys.Delete);
            keyCalibrate = ParseKey(cfg.KeyCalibrate, Keys.Shift | Keys.Delete);
            keyBindShoot = ParseKey(cfg.KeyBindShoot, Keys.Control | Keys.Delete);
            keyReset = ParseKey(cfg.KeyReset, Keys.Back);
        }

        /// <summary>"Delete", "Shift+Delete", "Ctrl+D"... (names from System.Windows.Forms.Keys).</summary>
        Keys ParseKey(string v, Keys def)
        {
            Keys k = Keys.None;
            foreach (var part in (v ?? "").Split('+'))
            {
                string t = part.Trim();
                if (t.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)) t = "Control";
                if (t.Equals("Backspace", StringComparison.OrdinalIgnoreCase)) t = "Back";
                if (t.Equals("Del", StringComparison.OrdinalIgnoreCase)) t = "Delete";
                if (!Enum.TryParse(t, true, out Keys p)) { Log($"Unknown key '{v}', using {def}"); return def; }
                k |= p == Keys.ShiftKey ? Keys.Shift : p == Keys.ControlKey ? Keys.Control : p == Keys.Menu ? Keys.Alt : p;
            }
            return k == Keys.None ? def : k;
        }

        void Log(string s) { try { logFile?.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {s}"); } catch { } }

        void Flash(string msg, double seconds) { flash = msg; flashUntil = M.Ms + (long)(seconds * 1000); }

        // ---------------------------------------------------------------- keys

        void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (radio.WizardActive)
            {
                if (e.KeyData == keyCalibrate) radio.Advance();
                else if (e.KeyCode == Keys.Escape || e.KeyData == keyToggle) radio.Cancel();
                Flash(radio.WizardMessage, 6);
                return;
            }
            if (e.KeyData == keyCalibrate) { cfg.Reload(Log); radio.StartCalibration(); Flash(radio.WizardMessage, 8); }
            else if (e.KeyData == keyBindShoot) { cfg.Reload(Log); radio.StartShootBind(); Flash(radio.WizardMessage, 8); }
            else if (e.KeyData == keyToggle) { if (active) Deactivate("toggle"); else Activate(); }
            else if (e.KeyData == keyReset && active) fl.Place(launchPos, launchFwd);
        }

        // ---------------------------------------------------------------- on / off

        void Activate()
        {
            var ped = Game.Player.Character;
            if (ped == null || !ped.Exists() || ped.IsDead) { Flash("No player to launch from", 4); return; }
            if (!cfg.Reload(Log)) Flash("LosSantosFpv.json has an error, flying with the last good settings: " + cfg.LoadError, 8);
            ApplyKeys();
            setup = DroneSetup.FromSettings(cfg, Log);
            fl = new Flight(setup) { Sweep = Sweep, Ray = Ray, OnImpact = Impact };

            // Launch spot: 1.5 m in front of the gameplay camera, at its height (the quad drops to the ground disarmed).
            var cp = GameplayCamera.Position;
            var cd = GameplayCamera.Direction;
            var fwd = new Vec(cd.X, cd.Y, 0).Normalized;
            launchPos = new Vec(cp.X, cp.Y, cp.Z) + fwd * 1.5f;
            launchFwd = fwd;
            if (Ray(new Vec(cp.X, cp.Y, cp.Z), launchPos).HasValue) launchPos = new Vec(cp.X, cp.Y, cp.Z) + fwd * 0.3f;
            fl.Place(launchPos, launchFwd);

            prop = SpawnProp(cfg.DroneModel) ?? SpawnProp("ch_prop_casino_drone_02a") ?? SpawnProp("prop_tennis_ball");
            if (prop == null) { Flash("Could not spawn the drone model", 5); return; }

            cam = World.CreateCamera(new Vector3(launchPos.X, launchPos.Y, launchPos.Z), Vector3.Zero, setup.Fov);
            Function.Call(Hash.HARD_ATTACH_CAM_TO_ENTITY, cam.Handle, prop.Handle, setup.CameraAngle, 0f, 0f, 0f, 0.07f, 0.03f, true);
            cam.IsActive = true;
            World.RenderingCamera = cam;

            weaponHash = Function.Call<int>(Hash.GET_HASH_KEY, cfg.Weapon);
            if (cfg.ShootEnabled) Function.Call(Hash.REQUEST_WEAPON_ASSET, weaponHash, 31, 0);
            if (cfg.NoWantedLevel)
            {
                prevMaxWanted = Function.Call<int>(Hash.GET_MAX_WANTED_LEVEL);
                Function.Call(Hash.SET_MAX_WANTED_LEVEL, 0);
            }
            pedHealth = ped.Health;
            pedPos = ped.Position;
            active = true;
            Pose(launchPos, launchFwd, new Vec(0, 0, 1));
            Log($"drone on at {launchPos}; controller '{radio.DeviceName}'");
        }

        Prop SpawnProp(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var m = new Model(name);
            if (!m.IsInCdImage || !m.Request(2000)) { Log("model not available: " + name); return null; }
            var p = World.CreateProp(m, new Vector3(launchPos.X, launchPos.Y, launchPos.Z), false, false);
            m.MarkAsNoLongerNeeded();
            if (p == null || !p.Exists()) return null;
            p.IsCollisionEnabled = false;
            p.IsPositionFrozen = true;
            p.IsVisible = cfg.ShowDroneModel;
            Function.Call(Hash.SET_ENTITY_DYNAMIC, p.Handle, false);
            return p;
        }

        void Deactivate(string why)
        {
            if (!active) return;
            active = false;
            Log("drone off (" + why + ")");
            World.RenderingCamera = null;
            if (cam != null && cam.Exists()) cam.Delete();
            cam = null;
            if (prop != null && prop.Exists()) prop.Delete();
            prop = null;
            Function.Call(Hash.CLEAR_FOCUS);
            Function.Call(Hash.RENDER_SCRIPT_CAMS, false, false, 0, true, false, 0);
            if (cfg.NoWantedLevel)
            {
                ClearWanted();
                wantedGraceUntil = M.Ms + 5000;   // crimes from the last shots can still be reported a moment later
                Function.Call(Hash.SET_MAX_WANTED_LEVEL, Math.Max(prevMaxWanted, 0));
            }
            if (why != "toggle") Flash("Drone landed: " + why, 4);
        }

        static void ClearWanted()
        {
            var pl = Game.Player;
            if (pl.WantedLevel != 0) { Function.Call(Hash.SET_PLAYER_WANTED_LEVEL, pl.Handle, 0, false); Function.Call(Hash.SET_PLAYER_WANTED_LEVEL_NOW, pl.Handle, false); }
        }

        // ---------------------------------------------------------------- per frame

        void OnTick(object sender, EventArgs e)
        {
            var s = radio.Read();
            radio.Tick();
            if (radio.WizardActive || M.Ms < flashUntil) DrawFlash();
            if (!active)
            {
                if (M.Ms < wantedGraceUntil) ClearWanted();
                return;
            }

            var ped = Game.Player.Character;
            if (ped == null || !ped.Exists() || ped.IsDead) { Deactivate("pilot died"); return; }
            if (cfg.LandWhenPilotHurt && ped.Health < pedHealth) { Deactivate("pilot hit"); return; }
            pedHealth = Math.Max(pedHealth, ped.Health);
            if (ped.Position.DistanceTo(pedPos) > 25f) { Deactivate("pilot moved"); return; }
            if (prop == null || !prop.Exists()) { Deactivate("drone model gone"); return; }
            if (cfg.NoWantedLevel) ClearWanted();

            // The pilot holds the controller: no walking, shooting, phone or weapon wheel. Pause menu still works.
            Game.DisableAllControlsThisFrame();
            Game.EnableControlThisFrame(GTA.Control.FrontendPause);
            Game.EnableControlThisFrame(GTA.Control.FrontendPauseAlternate);

            if (Game.IsPaused) return;
            fl.In = s;
            if (s.Reset) fl.Place(launchPos, launchFwd);
            fl.Update(Game.LastFrameTime, out var pos, out var fwd, out var up);
            if (fl.P.Z < launchPos.Z - 400f || (fl.P - launchPos).Length > 6000f) fl.Place(launchPos, launchFwd);
            Pose(pos, fwd, up);
            float a = setup.CameraAngle * (float)Math.PI / 180f;
            camFwd = (fwd * (float)Math.Cos(a) + up * (float)Math.Sin(a)).Normalized;
            camPos = pos + fwd * 0.07f + up * 0.03f;
            fireCooldown -= Game.LastFrameTime;
            if (cfg.ShootEnabled && s.Fire && fireCooldown <= 0f) { Shoot(); fireCooldown = 1f / Math.Max(cfg.ShotsPerSecond, 0.5f); }
            if (cfg.ShowOsd) DrawOsd(s);
        }

        void Pose(Vec pos, Vec fwd, Vec up)
        {
            var right = Vec.Cross(fwd, up);
            prop.PositionNoOffset = new Vector3(pos.X, pos.Y, pos.Z);
            var q = FromBasis(right, fwd, up);
            Function.Call(Hash.SET_ENTITY_QUATERNION, prop.Handle, q.X, q.Y, q.Z, q.W);
            // Stream the world around the drone, not around the pilot.
            Function.Call(Hash.SET_FOCUS_POS_AND_VEL, pos.X, pos.Y, pos.Z, fl.V.X, fl.V.Y, fl.V.Z);
        }

        /// <summary>Rotation whose columns are the local +X (right), +Y (forward), +Z (up) axes in world space.</summary>
        static Quaternion FromBasis(Vec r, Vec f, Vec u)
        {
            float m00 = r.X, m01 = f.X, m02 = u.X, m10 = r.Y, m11 = f.Y, m12 = u.Y, m20 = r.Z, m21 = f.Z, m22 = u.Z;
            float tr = m00 + m11 + m22, x, y, z, w;
            if (tr > 0) { float s = (float)Math.Sqrt(tr + 1) * 2; w = 0.25f * s; x = (m21 - m12) / s; y = (m02 - m20) / s; z = (m10 - m01) / s; }
            else if (m00 > m11 && m00 > m22) { float s = (float)Math.Sqrt(1 + m00 - m11 - m22) * 2; w = (m21 - m12) / s; x = 0.25f * s; y = (m01 + m10) / s; z = (m02 + m20) / s; }
            else if (m11 > m22) { float s = (float)Math.Sqrt(1 + m11 - m00 - m22) * 2; w = (m02 - m20) / s; x = (m01 + m10) / s; y = 0.25f * s; z = (m12 + m21) / s; }
            else { float s = (float)Math.Sqrt(1 + m22 - m00 - m11) * 2; w = (m10 - m01) / s; x = (m02 + m20) / s; y = (m12 + m21) / s; z = 0.25f * s; }
            return new Quaternion(x, y, z, w);
        }

        // ---------------------------------------------------------------- world queries

        static Vector3 V3(Vec v) => new Vector3(v.X, v.Y, v.Z);
        static Vec Vc(Vector3 v) => new Vec(v.X, v.Y, v.Z);
        const IntersectFlags Solid = IntersectFlags.Map | IntersectFlags.Vehicles | IntersectFlags.Peds | IntersectFlags.Objects | IntersectFlags.Glass | IntersectFlags.Foliage;

        Hit? Ray(Vec a, Vec b)
        {
            var r = World.Raycast(V3(a), V3(b), Solid, prop);
            if (!r.DidHit) return null;
            return new Hit { Position = Vc(r.HitPosition), Normal = Vc(r.SurfaceNormal), Entity = r.HitEntity };
        }

        /// <summary>Sphere sweep: a synchronous ray along the motion (lengthened by the radius) plus a capsule test.</summary>
        Hit? Sweep(Vec a, Vec b, float radius)
        {
            var d = b - a;
            float len = d.Length;
            var dir = len > 1e-5f ? d * (1f / len) : new Vec(0, 0, -1);
            var r = Ray(a, b + dir * radius);
            if (r.HasValue)
            {
                var h = r.Value;
                h.Position = h.Position - dir * radius;
                return h;
            }
            var c = World.RaycastCapsule(V3(a), V3(b), radius, Solid, prop);
            if (c.DidHit)
                return new Hit { Position = Vc(c.HitPosition) + Vc(c.SurfaceNormal).Normalized * radius, Normal = Vc(c.SurfaceNormal), Entity = c.HitEntity };
            return null;
        }

        /// <summary>One bullet from just ahead of the FPV camera, straight down its axis (the crosshair).</summary>
        void Shoot()
        {
            if (!Function.Call<bool>(Hash.HAS_WEAPON_ASSET_LOADED, weaponHash)) { Function.Call(Hash.REQUEST_WEAPON_ASSET, weaponHash, 31, 0); return; }
            var from = camPos + camFwd * 0.4f;
            var to = camPos + camFwd * 500f;
            Function.Call(Hash.SHOOT_SINGLE_BULLET_BETWEEN_COORDS, from.X, from.Y, from.Z, to.X, to.Y, to.Z, cfg.Damage, true, weaponHash,
                Game.Player.Character.Handle, true, false, -1f);
        }

        void Impact(object entity, float speed)
        {
            if (!cfg.RamDamage) return;
            if (entity is Ped p && p.Exists() && p != Game.Player.Character && !p.IsDead)
            {
                p.ApplyDamage((int)M.Clamp(speed * 3f, 5f, 100f));
                Function.Call(Hash.SET_PED_TO_RAGDOLL, p.Handle, 1500, 2500, 0, false, false, false);
            }
        }

        // ---------------------------------------------------------------- OSD

        static void Text(string s, float x, float y, float scale, Color c, Alignment align = Alignment.Left) =>
            new TextElement(s, new PointF(x, y), scale, c, GTA.UI.Font.ChaletLondon, align, true, true).Draw();

        void DrawOsd(Sticks s)
        {
            Text(fl.Armed ? "ARMED" : "DISARMED", 20, 15, 0.6f, fl.Armed ? Color.FromArgb(90, 255, 120) : Color.FromArgb(255, 90, 80));
            if (fl.Status.Length > 0) Text(fl.Status, 640, 260, 0.8f, Color.FromArgb(255, 220, 80), Alignment.Center);
            Text($"{fl.V.Length * 3.6f:0} km/h", 640, 640, 0.75f, Color.White, Alignment.Center);
            float alt = fl.P.Z - launchPos.Z;
            Text($"THR {s.Throttle * 100:0}%  (hover ~{setup.HoverThrottle() * 100:0}%)    ALT {alt:0.0} m    {fl.MeanRpm / 1000:0.0}k rpm    {s.Source}",
                640, 680, 0.4f, Color.FromArgb(230, 230, 230), Alignment.Center);
            Text("+", 640, 345, 0.5f, s.Fire && cfg.ShootEnabled ? Color.FromArgb(255, 255, 80, 60) : Color.FromArgb(160, 255, 255, 255), Alignment.Center);
        }

        void DrawFlash()
        {
            Text(radio.WizardActive ? radio.WizardMessage : flash, 640, 120, 0.45f, Color.FromArgb(255, 230, 140), Alignment.Center);
            if (radio.WizardActive) Text(radio.DescribeAxes(), 640, 160, 0.4f, Color.FromArgb(200, 200, 200), Alignment.Center);
        }
    }
}
