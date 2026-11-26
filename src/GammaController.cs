using System;
using System.Collections.Generic;

namespace ArcBright
{
    // Brightness, contrast and gamma through the Windows GDI gamma ramp.
    // Works on every GPU (AMD, NVIDIA, Intel), in fullscreen and borderless.
    internal sealed class GammaController
    {
        private readonly Dictionary<string, ushort[]> originals = new Dictionary<string, ushort[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ushort[]> applied = new Dictionary<string, ushort[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Recovery recovery;

        public GammaController(Recovery recovery)
        {
            this.recovery = recovery;
        }

        // Applies the curve on top of the display's own ramp. Returns the share of the
        // requested change Windows accepted (100 = all of it), or -1 if it refused all of it.
        public int Apply(string device, double brightness, double contrast, double gamma)
        {
            bool identity = Math.Abs(brightness) < 1e-9 && Math.Abs(contrast - 1) < 1e-9 && Math.Abs(gamma - 1) < 1e-9;
            if (identity)
            {
                Restore(device);
                return 100;
            }

            ushort[] original;
            if (!originals.TryGetValue(device, out original))
            {
                original = GetRamp(device);
                if (original == null)
                {
                    return -1;
                }
                originals[device] = original;
            }
            recovery.SaveGamma(device, original);

            // Without the GdiIcmGammaRange registry value Windows refuses strong curves,
            // so step back until it takes one.
            for (int strength = 100; strength > 0; strength -= 10)
            {
                ushort[] ramp = Compose(original, brightness, contrast, gamma, strength / 100.0);
                if (SetRamp(device, ramp))
                {
                    applied[device] = ramp;
                    return strength;
                }
            }
            Restore(device);
            return -1;
        }

        // True when nothing of ours should be on this display, or ours is still on it.
        // Some games and Windows itself reset the ramp, for example after a mode change.
        public bool IsStillApplied(string device)
        {
            ushort[] mine;
            if (!applied.TryGetValue(device, out mine))
            {
                return true;
            }
            ushort[] now = GetRamp(device);
            if (now == null)
            {
                return true;
            }
            for (int i = 0; i < now.Length; i++)
            {
                if (Math.Abs(now[i] - mine[i]) > 256)
                {
                    return false;
                }
            }
            return true;
        }

        public void Restore(string device)
        {
            ushort[] original;
            if (applied.ContainsKey(device) && originals.TryGetValue(device, out original))
            {
                SetRamp(device, original);
            }
            applied.Remove(device);
            originals.Remove(device);
            recovery.ClearGamma(device);
        }

        public void RestoreAll()
        {
            foreach (string device in new List<string>(applied.Keys))
            {
                Restore(device);
            }
        }

        public static ushort[] Compose(ushort[] original, double brightness, double contrast, double gamma, double strength)
        {
            var ramp = new ushort[768];
            for (int i = 0; i < 256; i++)
            {
                double x = i / 255.0;
                double y = Math.Pow(x, 1.0 / gamma);
                y = (y - 0.5) * contrast + 0.5 + brightness;
                y = Math.Max(0.0, Math.Min(1.0, y));
                y = x + (y - x) * strength;

                // Run the new curve through the display's own ramp, so a calibration stays.
                double pos = y * 255.0;
                int lo = Math.Min(254, (int)Math.Floor(pos));
                double t = pos - lo;
                for (int ch = 0; ch < 3; ch++)
                {
                    double v = original[ch * 256 + lo] * (1 - t) + original[ch * 256 + lo + 1] * t;
                    ramp[ch * 256 + i] = (ushort)Math.Max(0, Math.Min(65535, Math.Round(v)));
                }
            }
            return ramp;
        }

        public static ushort[] GetRamp(string device)
        {
            IntPtr dc = Native.CreateDC(device, null, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                var ramp = new ushort[768];
                return Native.GetDeviceGammaRamp(dc, ramp) ? ramp : null;
            }
            finally
            {
                Native.DeleteDC(dc);
            }
        }

        public static bool SetRamp(string device, ushort[] ramp)
        {
            IntPtr dc = Native.CreateDC(device, null, null, IntPtr.Zero);
            if (dc == IntPtr.Zero)
            {
                return false;
            }
            try
            {
                return Native.SetDeviceGammaRamp(dc, ramp);
            }
            finally
            {
                Native.DeleteDC(dc);
            }
        }
    }
}
