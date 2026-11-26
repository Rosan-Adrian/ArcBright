using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ArcBright
{
    internal sealed class Settings
    {
        public const int BrightnessMin = -50, BrightnessMax = 50, BrightnessDefault = 0;
        public const int ContrastMin = 50, ContrastMax = 150, ContrastDefault = 100;
        public const int GammaMin = 50, GammaMax = 250, GammaDefault = 100;
        public const int SaturationMin = 0, SaturationMax = 200, SaturationDefault = 100;
        public const string DefaultTargets = "PioneerGame";

        public int Brightness = BrightnessDefault;   // -50..50, added to the output (x / 200)
        public int Contrast = ContrastDefault;       // percent
        public int Gamma = GammaDefault;             // x100; above 100 lifts the shadows
        public int Saturation = SaturationDefault;   // AMD driver units, 100 = normal
        public string Targets = DefaultTargets;      // process names, comma separated
        public bool Enabled = true;
        public bool TrayHintShown;
        public string Language = "";                 // "", "en" or "ro"

        public static string Folder
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ArcBright");
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(Folder, "settings.ini"); }
        }

        public bool IsTargetName(string processName)
        {
            if (string.IsNullOrEmpty(processName))
            {
                return false;
            }
            foreach (string raw in Targets.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string name = raw.Trim();
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - 4);
                }
                if (name.Length > 0 && string.Equals(name, processName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        public void ResetColours()
        {
            Brightness = BrightnessDefault;
            Contrast = ContrastDefault;
            Gamma = GammaDefault;
            Saturation = SaturationDefault;
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                {
                    return s;
                }
                var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
                s.Brightness = ReadInt(values, "Brightness", s.Brightness, BrightnessMin, BrightnessMax);
                s.Contrast = ReadInt(values, "Contrast", s.Contrast, ContrastMin, ContrastMax);
                s.Gamma = ReadInt(values, "Gamma", s.Gamma, GammaMin, GammaMax);
                s.Saturation = ReadInt(values, "Saturation", s.Saturation, SaturationMin, SaturationMax);
                string text;
                if (values.TryGetValue("Targets", out text) && text.Length > 0)
                {
                    s.Targets = text;
                }
                if (values.TryGetValue("Enabled", out text))
                {
                    s.Enabled = text != "0";
                }
                if (values.TryGetValue("TrayHintShown", out text))
                {
                    s.TrayHintShown = text == "1";
                }
                if (values.TryGetValue("Language", out text))
                {
                    s.Language = text;
                }
            }
            catch (Exception)
            {
                // A damaged file falls back to the defaults.
            }
            return s;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Folder);
                var sb = new StringBuilder();
                sb.AppendLine("Brightness=" + Brightness.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Contrast=" + Contrast.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Gamma=" + Gamma.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Saturation=" + Saturation.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("Targets=" + Targets);
                sb.AppendLine("Enabled=" + (Enabled ? "1" : "0"));
                sb.AppendLine("TrayHintShown=" + (TrayHintShown ? "1" : "0"));
                sb.AppendLine("Language=" + Language);
                File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception)
            {
                // Not being able to save is not worth interrupting a game for.
            }
        }

        private static int ReadInt(Dictionary<string, string> values, string key, int fallback, int min, int max)
        {
            string text;
            int value;
            if (values.TryGetValue(key, out text) && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return Math.Max(min, Math.Min(max, value));
            }
            return fallback;
        }
    }
}
