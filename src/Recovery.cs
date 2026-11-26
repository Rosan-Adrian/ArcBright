using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ArcBright
{
    // Writes the display's original values to disk right before ArcBright changes them,
    // and deletes them once they are put back. If ArcBright is killed while a change is
    // on, the next start finds the files and restores the screen.
    internal sealed class Recovery
    {
        private readonly string folder;

        public Recovery()
        {
            folder = Path.Combine(Settings.Folder, "restore");
        }

        public void SaveGamma(string device, ushort[] ramp)
        {
            string file = GammaFile(device);
            if (File.Exists(file))
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(folder);
                using (var w = new BinaryWriter(File.Create(file)))
                {
                    w.Write(device);
                    foreach (ushort v in ramp)
                    {
                        w.Write(v);
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public void ClearGamma(string device)
        {
            TryDelete(GammaFile(device));
        }

        public void SaveSaturation(int adapter, int display, int value)
        {
            string file = SaturationFile(adapter, display);
            if (File.Exists(file))
            {
                return;
            }
            try
            {
                Directory.CreateDirectory(folder);
                File.WriteAllText(file, string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}", adapter, display, value), Encoding.ASCII);
            }
            catch (Exception)
            {
            }
        }

        public void ClearSaturation(int adapter, int display)
        {
            TryDelete(SaturationFile(adapter, display));
        }

        // Puts back whatever an earlier run left changed. Returns how many values were restored.
        public int RestoreLeftovers(AmdColor amd)
        {
            if (!Directory.Exists(folder))
            {
                return 0;
            }
            int restored = 0;
            foreach (string file in Directory.GetFiles(folder, "gamma_*.bin"))
            {
                try
                {
                    string device;
                    var ramp = new ushort[768];
                    using (var r = new BinaryReader(File.OpenRead(file)))
                    {
                        device = r.ReadString();
                        for (int i = 0; i < ramp.Length; i++)
                        {
                            ramp[i] = r.ReadUInt16();
                        }
                    }
                    if (GammaController.SetRamp(device, ramp))
                    {
                        restored++;
                    }
                }
                catch (Exception)
                {
                }
                TryDelete(file);
            }
            foreach (string file in Directory.GetFiles(folder, "sat_*.txt"))
            {
                try
                {
                    string[] parts = File.ReadAllText(file, Encoding.ASCII).Split(' ');
                    int adapter = int.Parse(parts[0], CultureInfo.InvariantCulture);
                    int display = int.Parse(parts[1], CultureInfo.InvariantCulture);
                    int value = int.Parse(parts[2], CultureInfo.InvariantCulture);
                    if (amd != null && amd.Available && amd.SetRaw(adapter, display, value))
                    {
                        restored++;
                    }
                }
                catch (Exception)
                {
                }
                TryDelete(file);
            }
            return restored;
        }

        private string GammaFile(string device)
        {
            var sb = new StringBuilder("gamma_");
            foreach (char c in device)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            sb.Append(".bin");
            return Path.Combine(folder, sb.ToString());
        }

        private string SaturationFile(int adapter, int display)
        {
            return Path.Combine(folder, string.Format(CultureInfo.InvariantCulture, "sat_{0}_{1}.txt", adapter, display));
        }

        private static void TryDelete(string file)
        {
            try
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
            catch (Exception)
            {
            }
        }
    }
}
