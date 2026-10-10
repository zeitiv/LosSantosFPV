using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;

namespace LosSantosFpv
{
    /// <summary>Stick values in pilot convention: roll/pitch/yaw -1..1 (right/forward/right positive), throttle 0..1.</summary>
    public struct Sticks
    {
        public float Roll, Pitch, Yaw, Throttle;
        public bool Reset, Fire;
        public string Source;
    }

    /// <summary>
    /// Reads any USB joystick-mode RC radio (EdgeTX/OpenTX: TBS Tango/Mambo, RadioMaster, Jumper, FrSky...) or HID
    /// gamepad as raw HID, so every axis and switch is visible; falls back to an Xbox pad (XInput, mode 2).
    /// Owns the calibration wizard and the shoot-control binding.
    /// </summary>
    public class RadioInput
    {
        readonly Settings cfg;
        readonly Action<string> log;
        HidJoystick hid;
        long nextScan;
        bool loggedScan;
        public readonly Dictionary<string, float> Raw = new Dictionary<string, float>();
        ulong buttons;

        public RadioInput(Settings cfg, Action<string> log) { this.cfg = cfg; this.log = log; }

        public string DeviceName => hid != null ? hid.Name : "none";

        void Scan()
        {
            if (hid != null && hid.Connected) return;
            hid?.Dispose();
            hid = null;
            try { hid = HidJoystick.Open((cfg.Device ?? "").ToLowerInvariant(), log, !loggedScan); }
            catch (Exception e) { log("Controller scan failed: " + e.Message); }
            loggedScan = true;
        }

        bool Poll()
        {
            if (M.Ms > nextScan) { Scan(); nextScan = M.Ms + 3000; }
            if (hid == null) return false;
            if (!hid.Connected) { log("Controller disconnected: " + hid.Name); hid.Dispose(); hid = null; nextScan = 0; return false; }
            hid.Snapshot(Raw, out buttons);
            return true;
        }

        float R(string axis) => axis != null && Raw.TryGetValue(axis, out var v) ? v : 0f;

