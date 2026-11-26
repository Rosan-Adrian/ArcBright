using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ArcBright
{
    // Colour saturation through the AMD display library (atiadlxx.dll, installed with
    // every Radeon driver). It is the same setting as Saturation under Custom Color
    // in AMD Software: Adrenalin Edition.
    internal sealed class AmdColor : IDisposable
    {
        private const int ADL_OK = 0;
        private const int ADL_MAX_PATH = 256;
        private const int ADL_DISPLAY_COLOR_SATURATION = 1 << 2;
        private const int DISPLAY_CONNECTED = 0x1;
        private const int DISPLAY_MAPPED = 0x2;

        [UnmanagedFunctionPointer(CallingConvention.StdCall)]
        private delegate IntPtr MallocCallback(int size);

        // Kept in a static field so the garbage collector never frees it while ADL holds it.
        private static readonly MallocCallback Malloc = size => Marshal.AllocHGlobal(size);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct AdapterInfo
        {
            public int Size;
            public int AdapterIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string UDID;
            public int BusNumber;
            public int DeviceNumber;
            public int FunctionNumber;
            public int VendorID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string AdapterName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string DisplayName;
            public int Present;
            public int Exist;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string DriverPath;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string DriverPathExt;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string PNPString;
            public int OSDisplayIndex;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct DisplayInfo
        {
            public int LogicalIndex;
            public int PhysicalIndex;
            public int LogicalAdapterIndex;
            public int PhysicalAdapterIndex;
            public int ControllerIndex;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string DisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = ADL_MAX_PATH)] public string ManufacturerName;
            public int DisplayType;
            public int OutputType;
            public int Connector;
            public int InfoMask;
            public int InfoValue;
        }

        [DllImport("atiadlxx.dll")]
        private static extern int ADL2_Main_Control_Create(MallocCallback callback, int enumConnectedAdapters, out IntPtr context);

        [DllImport("atiadlxx.dll")]
        private static extern int ADL2_Main_Control_Destroy(IntPtr context);

        [DllImport("atiadlxx.dll")]
        private static extern int ADL2_Adapter_NumberOfAdapters_Get(IntPtr context, out int count);

        [DllImport("atiadlxx.dll")]
        private static extern int ADL2_Adapter_AdapterInfo_Get(IntPtr context, IntPtr info, int inputSize);

        [DllImport("atiadlxx.dll")]
        private static extern int ADL2_Display_DisplayInfo_Get(IntPtr context, int adapterIndex, out int count, out IntPtr info, int forceDetect);

        [DllImport("atiadlxx.dll")]
        private static extern int ADL2_Display_Color_Get(IntPtr context, int adapterIndex, int displayIndex, int colorType,
            out int current, out int defaultValue, out int min, out int max, out int step);

        [DllImport("atiadlxx.dll")]
        private static extern int ADL2_Display_Color_Set(IntPtr context, int adapterIndex, int displayIndex, int colorType, int current);

        public struct Target
        {
            public int Adapter;
            public int Display;
            public string Name;
        }

        private sealed class Saved
        {
            public int Original;
            public int Min;
            public int Max;
        }

        private IntPtr context;
        private AdapterInfo[] adapters = new AdapterInfo[0];
        private readonly Dictionary<string, Saved> saved = new Dictionary<string, Saved>();
        private readonly Dictionary<string, Target> applied = new Dictionary<string, Target>();
        private readonly Recovery recovery;

        public bool Available { get; private set; }
        public string Error { get; private set; }

        public AmdColor(Recovery recovery)
        {
            this.recovery = recovery;
            try
            {
                int rc = ADL2_Main_Control_Create(Malloc, 1, out context);
                if (rc != ADL_OK)
                {
                    Error = "ADL error " + rc;
                    return;
                }
                LoadAdapters();
                Available = adapters.Length > 0;
                if (!Available)
                {
                    Error = "no AMD display";
                }
            }
            catch (DllNotFoundException)
            {
                Error = "no AMD driver";
            }
            catch (EntryPointNotFoundException)
            {
                Error = "AMD driver too old";
            }
            catch (Exception ex)
            {
                Error = ex.Message;
            }
        }

        private void LoadAdapters()
        {
            int count;
            if (ADL2_Adapter_NumberOfAdapters_Get(context, out count) != ADL_OK || count <= 0)
            {
                adapters = new AdapterInfo[0];
                return;
            }
            int size = Marshal.SizeOf(typeof(AdapterInfo));
            IntPtr buffer = Marshal.AllocHGlobal(size * count);
            try
            {
                for (int i = 0; i < size * count; i++)
                {
                    Marshal.WriteByte(buffer, i, 0);
                }
                if (ADL2_Adapter_AdapterInfo_Get(context, buffer, size * count) != ADL_OK)
                {
                    adapters = new AdapterInfo[0];
                    return;
                }
                var list = new List<AdapterInfo>();
                for (int i = 0; i < count; i++)
                {
                    var info = (AdapterInfo)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + (long)i * size), typeof(AdapterInfo));
                    if (info.Present != 0)
                    {
                        list.Add(info);
                    }
                }
                adapters = list.ToArray();
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // The AMD displays that show the given Windows screen, such as \\.\DISPLAY1.
        public List<Target> FindTargets(string device)
        {
            var result = FindTargetsOnce(device);
            if (result.Count == 0 && Available)
            {
                // Screens come and go; read the adapters again before giving up.
                LoadAdapters();
                result = FindTargetsOnce(device);
            }
            return result;
        }

        private List<Target> FindTargetsOnce(string device)
        {
            var result = new List<Target>();
            var seen = new HashSet<string>();
            foreach (AdapterInfo adapter in adapters)
            {
                if (device != null && !string.Equals(adapter.DisplayName, device, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                int count;
                IntPtr buffer;
                if (ADL2_Display_DisplayInfo_Get(context, adapter.AdapterIndex, out count, out buffer, 0) != ADL_OK)
                {
                    continue;
                }
                try
                {
                    int size = Marshal.SizeOf(typeof(DisplayInfo));
                    for (int i = 0; i < count; i++)
                    {
                        var info = (DisplayInfo)Marshal.PtrToStructure(new IntPtr(buffer.ToInt64() + (long)i * size), typeof(DisplayInfo));
                        bool live = (info.InfoValue & (DISPLAY_CONNECTED | DISPLAY_MAPPED)) == (DISPLAY_CONNECTED | DISPLAY_MAPPED);
                        if (!live || info.LogicalAdapterIndex != adapter.AdapterIndex)
                        {
                            continue;
                        }
                        string key = Key(info.LogicalAdapterIndex, info.LogicalIndex);
                        if (seen.Add(key))
                        {
                            result.Add(new Target { Adapter = info.LogicalAdapterIndex, Display = info.LogicalIndex, Name = info.DisplayName });
                        }
                    }
                }
                finally
                {
                    if (buffer != IntPtr.Zero)
                    {
                        Marshal.FreeHGlobal(buffer);
                    }
                }
            }
            return result;
        }

        public bool Get(int adapter, int display, out int current, out int defaultValue, out int min, out int max)
        {
            int step;
            int rc = ADL2_Display_Color_Get(context, adapter, display, ADL_DISPLAY_COLOR_SATURATION, out current, out defaultValue, out min, out max, out step);
            return rc == ADL_OK;
        }

        public bool SetRaw(int adapter, int display, int value)
        {
            return ADL2_Display_Color_Set(context, adapter, display, ADL_DISPLAY_COLOR_SATURATION, value) == ADL_OK;
        }

        // Sets the saturation on every AMD display that shows this screen.
        public bool Apply(string device, int value)
        {
            if (!Available)
            {
                return false;
            }
            bool any = false;
            foreach (Target t in FindTargets(device))
            {
                string key = Key(t.Adapter, t.Display);
                Saved s;
                if (!saved.TryGetValue(key, out s))
                {
                    int cur, def, min, max;
                    if (!Get(t.Adapter, t.Display, out cur, out def, out min, out max))
                    {
                        continue;
                    }
                    s = new Saved { Original = cur, Min = min, Max = max };
                    saved[key] = s;
                }
                int wanted = Math.Max(s.Min, Math.Min(s.Max, value));
                if (wanted == s.Original && !applied.ContainsKey(key))
                {
                    any = true;
                    continue;
                }
                recovery.SaveSaturation(t.Adapter, t.Display, s.Original);
                if (SetRaw(t.Adapter, t.Display, wanted))
                {
                    applied[key] = t;
                    any = true;
                }
            }
            return any;
        }

        public void RestoreAll()
        {
            foreach (KeyValuePair<string, Target> pair in new List<KeyValuePair<string, Target>>(applied))
            {
                Saved s;
                if (saved.TryGetValue(pair.Key, out s))
                {
                    SetRaw(pair.Value.Adapter, pair.Value.Display, s.Original);
                }
                recovery.ClearSaturation(pair.Value.Adapter, pair.Value.Display);
            }
            applied.Clear();
            saved.Clear();
        }

        public IEnumerable<string> Describe()
        {
            foreach (AdapterInfo a in adapters)
            {
                yield return string.Format("adapter {0}: {1} -> {2} (vendor {3:X}, os index {4})", a.AdapterIndex, a.AdapterName, a.DisplayName, a.VendorID, a.OSDisplayIndex);
            }
        }

        private static string Key(int adapter, int display)
        {
            return adapter + ":" + display;
        }

        public void Dispose()
        {
            if (context != IntPtr.Zero)
            {
                try
                {
                    ADL2_Main_Control_Destroy(context);
                }
                catch (Exception)
                {
                }
                context = IntPtr.Zero;
            }
        }
    }
}
