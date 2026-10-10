using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

namespace LosSantosFpv
{
    /// <summary>net48 lacks Math.Clamp / MathF / TickCount64.</summary>
    public static class M
    {
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static long Ms => System.Diagnostics.Stopwatch.GetTimestamp() * 1000 / System.Diagnostics.Stopwatch.Frequency;
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
    }

    /// <summary>The quad's build and rates in SI, from LosSantosFpv.json and optionally a saved Liftoff .drone.</summary>
    public class DroneSetup
    {
        public string Name = "custom";
        public AxisRates Roll, Pitch, Yaw;
        public float CameraAngle, Fov, Mass, PropDiameter, PropPitch, Kv, HubOffset, IdleFraction = 0.04f, MaxRpm;
        public int PropBlades, Cells;

        public static DroneSetup FromSettings(Settings c, Action<string> log)
        {
            var s = new DroneSetup
            {
                Roll = c.RatesRoll, Pitch = c.RatesPitch, Yaw = c.RatesYaw,
                CameraAngle = M.Clamp(c.CameraAngleDeg, -10f, 80f), Fov = M.Clamp(c.FovDeg, 30f, 130f),
                Mass = M.Clamp(c.MassKg, 0.02f, 20f), PropDiameter = M.Clamp(c.PropDiameterIn, 1f, 30f), PropPitch = M.Clamp(c.PropPitchIn, 1f, 20f),
                PropBlades = Math.Max(2, Math.Min(c.PropBlades, 6)), Kv = M.Clamp(c.MotorKv, 100f, 30000f), Cells = Math.Max(1, Math.Min(c.BatteryCells, 12)),
            };
            s.HubOffset = 0.0156f * s.PropDiameter;
            if (c.LiftoffImport && !string.IsNullOrEmpty(c.LiftoffDrone))
            {
                if (Directory.Exists(c.LiftoffFolder))
                {
                    try { s.ImportLiftoff(c.LiftoffFolder, c.LiftoffDrone, log); }
                    catch (Exception e) { log("Liftoff import failed: " + e.Message); }
                }
                else log("Liftoff import: folder not found: " + c.LiftoffFolder);
            }
            s.DeriveMaxRpm();
            log($"Drone '{s.Name}': {s.Mass:0.000} kg, {s.PropDiameter}x{s.PropPitch}x{s.PropBlades} props, {s.Kv} Kv {s.Cells}S, max {s.MaxRpm:0} rpm, " +
                $"T/W {4 * s.Thrust(s.MaxRpm, 0) / (s.Mass * 9.80665f):0.0}, hover {s.HoverThrottle() * 100:0}%, cam {s.CameraAngle} deg; rates R {s.Roll} P {s.Pitch} Y {s.Yaw}");
            return s;
        }

        public void DeriveMaxRpm()
        {
            double D = PropDiameter, P = PropPitch, B = PropBlades;
            double K = 5.74345e-15 * Math.Pow(D, 3.39007) * Math.Pow(P, 1.38378) * Math.Pow(B, 0.654997) * Math.Pow(Kv, 3);
            double Io = 0.45, Rm = 0.1, n = Cells, V = n * 3.7, Rc = Math.Max(120.0 / Math.Pow(2, n), 5) / 1000.0;
            double R = n * Rc + Rm;
            double a = K * R * R, b = -(2 * K * V * R + 1), c = K * V * V + Io;
            double disc = Math.Max(b * b - 4 * a * c, 0);
            double I = a > 1e-12 ? (-b - Math.Sqrt(disc)) / (2 * a) : c / -b;
            double Vm = V - n * Rc * I - I * Rm;
            double rpm = Math.Max(Vm, 1) * Kv;
            double tipCap = 340.0 / (Math.PI * PropDiameter * 0.0254) * 60;
            MaxRpm = (float)Math.Min(rpm, tipCap);
        }

