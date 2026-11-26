using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace ArcBright
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--diag")
            {
                string path = args.Length > 1 && !args[1].StartsWith("--") ? args[1] : Path.Combine(Settings.Folder, "diag.txt");
                Diag.Run(path, !args.Contains("--read-only"));
                return 0;
            }

            bool created;
            using (var mutex = new Mutex(true, @"Local\ArcBright.SingleInstance", out created))
            {
                if (!created)
                {
                    Settings s = Settings.Load();
                    Lang.Init(s);
                    MessageBox.Show(Lang.T("ArcBright is already running. Look for the sun icon next to the clock.",
                        "ArcBright rulează deja. Caută iconița cu soare lângă ceas."), "ArcBright");
                    return 1;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Settings settings = Settings.Load();
                Lang.Init(settings);
                using (var engine = new Engine(settings))
                {
                    // Whatever happens, the screen goes back to normal.
                    Application.ThreadException += (sender, e) =>
                    {
                        engine.RestoreAll();
                        MessageBox.Show(e.Exception.ToString(), "ArcBright");
                    };
                    AppDomain.CurrentDomain.UnhandledException += (sender, e) => engine.RestoreAll();
                    AppDomain.CurrentDomain.ProcessExit += (sender, e) => engine.RestoreAll();
                    Microsoft.Win32.SystemEvents.SessionEnding += (sender, e) => engine.RestoreAll();

                    bool startHidden = args.Contains("--tray");
                    Application.Run(new MainForm(settings, engine, startHidden));
                    engine.RestoreAll();
                    settings.Save();
                }
            }
            return 0;
        }
    }
}
