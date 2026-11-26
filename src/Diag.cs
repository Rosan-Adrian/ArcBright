using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ArcBright
{
    // "ArcBright.exe --diag [file]" writes what ArcBright can see and change on this PC.
    // Useful for bug reports. It flashes the screen briefly while it tests.
    internal static class Diag
    {
        public static void Run(string path, bool testChanges)
        {
            var sb = new StringBuilder();
            sb.AppendLine("ArcBright diagnostics " + DateTime.Now.ToString("s"));
            sb.AppendLine("OS: " + Environment.OSVersion + (Environment.Is64BitProcess ? " (64-bit)" : " (32-bit)"));
            sb.AppendLine("GdiIcmGammaRange: " + GammaRangeValue());

            foreach (Screen s in Screen.AllScreens)
            {
                ushort[] ramp = GammaController.GetRamp(s.DeviceName);
                sb.AppendLine(string.Format("screen {0} {1} primary={2} ramp={3}", s.DeviceName, s.Bounds, s.Primary,
                    ramp == null ? "unreadable" : ramp[0] + ".." + ramp[128] + ".." + ramp[255]));
                if (ramp != null && testChanges)
                {
                    sb.AppendLine("  set same ramp: " + GammaController.SetRamp(s.DeviceName, ramp));
                    int[][] tests = { new[] { 0, 110, 150 }, new[] { 10, 110, 200 }, new[] { 50, 150, 250 } };
                    foreach (int[] t in tests)
                    {
                        int accepted = -1;
                        for (int strength = 100; strength > 0; strength -= 10)
                        {
                            if (GammaController.SetRamp(s.DeviceName, GammaController.Compose(ramp, t[0] / 200.0, t[1] / 100.0, t[2] / 100.0, strength / 100.0)))
                            {
                                accepted = strength;
                                break;
                            }
                        }
                        GammaController.SetRamp(s.DeviceName, ramp);
                        sb.AppendLine(string.Format("  brightness {0} contrast {1} gamma {2}: accepted {3}%", t[0], t[1], t[2], accepted));
                    }
                }
            }

            var recovery = new Recovery();
            using (var amd = new AmdColor(recovery))
            {
                sb.AppendLine("AMD: " + (amd.Available ? "available" : "not available (" + amd.Error + ")"));
                if (amd.Available)
                {
                    foreach (string line in amd.Describe())
                    {
                        sb.AppendLine("  " + line);
                    }
                    foreach (Screen s in Screen.AllScreens)
                    {
                        foreach (AmdColor.Target t in amd.FindTargets(s.DeviceName))
                        {
                            int cur, def, min, max;
                            bool ok = amd.Get(t.Adapter, t.Display, out cur, out def, out min, out max);
                            sb.AppendLine(string.Format("  {0} -> adapter {1} display {2} '{3}': saturation get={4} current={5} default={6} range={7}..{8}",
                                s.DeviceName, t.Adapter, t.Display, t.Name, ok, cur, def, min, max));
                            if (ok && testChanges)
                            {
                                int test = cur + 20 <= max ? cur + 20 : cur - 20;
                                bool set = amd.SetRaw(t.Adapter, t.Display, test);
                                Thread.Sleep(300);
                                int after, d2, mn2, mx2;
                                amd.Get(t.Adapter, t.Display, out after, out d2, out mn2, out mx2);
                                bool back = amd.SetRaw(t.Adapter, t.Display, cur);
                                int final, d3, mn3, mx3;
                                amd.Get(t.Adapter, t.Display, out final, out d3, out mn3, out mx3);
                                sb.AppendLine(string.Format("    set {0}: {1}, read back {2}; restore: {3}, now {4}", test, set, after, back, final));
                            }
                        }
                    }
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        public static string GammaRangeValue()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion\ICM"))
                {
                    object v = key == null ? null : key.GetValue("GdiIcmGammaRange");
                    return v == null ? "not set" : v.ToString();
                }
            }
            catch (Exception ex)
            {
                return "unreadable (" + ex.Message + ")";
            }
        }
    }
}
