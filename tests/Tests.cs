using System;
using System.IO;
using LosSantosFpv;

/// <summary>
/// Headless checks: the flight model must match the fpv-drone-port reference bench, sticks must turn the right way,
/// and the settings file must round-trip and survive mistakes. `dotnet run --project tests` (add "hid" to list a
/// connected controller's axes instead).
/// </summary>
static class Tests
{
    static int failed;

    static void Check(string name, bool ok, string detail)
    {
        Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}: {detail}");
        if (!ok) failed++;
    }

    static Flight Armed(DroneSetup s, Vec pos)
    {
        var f = new Flight(s);
        f.Place(pos, new Vec(0, 1, 0));
        f.In = new Sticks { Throttle = 0 };
        f.Update(0.02f, out _, out _, out _);   // throttle seen low -> armed
        return f;
    }

    static void Run(Flight f, Sticks s, float seconds) { f.In = s; for (int i = 0; i < seconds * 100; i++) f.Update(0.01f, out _, out _, out _); }

    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "hid") { HidTest.Run(); return 0; }
        Action<string> quiet = _ => { };

        // Defaults = a 5" freestyle quad (reference table in the fpv-drone-port skill: flight-core.md).
        var cfg = new Settings();
        cfg.RatesRoll = cfg.RatesPitch = new AxisRates(2.25f, 0.5f, 0.63f);
        var s = DroneSetup.FromSettings(cfg, quiet);
        Check("max rpm", Math.Abs(s.MaxRpm - 26111) < 50, $"{s.MaxRpm:0} (ref 26111)");
        float tw = 4 * s.Thrust(s.MaxRpm, 0) / (s.Mass * Flight.G);
        Check("thrust/weight", Math.Abs(tw - 5.4f) < 0.2f, $"{tw:0.00} (ref 5.4)");

        float hov = s.HoverThrottle();
        var f = Armed(s, new Vec(0, 0, 100));
        Check("armed after throttle low", f.Armed, f.Status);
        Run(f, new Sticks { Throttle = hov }, 3);
        Check("hover holds", Math.Abs(f.V.Z) < 0.5f, $"hover {hov * 100:0.0}%, vz {f.V.Z:0.00} m/s after 3 s");

        f = Armed(s, new Vec(0, 0, 100));
        Run(f, new Sticks { Throttle = 1 }, 15);
        Check("vertical top speed", Math.Abs(f.V.Z - 22.6f) < 1.5f, $"{f.V.Z:0.0} m/s (ref 22.6)");

        f = Armed(s, new Vec(0, 0, 100));
        f.In = new Sticks { Throttle = 0.5f, Roll = 1 };
        f.Update(0.024f, out _, out _, out _);
        float r24 = f.W.Y * 57.2958f;
        Check("roll response", r24 > 0.85f * 1216, $"{r24:0} deg/s after 24 ms (ref 1216 full stick)");
        Run(f, new Sticks { Throttle = 0.5f, Roll = 1 }, 0.1f);
        Check("roll right tilts up-vector right", f.U.X > 0.3f, $"U={f.U}");

        f = Armed(s, new Vec(0, 0, 100));
        Run(f, new Sticks { Throttle = 0.5f, Pitch = 0.3f }, 0.1f);
        Check("pitch forward = nose down", f.F.Z < -0.05f, $"F={f.F}");

        f = Armed(s, new Vec(0, 0, 100));
        Run(f, new Sticks { Throttle = 0.5f, Yaw = 0.3f }, 0.1f);
        Check("yaw right = nose right", f.F.X > 0.05f, $"F={f.F}");

        f = Armed(s, new Vec(0, 0, 100));
        Run(f, new Sticks { Throttle = 0.5f }, 0.05f);
        f.Place(new Vec(0, 0, 100), new Vec(0, 1, 0));
        Check("reset disarms", !f.Armed && f.WaitLow, f.Status);

        // Collision: a floor at z=0 must stop a falling quad.
        f = Armed(s, new Vec(0, 0, 5));
        f.Sweep = (a, b, r) => b.Z - r < 0 ? new Hit { Position = new Vec(b.X, b.Y, r), Normal = new Vec(0, 0, 1) } : (Hit?)null;
        Run(f, new Sticks { Throttle = 0 }, 4);
        Check("lands on the floor", f.P.Z > 0 && f.P.Z < 0.2f, $"z={f.P.Z:0.000}");

        // Settings: defaults written, round trip, comments, bad file kept.
        var dir = Path.Combine(Path.GetTempPath(), "lossantosfpv-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = Path.Combine(dir, "LosSantosFpv.json");
            var a = Settings.Load(path, quiet);
            Check("settings file created", File.Exists(path) && a.LoadError == null, path);
            a.Weapon = "WEAPON_RPG"; a.ShootControl = "axis:slider:0:above"; a.Yaw = new Channel("rx", 0.01f, true); a.Save();
            var b = Settings.Load(path, quiet);
            Check("settings round trip", b.Weapon == "WEAPON_RPG" && b.ShootControl == "axis:slider:0:above" && b.Yaw.Axis == "rx" && b.Yaw.Invert, $"{b.Weapon} {b.ShootControl} {b.Yaw}");
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"WEAPON_RPG\"", "\"WEAPON_MINIGUN\", // comment ok"));
            Check("comments allowed", b.Reload(quiet) && b.Weapon == "WEAPON_MINIGUN", b.Weapon);
            File.WriteAllText(path, "{ \"keys\": { \"toggleDrone\": Delete } }");
            Check("bad file rejected, kept, old values kept", !b.Reload(quiet) && b.Weapon == "WEAPON_MINIGUN" && File.ReadAllText(path).Contains("Delete }"), b.LoadError);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        // Migration from the old "GTA FPV" name.
        dir = Path.Combine(Path.GetTempPath(), "lossantosfpv-mig-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "GtaFpv.json"), "{ \"shooting\": { \"weapon\": \"WEAPON_RAILGUN\" } }");
            var m = Settings.Load(Path.Combine(dir, "LosSantosFpv.json"), quiet);
            Check("migrates GtaFpv.json", m.Weapon == "WEAPON_RAILGUN" && File.Exists(Path.Combine(dir, "LosSantosFpv.json")), m.Weapon);
            File.Delete(Path.Combine(dir, "GtaFpv.json")); File.Delete(Path.Combine(dir, "LosSantosFpv.json"));
            File.WriteAllText(Path.Combine(dir, "GtaFpv.ini"), "Yaw = rx;0.01;-1;1;True\nFireBind = button:2\n");
            m = Settings.Load(Path.Combine(dir, "LosSantosFpv.json"), quiet);
            Check("migrates GtaFpv.ini", m.Yaw.Axis == "rx" && m.Yaw.Invert && m.ShootControl == "button:2", $"{m.Yaw} {m.ShootControl}");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }

        Console.WriteLine(failed == 0 ? "\nAll checks passed." : $"\n{failed} check(s) FAILED.");
        return failed == 0 ? 0 : 1;
    }
}
