static class HidTest {
  public static void Run() {
    var j = LosSantosFpv.HidJoystick.Open("", s => System.Console.WriteLine(s), true);
    System.Console.WriteLine(j == null ? "no HID joystick found (ok if the radio is unplugged)" : "opened " + j.Name);
    if (j != null) { System.Threading.Thread.Sleep(500); var d = new System.Collections.Generic.Dictionary<string,float>(); j.Snapshot(d, out var b); foreach (var kv in d) System.Console.WriteLine($"  {kv.Key} {kv.Value:+0.00;-0.00}"); System.Console.WriteLine($"  buttons {b:X}"); j.Dispose(); }
  }
}
