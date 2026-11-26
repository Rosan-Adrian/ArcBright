using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Forms;

namespace ArcBright
{
    internal enum EngineState
    {
        Off,
        Waiting,
        Active,
        Preview
    }

    // Decides, every time the window in front changes, whether the colours are on.
    internal sealed class Engine : IDisposable
    {
        private readonly Settings settings;
        private readonly Recovery recovery = new Recovery();
        private readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;
        private string activeDevice;
        private string appliedKey;
        private uint lastPid;
        private string lastName;

        public readonly GammaController Gamma;
        public readonly AmdColor Amd;

        public bool Preview;
        public IntPtr PreviewWindow;

        public EngineState State { get; private set; }
        public int GammaStrength { get; private set; }   // 100 = all, 1..99 = limited, -1 = refused
        public string ForegroundName { get; private set; }

        public Engine(Settings settings)
        {
            this.settings = settings;
            Gamma = new GammaController(recovery);
            Amd = new AmdColor(recovery);
            recovery.RestoreLeftovers(Amd);
            GammaStrength = 100;
        }

        public void Evaluate()
        {
            IntPtr front = Native.GetForegroundWindow();
            string device = null;
            EngineState state;

            if (settings.Enabled && front != IntPtr.Zero && IsTarget(front))
            {
                device = Screen.FromHandle(front).DeviceName;
                state = EngineState.Active;
            }
            else if (Preview && PreviewWindow != IntPtr.Zero)
            {
                device = Screen.FromHandle(PreviewWindow).DeviceName;
                state = EngineState.Preview;
            }
            else
            {
                state = settings.Enabled ? EngineState.Waiting : EngineState.Off;
            }

            State = state;
            if (device == null)
            {
                RestoreAll();
                return;
            }
            if (activeDevice != null && !string.Equals(activeDevice, device, StringComparison.OrdinalIgnoreCase))
            {
                RestoreAll();
            }

            string key = string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2}|{3}|{4}",
                device, settings.Brightness, settings.Contrast, settings.Gamma, settings.Saturation);
            if (key != appliedKey || !Gamma.IsStillApplied(device))
            {
                GammaStrength = Gamma.Apply(device, settings.Brightness / 200.0, settings.Contrast / 100.0, settings.Gamma / 100.0);
                if (Amd.Available)
                {
                    Amd.Apply(device, settings.Saturation);
                }
                appliedKey = key;
            }
            activeDevice = device;
        }

        public void RestoreAll()
        {
            if (activeDevice == null && appliedKey == null)
            {
                return;
            }
            Gamma.RestoreAll();
            if (Amd.Available)
            {
                Amd.RestoreAll();
            }
            activeDevice = null;
            appliedKey = null;
        }

        // Forget the cached process name, for example after the target list changed.
        public void Invalidate()
        {
            lastPid = 0;
            lastName = null;
            appliedKey = null;
        }

        private bool IsTarget(IntPtr window)
        {
            uint pid;
            Native.GetWindowThreadProcessId(window, out pid);
            if (pid == 0 || pid == ownPid)
            {
                ForegroundName = null;
                return false;
            }
            string name = pid == lastPid ? lastName : null;
            if (name == null)
            {
                name = ProcessName(pid);
                if (name != null)
                {
                    lastPid = pid;
                    lastName = name;
                }
            }
            ForegroundName = name;
            return settings.IsTargetName(name);
        }

        // Reads the name from the system's process list; no handle to the game is opened.
        public static string ProcessName(uint pid)
        {
            try
            {
                using (Process p = Process.GetProcessById((int)pid))
                {
                    return p.ProcessName;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void Dispose()
        {
            RestoreAll();
            Amd.Dispose();
        }
    }
}