        public float Thrust(float rpm, float vAxial)
        {
            if (rpm < 5f) return 0f;
            float stat = 7.47958e-5f * M.Pow(PropBlades, 4.70298f) + 2.525e-11f * PropPitch * PropDiameter * PropDiameter * PropDiameter * rpm * rpm;
            float pitchSpeed = rpm * PropPitch * 0.0254f / 60f;
            return stat * (1f - Math.Max(vAxial, 0f) / (1.1f * pitchSpeed));
        }

        /// <summary>Throttle that hovers (static, no ground effect).</summary>
        public float HoverThrottle()
        {
            float lo = 0, hi = 1;
            for (int i = 0; i < 40; i++)
            {
                float t = (lo + hi) / 2, rpm = IdleFraction * MaxRpm + (MaxRpm - IdleFraction * MaxRpm) * t;
                if (4 * Thrust(rpm, 0) > Mass * 9.80665f) hi = t; else lo = t;
            }
            return (lo + hi) / 2;
        }

        void ImportLiftoff(string folder, string wanted, Action<string> log)
        {
            foreach (var file in Directory.GetFiles(folder, "*.drone", SearchOption.AllDirectories))
            {
                var doc = new XmlDocument();
                doc.LoadXml(File.ReadAllText(file)); // files declare utf-16 but are plain text
                var root = doc.DocumentElement;
                if (Text(root, "name")?.Trim() != wanted.Trim()) continue;
                Name = wanted;
                var fc = root["flightControllerSettings"];
                if (fc != null) { Roll = Rates(fc, "roll"); Pitch = Rates(fc, "pitch"); Yaw = Rates(fc, "yaw"); }
                var fb = root["frameBlueprint"];
                if (fb != null)
                {
                    if (float.TryParse(Text(fb["camera"], "angle"), NumberStyles.Float, CultureInfo.InvariantCulture, out var a)) CameraAngle = a;
                    var motor = fb["motorLF"];
                    PartGuess(Text(motor, "id") ?? "", motor?["propeller"] != null ? Text(motor["propeller"], "id") ?? "" : "", Text(fb["actionCam"], "id") ?? "");
                }
                log($"Imported Liftoff drone '{wanted}' from {Path.GetFileName(file)}");
                return;
            }
            log($"No Liftoff drone named '{wanted}' in {folder}; using the JSON values");
        }

        // Liftoff 1.x stores rates as percent; > 200 is taken linearly (no Betaflight >2.0 boost).
        static AxisRates Rates(XmlElement fc, string axis)
        {
            float F(string n) => float.Parse(Text(fc, n) ?? "0", CultureInfo.InvariantCulture) / 100f;
            return new AxisRates(F(axis + "Rate"), F(axis + "Expo"), F(axis + "SuperExpo"));
        }

        void PartGuess(string motor, string prop, string actionCam)
        {
            var m = Regex.Match(motor, @"(\d{2})(\d{2})");
            if (m.Success)
            {
                int stator = int.Parse(m.Groups[1].Value);
                Kv = stator >= 28 ? 1700f : stator >= 22 ? 2450f : stator >= 14 ? 3800f : 8000f;
            }
            var p = Regex.Match(prop, @"(\d)(\d)(\d)(\d)(?:x(\d))?");
            if (p.Success)
            {
                PropDiameter = int.Parse(p.Groups[1].Value) + int.Parse(p.Groups[2].Value) / 10f;
                PropPitch = int.Parse(p.Groups[3].Value) + int.Parse(p.Groups[4].Value) / 10f;
                PropBlades = p.Groups[5].Success ? int.Parse(p.Groups[5].Value) : 3;
            }
            HubOffset = 0.0156f * PropDiameter;
            Mass = 0.58f * M.Pow(PropDiameter / 5f, 2.2f);
            if (actionCam.Length > 0 && !actionCam.ToLowerInvariant().Contains("blank")) Mass += 0.075f;
        }

        static string Text(XmlNode n, string child) => n?[child]?.InnerText;
    }
}