        /// <summary>"button:3" or "axis:slider:0.0:above".</summary>
        bool ShootHeld()
        {
            var p = (cfg.ShootControl ?? "").Split(':');
            if (p.Length >= 2 && p[0] == "button" && int.TryParse(p[1], out int b)) return b >= 0 && b < 64 && (buttons & (1UL << b)) != 0;
            if (p.Length >= 4 && p[0] == "axis" && float.TryParse(p[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float t))
                return p[3] == "below" ? R(p[1]) < t : R(p[1]) > t;
            return false;
        }

        public Sticks Read()
        {
            var s = new Sticks();
            if (Poll())
            {
                s.Source = hid.Name;
                s.Roll = cfg.Roll.Bipolar(R(cfg.Roll.Axis));
                s.Pitch = cfg.Pitch.Bipolar(R(cfg.Pitch.Axis));
                s.Yaw = cfg.Yaw.Bipolar(R(cfg.Yaw.Axis));
                s.Throttle = cfg.Throttle.Unipolar(R(cfg.Throttle.Axis));
                if (cfg.ResetButton >= 0 && cfg.ResetButton < 64) s.Reset = (buttons & (1UL << cfg.ResetButton)) != 0;
                s.Fire = ShootHeld();
            }
            else if (cfg.XboxPadFallback && (XInputGetState(0, out var x) == 0 || XInputGetState(1, out x) == 0))
            {
                // Mode 2 on a pad: left stick = throttle (up from centre) / yaw, right stick = pitch / roll.
                s.Source = "Xbox pad";
                s.Throttle = M.Clamp(x.Gamepad.sThumbLY / 32767f * 0.5f + 0.5f, 0f, 1f);
                s.Yaw = x.Gamepad.sThumbLX / 32767f;
                s.Pitch = x.Gamepad.sThumbRY / 32767f;
                s.Roll = x.Gamepad.sThumbRX / 32767f;
                s.Reset = (x.Gamepad.wButtons & 0x8000) != 0;   // Y
                s.Fire = x.Gamepad.bRightTrigger > 128;          // RT
            }
            else s.Source = "no controller";
            s.Roll = Dead(s.Roll); s.Pitch = Dead(s.Pitch); s.Yaw = Dead(s.Yaw);
            return s;
        }

        float Dead(float v)
        {
            float d = M.Clamp(cfg.Deadband, 0f, 0.5f);
            if (Math.Abs(v) <= d) return 0f;
            return Math.Sign(v) * (Math.Abs(v) - d) / (1f - d);
        }

        public string DescribeAxes() =>
            hid == null ? "no USB joystick/radio found" : hid.Name + ":  " + string.Join("   ", Raw.Keys.OrderBy(k => k).Select(a => $"{a} {R(a):+0.00;-0.00}"))
            + (buttons != 0 ? "   buttons " + string.Join(",", Enumerable.Range(0, 64).Where(i => (buttons & (1UL << i)) != 0)) : "");

        // ---------------- calibration wizard + shoot binding ----------------

        public enum Mode { None, Calibrate, BindShoot }
        public Mode Active { get; private set; }
        public bool WizardActive => Active != Mode.None;
        public string WizardMessage = "";
        int step;
        long bindUntil;
        ulong restButtons;
        readonly Dictionary<string, float> lo = new(), hi = new(), rest = new(), deflected = new();
        readonly Dictionary<string, string> picked = new();

        string[] Prompts => new[]
        {
            $"1/7  Move BOTH sticks in full circles and the throttle through its whole range a few times.  Then press {cfg.KeyCalibrate}.",
            $"2/7  Centre the sticks, put the THROTTLE fully DOWN, all switches to their normal position.  Press {cfg.KeyCalibrate}.",
            $"3/7  Push the THROTTLE fully UP and hold.  Press {cfg.KeyCalibrate}.",
            $"4/7  Hold ROLL fully RIGHT.  Press {cfg.KeyCalibrate}.",
            $"5/7  Hold PITCH fully FORWARD (nose down).  Press {cfg.KeyCalibrate}.",
            $"6/7  Hold YAW fully RIGHT.  Press {cfg.KeyCalibrate}.",
            $"7/7  SHOOTING: flip the switch / hold the button you want to shoot with, then press {cfg.KeyCalibrate}.  (Nothing moved = no shoot control.)",
        };

        public void Cancel()
        {
            if (!WizardActive) return;
            Active = Mode.None;
            WizardMessage = "Cancelled - keeping the previous setup.";
            log("Wizard cancelled");
        }

        public void StartCalibration()
        {
            nextScan = 0;
            if (!Poll()) { WizardMessage = "No USB joystick/radio found. Plug the radio in and choose USB Joystick (HID) mode. Xbox pads need no calibration."; return; }
            Active = Mode.Calibrate; step = 0;
            lo.Clear(); hi.Clear(); rest.Clear(); picked.Clear(); deflected.Clear();
            WizardMessage = Prompts[0];
            log("Calibration started on " + DescribeAxes());
        }

        /// <summary>Shoot binding on its own: one key press, then flip the switch / press the button. Saves automatically.</summary>
        public void StartShootBind()
        {
            nextScan = 0;
            if (!Poll()) { WizardMessage = "No USB joystick/radio found. (On an Xbox pad the right trigger shoots.)"; return; }
            Active = Mode.BindShoot;
            rest.Clear();
            foreach (var kv in Raw) rest[kv.Key] = kv.Value;
            restButtons = buttons;
            bindUntil = M.Ms + 15000;
            WizardMessage = "Flip the switch or press the button you want to SHOOT with...   (Esc cancels)";
            log("Shoot bind started on " + DescribeAxes());
        }

        /// <summary>Call every frame.</summary>
        public void Tick()
        {
            if (Active == Mode.Calibrate)
            {
                foreach (var kv in Raw)
                {
                    lo[kv.Key] = lo.TryGetValue(kv.Key, out var l) ? Math.Min(l, kv.Value) : kv.Value;
                    hi[kv.Key] = hi.TryGetValue(kv.Key, out var h) ? Math.Max(h, kv.Value) : kv.Value;
                }
            }
            else if (Active == Mode.BindShoot)
            {
                if (M.Ms > bindUntil) { Active = Mode.None; WizardMessage = "Nothing moved - shoot control unchanged."; return; }
                var stickAxes = new HashSet<string> { cfg.Throttle.Axis, cfg.Roll.Axis, cfg.Pitch.Axis, cfg.Yaw.Axis };
                string bind = DetectShoot(stickAxes, 0.6f);
                if (bind != null)
                {
                    cfg.ShootControl = bind;
                    cfg.Save();
                    Active = Mode.None;
                    WizardMessage = "Shoot control saved: " + bind;
                    log("Shoot control = " + bind);
                }
            }
        }

        /// <summary>A newly pressed button, else a non-stick axis (switch) moved far from rest; null if nothing.</summary>
        string DetectShoot(HashSet<string> exclude, float minMove)
        {
            ulong newly = buttons & ~restButtons;
            if (newly != 0)
            {
                int b = 0; while ((newly & (1UL << b)) == 0) b++;
                return "button:" + b;
            }
            var sw = Raw.Where(kv => !exclude.Contains(kv.Key) && rest.ContainsKey(kv.Key))
                        .OrderByDescending(kv => Math.Abs(kv.Value - rest[kv.Key])).FirstOrDefault();
            if (sw.Key == null || Math.Abs(sw.Value - rest[sw.Key]) < minMove) return null;
            return string.Format(CultureInfo.InvariantCulture, "axis:{0}:{1:0.###}:{2}", sw.Key, (sw.Value + rest[sw.Key]) / 2, sw.Value > rest[sw.Key] ? "above" : "below");
        }

        /// <summary>Calibrate key during the wizard: record this step and move on.</summary>
        public void Advance()
        {
            if (Active != Mode.Calibrate) return;
            if (!Poll()) { Active = Mode.None; WizardMessage = "Controller disconnected"; return; }
            string[] fn = { null, null, "throttle", "roll", "pitch", "yaw", null };
            switch (step)
            {
                case 0:
                    int moved = hi.Keys.Count(k => hi[k] - lo[k] > 0.5f);
                    log($"Calibration step 1: {moved} axes moved; ranges: " + string.Join(", ", hi.Keys.Select(k => $"{k} {lo[k]:0.00}..{hi[k]:0.00}")));
                    if (moved < 4)
                    {
                        WizardMessage = $"Only saw {moved} axes move - move both sticks fully around, then press {cfg.KeyCalibrate} again.  (Nothing moving? Put the radio in USB Joystick mode.  Esc cancels.)";
                        return;
                    }
                    break;
                case 1:
                    foreach (var kv in Raw) rest[kv.Key] = kv.Value;
                    restButtons = buttons;
                    break;
                case 6:
                    var bind = DetectShoot(new HashSet<string>(picked.Values), 0.4f);
                    if (bind != null) cfg.ShootControl = bind;
                    log("Calibration: shoot = " + (bind ?? "unchanged"));
                    break;
                default:
                    // The unused axis that moved furthest from the rest position.
                    var best = Raw.Where(kv => !picked.ContainsValue(kv.Key) && rest.ContainsKey(kv.Key) && hi.ContainsKey(kv.Key))
                                  .OrderByDescending(kv => Math.Abs(kv.Value - rest[kv.Key])).FirstOrDefault();
                    if (best.Key == null || Math.Abs(best.Value - rest[best.Key]) < 0.3f)
                    { WizardMessage = "No stick movement detected - hold it fully over, then press the key again.   " + Prompts[step]; return; }
                    picked[fn[step]] = best.Key;
                    deflected[fn[step]] = best.Value;
                    log($"Calibration: {fn[step]} = {best.Key} (rest {rest[best.Key]:0.00} -> {best.Value:0.00})");
                    break;
            }
            step++;
            if (step < 7) { WizardMessage = Prompts[step]; return; }
            Finish();
        }

        void Finish()
        {
            Channel Make(string f)
            {
                string k = picked[f];
                return new Channel { Axis = k, Min = lo[k], Max = hi[k], Center = rest[k], Invert = deflected[f] < rest[k] };
            }
            cfg.Throttle = Make("throttle"); cfg.Roll = Make("roll"); cfg.Pitch = Make("pitch"); cfg.Yaw = Make("yaw");
            cfg.Device = hid.Name;
            cfg.Save();
            Active = Mode.None;
            WizardMessage = $"Saved!  throttle={cfg.Throttle}  roll={cfg.Roll}  pitch={cfg.Pitch}  yaw={cfg.Yaw}  shoot={(cfg.ShootControl.Length > 0 ? cfg.ShootControl : "none")}";
            log("Calibration " + WizardMessage);
        }

        // ---------------- interop ----------------

        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_GAMEPAD { public ushort wButtons; public byte bLeftTrigger, bRightTrigger; public short sThumbLX, sThumbLY, sThumbRX, sThumbRY; }
        [StructLayout(LayoutKind.Sequential)]
        struct XINPUT_STATE { public uint dwPacketNumber; public XINPUT_GAMEPAD Gamepad; }
        [DllImport("xinput1_4.dll")] static extern uint XInputGetState(uint user, out XINPUT_STATE state);
    }
}
