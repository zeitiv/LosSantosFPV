using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace LosSantosFpv
{
    /// <summary>
    /// Raw HID joystick reader. Unlike winmm (6 axes max) it sees every axis an RC radio exposes, including switches that
    /// EdgeTX/Tango 2 report as extra axes (rx, ry, rz, slider, slider1), and every button. A background thread blocks on
    /// ReadFile and keeps the latest values; Snapshot() copies them out.
    /// </summary>
    public sealed class HidJoystick : IDisposable
    {
        public string Name { get; private set; } = "";
        public bool Connected => handle != null && !handle.IsInvalid && !dead;

        SafeFileHandle handle;
        IntPtr pp;
        volatile bool dead;
        Thread thread;
        int reportLen;
        readonly List<Axis> axes = new List<Axis>();
        readonly object gate = new object();
        readonly Dictionary<string, float> values = new Dictionary<string, float>();
        ulong buttons;

        class Axis { public ushort Page, Usage; public int Min, Max, Bits; public string Name; }

        public static HidJoystick Open(string wanted, Action<string> log, bool logAll)
        {
            HidD_GetHidGuid(out Guid guid);
            IntPtr set = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, 0x12 /* PRESENT | DEVICEINTERFACE */);
            if (set == new IntPtr(-1)) return null;
            HidJoystick best = null;
            try
            {
                for (uint i = 0; ; i++)
                {
                    var did = new SP_DEVICE_INTERFACE_DATA { cbSize = Marshal.SizeOf<SP_DEVICE_INTERFACE_DATA>() };
                    if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref did)) break;
                    string path = DevicePath(set, ref did);
                    if (path == null) continue;
                    string lp = path.ToLowerInvariant();
                    if (lp.Contains("ig_") || lp.Contains("vid_045e")) continue;   // Xbox pads: read through XInput

                    using (var probe = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero))
                    {
                        if (probe.IsInvalid || !HidD_GetPreparsedData(probe, out IntPtr ppx)) continue;
                        HidP_GetCaps(ppx, out HIDP_CAPS caps);
                        HidD_FreePreparsedData(ppx);
                        if (caps.UsagePage != 1 || (caps.Usage != 4 && caps.Usage != 5)) continue;
                        var sb = new StringBuilder(256);
                        string name = HidD_GetProductString(probe, sb, sb.Capacity * 2) ? sb.ToString() : "HID joystick";
                        if (logAll) log($"  HID joystick: '{name}'");
                        if (best != null && !(wanted.Length > 0 && name.ToLowerInvariant().Contains(wanted))) continue;
                        var j = new HidJoystick();
                        if (j.Start(path, name, log)) { best?.Dispose(); best = j; }
                    }
                }
            }
            finally { SetupDiDestroyDeviceInfoList(set); }
            return best;
        }

        bool Start(string path, string name, Action<string> log)
        {
            Name = name;
            handle = CreateFile(path, 0x80000000 /* GENERIC_READ */, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
            if (handle.IsInvalid || !HidD_GetPreparsedData(handle, out pp)) return false;
            HidP_GetCaps(pp, out HIDP_CAPS caps);
            reportLen = caps.InputReportByteLength;

            ushort n = caps.NumberInputValueCaps;
            var buf = new byte[72 * Math.Max((int)n, 1)];
            var gh = GCHandle.Alloc(buf, GCHandleType.Pinned);
            try { HidP_GetValueCaps(0, gh.AddrOfPinnedObject(), ref n, pp); } finally { gh.Free(); }
            var sliders = 0;
            for (int i = 0; i < n; i++)
            {
                int o = i * 72;
                ushort page = BitConverter.ToUInt16(buf, o);
                bool isRange = buf[o + 12] != 0;
                ushort bits = BitConverter.ToUInt16(buf, o + 18);
                int min = BitConverter.ToInt32(buf, o + 40), max = BitConverter.ToInt32(buf, o + 44);
                ushort u0 = BitConverter.ToUInt16(buf, o + 56), u1 = isRange ? BitConverter.ToUInt16(buf, o + 58) : u0;
                for (int u = u0; u <= u1; u++)
                {
                    string nm = AxisName(page, (ushort)u, ref sliders);
                    if (nm == null) continue;
                    axes.Add(new Axis { Page = page, Usage = (ushort)u, Min = min, Max = max, Bits = bits, Name = nm });
                }
            }
            log($"Radio (raw HID): '{name}' axes: {string.Join(", ", axes.ConvertAll(a => a.Name))}");
            thread = new Thread(ReadLoop) { IsBackground = true, Name = "LosSantosFpv HID" };
            thread.Start();
            return true;
        }

        static string AxisName(ushort page, ushort usage, ref int sliders)
        {
            if (page == 1)
                switch (usage)
                {
                    case 0x30: return "x"; case 0x31: return "y"; case 0x32: return "z";
                    case 0x33: return "rx"; case 0x34: return "ry"; case 0x35: return "rz";
                    case 0x36: return sliders++ == 0 ? "slider" : "slider" + (sliders - 1);
                    case 0x37: return "dial"; case 0x38: return "wheel";
                    case 0x39: return null;    // hat switch
                }
            if (page == 2) return "sim" + usage.ToString("x2");   // simulation controls (rudder, throttle...)
            return null;
        }

        void ReadLoop()
        {
            var report = new byte[reportLen];
            var usages = new ushort[128];
            while (!dead)
            {
                if (!ReadFile(handle, report, reportLen, out int got, IntPtr.Zero) || got <= 0) { dead = true; break; }
                lock (gate)
                {
                    foreach (var a in axes)
                    {
                        if (HidP_GetUsageValue(0, a.Page, 0, a.Usage, out uint raw, pp, report, (uint)reportLen) != 0x00110000) continue;
                        long v = raw;
                        if (a.Min < 0 && a.Bits > 0 && a.Bits < 32 && (raw & (1u << (a.Bits - 1))) != 0) v = (long)raw - (1L << a.Bits);
                        float span = a.Max - a.Min;
                        values[a.Name] = span > 0 ? M.Clamp((v - a.Min) / span * 2f - 1f, -1f, 1f) : 0f;
                    }
                    uint len = (uint)usages.Length;
                    if (HidP_GetUsages(0, 9, 0, usages, ref len, pp, report, (uint)reportLen) == 0x00110000)
                    {
                        ulong b = 0;
                        for (int i = 0; i < len; i++) if (usages[i] >= 1 && usages[i] <= 64) b |= 1UL << (usages[i] - 1);
                        buttons = b;
                    }
                }
            }
        }

        public void Snapshot(Dictionary<string, float> into, out ulong pressed)
        {
            lock (gate)
            {
                foreach (var kv in values) into[kv.Key] = kv.Value;
                pressed = buttons;
            }
        }

        public void Dispose()
        {
            dead = true;
            handle?.Dispose();   // unblocks ReadFile
            if (pp != IntPtr.Zero) { HidD_FreePreparsedData(pp); pp = IntPtr.Zero; }
        }

        static string DevicePath(IntPtr set, ref SP_DEVICE_INTERFACE_DATA did)
        {
            SetupDiGetDeviceInterfaceDetail(set, ref did, IntPtr.Zero, 0, out int size, IntPtr.Zero);
            if (size <= 0) return null;
            IntPtr mem = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.WriteInt32(mem, IntPtr.Size == 8 ? 8 : 6);   // SP_DEVICE_INTERFACE_DETAIL_DATA_W.cbSize
                if (!SetupDiGetDeviceInterfaceDetail(set, ref did, mem, size, out _, IntPtr.Zero)) return null;
                return Marshal.PtrToStringUni(mem + 4);
            }
            finally { Marshal.FreeHGlobal(mem); }
        }

        // ---------------- interop ----------------

        [StructLayout(LayoutKind.Sequential)]
        struct SP_DEVICE_INTERFACE_DATA { public int cbSize; public Guid InterfaceClassGuid; public int Flags; public IntPtr Reserved; }

        [StructLayout(LayoutKind.Sequential)]
        struct HIDP_CAPS
        {
            public ushort Usage, UsagePage, InputReportByteLength, OutputReportByteLength, FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes, NumberInputButtonCaps, NumberInputValueCaps, NumberInputDataIndices,
                          NumberOutputButtonCaps, NumberOutputValueCaps, NumberOutputDataIndices,
                          NumberFeatureButtonCaps, NumberFeatureValueCaps, NumberFeatureDataIndices;
        }

        [DllImport("hid.dll")] static extern void HidD_GetHidGuid(out Guid g);
        [DllImport("hid.dll", SetLastError = true)] static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr pp);
        [DllImport("hid.dll")] static extern bool HidD_FreePreparsedData(IntPtr pp);
        [DllImport("hid.dll", CharSet = CharSet.Unicode)] static extern bool HidD_GetProductString(SafeFileHandle h, StringBuilder sb, int bytes);
        [DllImport("hid.dll")] static extern int HidP_GetCaps(IntPtr pp, out HIDP_CAPS caps);
        [DllImport("hid.dll")] static extern int HidP_GetValueCaps(int type, IntPtr caps, ref ushort len, IntPtr pp);
        [DllImport("hid.dll")] static extern int HidP_GetUsageValue(int type, ushort page, ushort link, ushort usage, out uint value, IntPtr pp, byte[] report, uint len);
        [DllImport("hid.dll")] static extern int HidP_GetUsages(int type, ushort page, ushort link, [In, Out] ushort[] usages, ref uint len, IntPtr pp, byte[] report, uint reportLen);
        [DllImport("setupapi.dll", SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, IntPtr e, IntPtr hwnd, int flags);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid g, uint i, ref SP_DEVICE_INTERFACE_DATA d);
        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref SP_DEVICE_INTERFACE_DATA d, IntPtr detail, int size, out int required, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr sa, uint disp, uint flags, IntPtr tmpl);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool ReadFile(SafeFileHandle h, byte[] buf, int len, out int read, IntPtr ov);
    }
}
