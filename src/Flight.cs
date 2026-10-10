using System;

namespace LosSantosFpv
{
    public struct Vec
    {
        public float X, Y, Z;
        public Vec(float x, float y, float z) { X = x; Y = y; Z = z; }
        public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static Vec operator -(Vec a) => new Vec(-a.X, -a.Y, -a.Z);
        public static Vec operator *(Vec a, float s) => new Vec(a.X * s, a.Y * s, a.Z * s);
        public static float Dot(Vec a, Vec b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;
        public static Vec Cross(Vec a, Vec b) => new Vec(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
        public float Length => (float)Math.Sqrt(X * X + Y * Y + Z * Z);
        public Vec Normalized { get { float l = Length; return l < 1e-9f ? new Vec(0, 0, 1) : this * (1f / l); } }
        public static Vec Lerp(Vec a, Vec b, float t) => a + (b - a) * t;
        /// <summary>Rotate v about unit axis k by angle a (Rodrigues).</summary>
        public static Vec Rotate(Vec v, Vec k, float a)
        {
            float c = (float)Math.Cos(a), s = (float)Math.Sin(a);
            return v * c + Cross(k, v) * s + k * (Dot(k, v) * (1 - c));
        }
        public override string ToString() => $"({X:0.00} {Y:0.00} {Z:0.00})";
    }

    public struct Hit
    {
        public Vec Position, Normal;   // sphere centre at contact, surface normal
        public object Entity;          // host entity that was hit, if any
    }

    /// <summary>
    /// Liftoff-style quad (fpv-drone-port flight core), self-integrated at a fixed 125 Hz in a right-handed metre world
    /// (GTA: X east, Y north, Z up). Body basis R (right), F (nose), U (up). Body rates W = (about R: nose up,
    /// about F: roll right, about U: yaw left), rad/s.
    /// </summary>
    public class Flight
    {
        public const float G = 9.80665f, Step = 1f / 125f, Radius = 0.10f;
        public DroneSetup S;
        public Vec P, V, R = new Vec(1, 0, 0), F = new Vec(0, 1, 0), U = new Vec(0, 0, 1), W;
        public Vec PrevP, PrevF, PrevU;
        public bool Armed, WaitLow = true;
        public float MeanRpm, GroundEffect = 1f;
        public string Status = "";
        public Sticks In;

        /// <summary>Host collision: sweep a sphere from a to b, null if clear.</summary>
        public Func<Vec, Vec, float, Hit?> Sweep;
        /// <summary>Host ray: distance and normal to the first surface along a->b, null if clear.</summary>
        public Func<Vec, Vec, Hit?> Ray;
        /// <summary>Called on a hard impact (entity, impact speed m/s).</summary>
        public Action<object, float> OnImpact;

        float acc, ix, iy, iz, rArm, k0, kS, discMax;
        readonly float[] force = new float[4];
        // LF, RF, RB, LB as (x right, y fwd) signs.
        static readonly float[] hx = { -1, 1, 1, -1 }, hy = { 1, 1, -1, -1 };
        static readonly float[] sPitch = { 1, 1, -1, -1 };   // front minus back -> nose up torque
        static readonly float[] sRoll = { 1, -1, -1, 1 };    // left minus right -> roll right torque
        static readonly float[] sYaw = { 1, -1, 1, -1 };     // only eats headroom; the yaw torque is applied directly

        public Flight(DroneSetup s)
        {
            S = s;
            float w = 2 * s.HubOffset * 0.96f, h = 0.03f;
            ix = s.Mass / 12f * (w * w + h * h);   // pitch (about R)
            iy = s.Mass / 12f * (w * w + h * h);   // roll (about F)
            iz = s.Mass / 12f * (2 * w * w);       // yaw
            rArm = s.HubOffset * 1.41421356f;     // Liftoff divides by the 3D arm: ~71% of roll/pitch is realised
            k0 = 7.47958e-5f * M.Pow(s.PropBlades, 4.70298f);
            kS = 2.525e-11f * s.PropPitch * s.PropDiameter * s.PropDiameter * s.PropDiameter;
            discMax = M.Clamp(1f + 0.5f * (s.PropDiameter / 6f) * (s.PropDiameter / 6f), 1f, 1.5f);
        }

        public void Place(Vec pos, Vec forward)
        {
            P = pos; V = default; W = default;
            F = new Vec(forward.X, forward.Y, 0).Normalized; U = new Vec(0, 0, 1); R = Vec.Cross(F, U);
            PrevP = P; PrevF = F; PrevU = U;
            Armed = false; WaitLow = true; acc = 0; MeanRpm = 0;
        }

        float RpmForThrust(float t) => (float)Math.Sqrt(Math.Max(t - k0, 0f) / kS);

        /// <summary>Advance by a frame; returns the render pose interpolated between physics steps.</summary>
        public void Update(float dt, out Vec pos, out Vec fwd, out Vec up)
        {
            if (!Armed && In.Throttle < 0.05f) { Armed = true; WaitLow = false; }
            acc += M.Clamp(dt, 0f, 0.1f);
            int n = 0;
            while (acc >= Step && n < 12) { PrevP = P; PrevF = F; PrevU = U; DoStep(Step); acc -= Step; n++; }
            if (n == 12) acc = 0;
            float a = M.Clamp(acc / Step, 0f, 1f);
            pos = Vec.Lerp(PrevP, P, a);
            fwd = Vec.Lerp(PrevF, F, a).Normalized;
            up = Vec.Lerp(PrevU, U, a);
            up = (up - fwd * Vec.Dot(up, fwd)).Normalized;
        }

        void DoStep(float h)
        {
            // Ground effect: one ray straight down from the centre, range 4 prop pitches.
            float geRange = 4f * S.PropPitch * 0.0254f;
            GroundEffect = 1f;
            var g = Ray?.Invoke(P, P + new Vec(0, 0, -geRange));
            if (g.HasValue)
            {
                float dist = (g.Value.Position - P).Length;
                float align = M.Clamp(Vec.Dot(U, g.Value.Normal), 0.5f, 1f);
                GroundEffect = Math.Max((discMax + (1f - discMax) * dist / geRange) * align, 1f);
            }

            Vec force3 = new Vec(0, 0, -G * S.Mass) + Aero(V);
            Vec tau = default;
            if (!Armed)
            {
                MeanRpm = 0;
                Status = WaitLow ? "THROTTLE LOW TO ARM" : "DISARMED";
            }
            else
            {
                Status = "";
                const float d2r = (float)(Math.PI / 180);
                // Body targets: nose up, roll right, yaw left.
                float tx = -S.Pitch.DegPerSec(In.Pitch) * d2r, ty = S.Roll.DegPerSec(In.Roll) * d2r, tz = -S.Yaw.DegPerSec(In.Yaw) * d2r;
                // Deadbeat P rate loop (Liftoff "autotune"): torque = I (target - w) / dt.
                float ux = ix * (tx - W.X) / h, uy = iy * (ty - W.Y) / h, uz = iz * (tz - W.Z) / h;
                tau.Z = uz;
                float m = 0;
                for (int i = 0; i < 4; i++)
                {
                    force[i] = (sPitch[i] * ux + sRoll[i] * uy + sYaw[i] * uz) / (4 * rArm);
                    m = Math.Max(m, Math.Abs(force[i]));
                }
                float vAx = Vec.Dot(V, U);
                float tMax = S.Thrust(S.MaxRpm, vAx);
                if (m <= tMax)
                {
                    float rpm = S.IdleFraction * S.MaxRpm + (S.MaxRpm - S.IdleFraction * S.MaxRpm) * M.Clamp(In.Throttle, 0f, 1f);
                    float c = S.Thrust(rpm, vAx);
                    if (c < m) c = m; else if (c > tMax - m) c = tMax - m;   // airmode: attitude first (Unity Clamp order)
                    for (int i = 0; i < 4; i++) force[i] += c;
                }
                float total = 0, rpmSum = 0;
                for (int i = 0; i < 4; i++)
                {
                    float f = force[i] * GroundEffect;
                    total += f;
                    float px = hx[i] * S.HubOffset, py = hy[i] * S.HubOffset;
                    tau.X += py * f;          // p x (0,0,f) = (py f, -px f, 0)
                    tau.Y += -px * f;
                    rpmSum += RpmForThrust(Math.Abs(force[i]));
                }
                MeanRpm = rpmSum / 4;
                force3 += U * total;
            }

            // Rotation in body axes with gyroscopic term, Liftoff angular drag and max rate.
            var Iw = new Vec(ix * W.X, iy * W.Y, iz * W.Z);
            var gyro = Vec.Cross(W, Iw);
            float drag = 1f - 1.6f * h;
            W = new Vec((W.X + (tau.X - gyro.X) / ix * h) * drag, (W.Y + (tau.Y - gyro.Y) / iy * h) * drag, (W.Z + (tau.Z - gyro.Z) / iz * h) * drag);
            float wl = W.Length;
            if (wl > 36f) W = W * (36f / wl);

            V += force3 * (h / S.Mass);
            Vec from = P, to = P + V * h;
            Vec wWorld = R * W.X + F * W.Y + U * W.Z;
            float ang = wWorld.Length * h;
            if (ang > 1e-9f) { var k = wWorld.Normalized; F = Vec.Rotate(F, k, ang); U = Vec.Rotate(U, k, ang); }
            F = F.Normalized; U = (U - F * Vec.Dot(U, F)).Normalized; R = Vec.Cross(F, U);

            // Collide: stop at the contact, keep the tangential velocity (damped).
            var hit = Sweep?.Invoke(from, to, Radius);
            if (hit.HasValue)
            {
                var n = hit.Value.Normal.Normalized;
                to = hit.Value.Position + n * 0.003f;
                float vn = Vec.Dot(V, n);
                if (vn < 0)
                {
                    V = (V - n * vn) * 0.3f;
                    W = W * 0.5f;
                    if (-vn > 5f) OnImpact?.Invoke(hit.Value.Entity, -vn);
                }
            }
            P = to;
        }

        Vec Aero(Vec v)
        {
            float speed = v.Length;
            if (speed < 0.01f) return default;
            Vec vh = v * (1f / speed);
            float area = Math.Abs(Vec.Dot(vh, U)) * 0.040f + Math.Abs(Vec.Dot(vh, R)) * 0.013f + Math.Abs(Vec.Dot(vh, F)) * 0.012f;
            area *= (S.PropDiameter / 5f) * (S.PropDiameter / 5f);
            Vec fBody = vh * -(0.5f * 1.0f * 1.184f * area * speed * speed);
            float Dm = S.PropDiameter * 0.0254f;
            float cosA = M.Clamp(Vec.Dot(-vh, U), -1f, 1f);
            float vPerp = speed * (float)Math.Sqrt(Math.Max(1 - cosA * cosA, 0));
            float J = M.Clamp(speed / Math.Max(MeanRpm / 60f * Dm, 0.001f), 0f, 1f);
            float bladeF = (S.PropBlades - 3) * 0.2f + 3f;
            Vec fRotor = vh * -(0.5f * 1.225f * (Dm / 2 * bladeF) * vPerp * vPerp * (0.024f + (0.008f - 0.024f) * J) * 4);
            return fBody + fRotor;
        }
    }
}
