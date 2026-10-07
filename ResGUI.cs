// ResGUI - always-on-top system resource overlay for Windows.
// Build with build.bat (uses the C# compiler that ships with Windows, C# 5 syntax).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using Path = System.Windows.Shapes.Path;
using Timer = System.Threading.Timer;
using WF = System.Windows.Forms;
using SD = System.Drawing;

[assembly: AssemblyTitle("ResGUI")]
[assembly: AssemblyDescription("Always-on-top system resource overlay for Windows")]
[assembly: AssemblyProduct("ResGUI")]
[assembly: AssemblyCopyright("Copyright (c) 2026 Lampost-123. MIT License.")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace ResGUI
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        // SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION, one per logical processor
        [StructLayout(LayoutKind.Sequential)]
        public struct PROCESSOR_PERF
        {
            public long Idle;
            public long Kernel; // includes idle time
            public long User;
            public long Dpc;
            public long Interrupt;
            public uint InterruptCount;
        }

        public const int SystemProcessorPerformanceInformation = 8;

        [DllImport("ntdll.dll")]
        public static extern int NtQuerySystemInformation(int infoClass, IntPtr buffer, int length, out int returned);

        [DllImport("kernel32.dll")]
        public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);

        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);

        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int index);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hWnd, int index, int value);

        [DllImport("user32.dll")]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hWnd, int attribute, ref int value, int size);

        public const int GWL_EXSTYLE = -20;
        public const int WS_EX_LAYERED = 0x80000;
        public const int WM_STYLECHANGING = 0x007C;

        public delegate IntPtr SubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

        [DllImport("comctl32.dll")]
        public static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, UIntPtr id, UIntPtr data);

        [DllImport("comctl32.dll")]
        public static extern IntPtr DefSubclassProc(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct STYLESTRUCT { public int StyleOld; public int StyleNew; }
        public const uint LWA_ALPHA = 0x2;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWCP_ROUND = 2, DWMWA_BORDER_COLOR = 34;
        public const int WS_EX_TRANSPARENT = 0x20;
        public const int WS_EX_TOOLWINDOW = 0x80;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;
        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;
    }

    // NVIDIA Management Library, shipped with the NVIDIA driver (System32\nvml.dll).
    static class Nvml
    {
        const string Dll = "nvml.dll";
        public const int ClockGraphics = 0, ClockMemory = 2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Utilization { public uint Gpu; public uint Memory; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Memory { public ulong Total; public ulong Free; public ulong Used; }

        [DllImport(Dll, EntryPoint = "nvmlInit_v2")]
        public static extern int Init();

        [DllImport(Dll, EntryPoint = "nvmlShutdown")]
        public static extern int Shutdown();

        [DllImport(Dll, EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
        public static extern int GetHandle(uint index, out IntPtr device);

        [DllImport(Dll, EntryPoint = "nvmlDeviceGetName", CharSet = CharSet.Ansi)]
        public static extern int GetName(IntPtr device, StringBuilder name, uint length);

        [DllImport(Dll, EntryPoint = "nvmlDeviceGetUtilizationRates")]
        public static extern int GetUtilization(IntPtr device, out Utilization utilization);

        [DllImport(Dll, EntryPoint = "nvmlDeviceGetMemoryInfo")]
        public static extern int GetMemory(IntPtr device, out Memory memory);

        [DllImport(Dll, EntryPoint = "nvmlDeviceGetPowerUsage")]
        public static extern int GetPower(IntPtr device, out uint milliwatts);

        [DllImport(Dll, EntryPoint = "nvmlDeviceGetEnforcedPowerLimit")]
        public static extern int GetPowerLimit(IntPtr device, out uint milliwatts);

        [DllImport(Dll, EntryPoint = "nvmlDeviceGetTemperature")]
        public static extern int GetTemperature(IntPtr device, int sensor, out uint celsius);

        [DllImport(Dll, EntryPoint = "nvmlDeviceGetClockInfo")]
        public static extern int GetClock(IntPtr device, int type, out uint mhz);

        [DllImport(Dll, EntryPoint = "nvmlDeviceGetFanSpeed")]
        public static extern int GetFan(IntPtr device, out uint percent);
    }

    class GpuSample
    {
        public uint Util, MemLoad;
        public ulong VramUsed, VramTotal;
        public double Power, PowerLimit;
        public uint Temp, CoreMhz, MemMhz;
        public int Fan = -1;
    }

    class Gpu
    {
        IntPtr handle;
        double powerLimit;
        public bool Ok;
        public string Name = "System";

        public Gpu()
        {
            try
            {
                if (Nvml.Init() != 0 || Nvml.GetHandle(0, out handle) != 0) return;
                var sb = new StringBuilder(96);
                if (Nvml.GetName(handle, sb, 96) == 0)
                    Name = sb.ToString().Replace("NVIDIA ", "").Replace("GeForce ", "");
                uint mw;
                if (Nvml.GetPowerLimit(handle, out mw) == 0) powerLimit = mw / 1000.0;
                Ok = true;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        public GpuSample Read()
        {
            if (!Ok) return null;
            Nvml.Utilization u;
            Nvml.Memory m;
            uint mw, temp, core, mem, fan;
            if (Nvml.GetUtilization(handle, out u) != 0 || Nvml.GetMemory(handle, out m) != 0) return null;
            if (Nvml.GetPower(handle, out mw) != 0) mw = 0;
            if (Nvml.GetTemperature(handle, 0, out temp) != 0) temp = 0;
            if (Nvml.GetClock(handle, Nvml.ClockGraphics, out core) != 0) core = 0;
            if (Nvml.GetClock(handle, Nvml.ClockMemory, out mem) != 0) mem = 0;
            var sample = new GpuSample
            {
                Util = u.Gpu,
                MemLoad = u.Memory,
                VramUsed = m.Used,
                VramTotal = m.Total,
                Power = mw / 1000.0,
                PowerLimit = powerLimit,
                Temp = temp,
                CoreMhz = core,
                MemMhz = mem
            };
            if (Nvml.GetFan(handle, out fan) == 0) sample.Fan = (int)fan;
            return sample;
        }
    }

    class Cpu
    {
        readonly int stride = Marshal.SizeOf(typeof(Native.PROCESSOR_PERF));
        readonly int capacity = Environment.ProcessorCount;
        readonly IntPtr buffer;
        readonly long[] lastIdle, lastTotal;
        PerformanceCounter performance;
        double baseMhz;
        volatile bool clockReady;

        public double Total;
        public double[] Cores = new double[0];

        public Cpu()
        {
            buffer = Marshal.AllocHGlobal(stride * capacity);
            lastIdle = new long[capacity];
            lastTotal = new long[capacity];
            Sample();
            // Perf counters can take a moment to open; keep that off the UI thread.
            ThreadPool.QueueUserWorkItem(delegate { InitClock(); });
        }

        void InitClock()
        {
            try
            {
                using (var freq = new PerformanceCounter("Processor Information", "Processor Frequency", "_Total"))
                    baseMhz = freq.NextValue();
                performance = new PerformanceCounter("Processor Information", "% Processor Performance", "_Total");
                performance.NextValue();
                clockReady = baseMhz > 0;
            }
            catch (Exception) { } // counters disabled, or localized counter names
        }

        // Effective clock, the same figure Task Manager shows as "Speed".
        public double ClockMhz()
        {
            if (!clockReady) return 0;
            try { return performance.NextValue() / 100.0 * baseMhz; }
            catch (Exception) { return 0; }
        }

        // Busy fraction per logical core and overall since the previous call.
        public void Sample()
        {
            int returned;
            if (Native.NtQuerySystemInformation(Native.SystemProcessorPerformanceInformation, buffer, stride * capacity, out returned) != 0) return;
            int count = Math.Min(capacity, returned / stride);
            var cores = new double[count];
            long idleSum = 0, totalSum = 0;
            for (int i = 0; i < count; i++)
            {
                var p = (Native.PROCESSOR_PERF)Marshal.PtrToStructure(buffer + i * stride, typeof(Native.PROCESSOR_PERF));
                long total = p.Kernel + p.User;
                long dIdle = p.Idle - lastIdle[i], dTotal = total - lastTotal[i];
                lastIdle[i] = p.Idle;
                lastTotal[i] = total;
                cores[i] = dTotal > 0 ? Clamp(1.0 - (double)dIdle / dTotal) : 0;
                idleSum += dIdle;
                totalSum += dTotal;
            }
            Cores = cores;
            Total = totalSum > 0 ? Clamp(1.0 - (double)idleSum / totalSum) : 0;
        }

        static double Clamp(double v) { return Math.Max(0, Math.Min(1, v)); }
    }

    // Windows has no driver-free API for CPU die temperature. LibreHardwareMonitor reads it through
    // its PawnIO driver; with Options > Remote Web Server > Run enabled it serves all sensors as JSON.
    class CpuTemp
    {
        static readonly Regex Number = new Regex(@"-?\d+(?:[.,]\d+)?");
        readonly string url;
        volatile float celsius = float.NaN;
        int busy;
        readonly Timer timer;

        public CpuTemp(int port)
        {
            url = "http://127.0.0.1:" + port + "/data.json";
            timer = new Timer(delegate { Poll(); }, null, 0, 2000);
        }

        public float Celsius { get { return celsius; } }

        void Poll()
        {
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            try { celsius = Query(); }
            finally { Interlocked.Exchange(ref busy, 0); }
        }

        float Query()
        {
            try
            {
                var request = (HttpWebRequest)WebRequest.Create(url);
                request.Timeout = 1500;
                request.Proxy = null;
                string json;
                using (var response = request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                    json = reader.ReadToEnd();
                var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json);
                float best = float.NaN;
                int bestRank = int.MaxValue;
                Walk(root, false, false, ref best, ref bestRank);
                return best;
            }
            catch (WebException) { return float.NaN; }
            catch (ArgumentException) { return float.NaN; }
            catch (InvalidOperationException) { return float.NaN; }
        }

        // Node fields: Text, Value ("45.3 °C"), ImageURL, Children; newer builds add SensorId and Type.
        static void Walk(object node, bool inCpu, bool inTemps, ref float best, ref int bestRank)
        {
            var map = node as Dictionary<string, object>;
            if (map == null) return;
            string text = Field(map, "Text"), image = Field(map, "ImageURL"), id = Field(map, "SensorId"), type = Field(map, "Type");
            inCpu = inCpu || image.Contains("cpu");
            bool isCpuTemp = id.Length > 0
                ? (id.StartsWith("/amdcpu") || id.StartsWith("/intelcpu")) && (type == "Temperature" || id.Contains("/temperature/"))
                : inCpu && inTemps;
            object children;
            var list = map.TryGetValue("Children", out children) ? children as object[] : null;
            if (isCpuTemp && (list == null || list.Length == 0))
            {
                var m = Number.Match(Field(map, "Value"));
                float value;
                if (m.Success && float.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                {
                    int rank = text.Contains("Tctl") || text.Contains("Tdie") ? 0 : text.Contains("Package") ? 1 : text.Contains("Max") ? 2 : 3;
                    if (rank < bestRank)
                    {
                        bestRank = rank;
                        best = value;
                    }
                }
            }
            if (list == null) return;
            foreach (var child in list) Walk(child, inCpu, text == "Temperatures", ref best, ref bestRank);
        }

        static string Field(Dictionary<string, object> map, string key)
        {
            object v;
            return map.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : "";
        }
    }

    class Net
    {
        NetworkInterface[] adapters = new NetworkInterface[0];
        DateTime nextScan = DateTime.MinValue;
        readonly Stopwatch clock = Stopwatch.StartNew();
        long lastRx = -1, lastTx;
        double lastTime;

        public double Down, Up; // bytes per second

        public void Sample()
        {
            bool rescanned = false;
            if (DateTime.UtcNow >= nextScan)
            {
                adapters = Scan();
                nextScan = DateTime.UtcNow.AddSeconds(30);
                rescanned = true;
            }
            long rx = 0, tx = 0;
            foreach (var adapter in adapters)
            {
                try
                {
                    var stats = adapter.GetIPStatistics();
                    rx += stats.BytesReceived;
                    tx += stats.BytesSent;
                }
                catch (NetworkInformationException) { }
            }
            double now = clock.Elapsed.TotalSeconds;
            // After a rescan the adapter set may differ, so skip one delta rather than report a spike.
            if (!rescanned && lastRx >= 0 && now > lastTime)
            {
                Down = Math.Max(0, (rx - lastRx) / (now - lastTime));
                Up = Math.Max(0, (tx - lastTx) / (now - lastTime));
            }
            lastRx = rx;
            lastTx = tx;
            lastTime = now;
        }

        // Adapters with a default gateway: the ones carrying real traffic, not virtual switches or loopback.
        static NetworkInterface[] Scan()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces().Where(n =>
                    n.OperationalStatus == OperationalStatus.Up &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                    n.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
                    n.GetIPProperties().GatewayAddresses.Any(g =>
                        !g.Address.Equals(IPAddress.Any) && !g.Address.Equals(IPAddress.IPv6Any))).ToArray();
            }
            catch (NetworkInformationException) { return new NetworkInterface[0]; }
        }
    }

    // key=value file in %APPDATA%\ResGUI\settings.ini
    class Settings
    {
        public readonly string Path;
        readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Settings()
        {
            string dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ResGUI");
            Directory.CreateDirectory(dir);
            Path = System.IO.Path.Combine(dir, "settings.ini");
            if (!File.Exists(Path)) return;
            foreach (var line in File.ReadAllLines(Path))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0 || line.TrimStart().StartsWith("#")) continue;
                values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
            }
        }

        public string Get(string key, string fallback)
        {
            string v;
            return values.TryGetValue(key, out v) && v.Length > 0 ? v : fallback;
        }

        public double GetDouble(string key, double fallback)
        {
            double d;
            return double.TryParse(Get(key, ""), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : fallback;
        }

        public void Set(string key, object value)
        {
            values[key] = Convert.ToString(value, CultureInfo.InvariantCulture);
            Save();
        }

        public void Save()
        {
            var lines = new List<string> { "# ResGUI settings. Restart ResGUI after editing.", "# Hotkey example: Ctrl+Alt+R, Ctrl+Shift+F10, Win+Alt+H" };
            foreach (var kv in values) lines.Add(kv.Key + "=" + kv.Value);
            try { File.WriteAllLines(Path, lines); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    static class Theme
    {
        public static readonly Color Green = Color.FromRgb(0x34, 0xD3, 0x99);
        public static readonly Color Amber = Color.FromRgb(0xFB, 0xBF, 0x24);
        public static readonly Color Red = Color.FromRgb(0xF8, 0x71, 0x71);
        public static readonly Color Blue = Color.FromRgb(0x60, 0xA5, 0xFA);

        public static SolidColorBrush Brush(uint argb)
        {
            var b = new SolidColorBrush(Color.FromArgb((byte)(argb >> 24), (byte)(argb >> 16), (byte)(argb >> 8), (byte)argb));
            b.Freeze();
            return b;
        }

        public static readonly SolidColorBrush Track = Brush(0x17FFFFFF);
        public static readonly SolidColorBrush Text = Brush(0xFFF4F4F5);
        public static readonly SolidColorBrush Label = Brush(0xFFA1A1AA);
        public static readonly SolidColorBrush Muted = Brush(0xFF71717A);
        public static readonly SolidColorBrush Icon = Brush(0xFF8A8A93);
        public static readonly SolidColorBrush IconHover = Brush(0xFFF4F4F5);
        public static readonly SolidColorBrush GreenBrush = Solid(Green);
        public static readonly SolidColorBrush BlueBrush = Solid(Blue);
        public static readonly Brush Panel = Vertical(Color.FromArgb(0xF5, 0x22, 0x22, 0x29), Color.FromArgb(0xF5, 0x14, 0x14, 0x18));
        public static readonly Brush Edge = Vertical(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x0A, 0xFF, 0xFF, 0xFF));
        public static readonly FontFamily UiFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily NumberFont = new FontFamily("Segoe UI Variable Display, Segoe UI");
        static readonly SolidColorBrush[] LoadPalette = BuildPalette();

        static SolidColorBrush Solid(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        static SolidColorBrush[] BuildPalette()
        {
            var p = new SolidColorBrush[101];
            for (int i = 0; i <= 100; i++) p[i] = Solid(ForLoad(i / 100.0));
            return p;
        }

        public static Brush Vertical(Color top, Color bottom)
        {
            var b = new LinearGradientBrush(top, bottom, new Point(0, 0), new Point(0, 1));
            b.Freeze();
            return b;
        }

        public static Brush Fade(Color c, byte alpha)
        {
            return Vertical(Color.FromArgb(alpha, c.R, c.G, c.B), Color.FromArgb(0, c.R, c.G, c.B));
        }

        public static Pen Line(Color c, double thickness)
        {
            var pen = new Pen(Solid(c), thickness) { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            return pen;
        }

        public static double Clamp01(double v) { return v < 0 ? 0 : v > 1 ? 1 : v; }

        static Color Lerp(Color a, Color b, double t)
        {
            t = Clamp01(t);
            return Color.FromRgb((byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        // Continuous green > amber > red as load rises.
        public static Color ForLoad(double v)
        {
            if (v < 0.6) return Green;
            if (v < 0.8) return Lerp(Green, Amber, (v - 0.6) / 0.2);
            return Lerp(Amber, Red, (v - 0.8) / 0.15);
        }

        public static SolidColorBrush LoadBrush(double v)
        {
            return LoadPalette[(int)Math.Round(Clamp01(v) * 100)];
        }

        public static Brush TempBrush(double celsius)
        {
            return celsius < 60 ? (Brush)Label : LoadBrush((celsius - 45) / 50);
        }

        public static TextBlock TextBlock(string text, double size, Brush brush, FontWeight weight)
        {
            return new TextBlock { Text = text, FontSize = size, Foreground = brush, FontWeight = weight, VerticalAlignment = VerticalAlignment.Center };
        }

        public static Path Icon16(string geometry)
        {
            return new Path
            {
                Data = Geometry.Parse(geometry),
                Stroke = Icon,
                StrokeThickness = 1.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 16,
                Height = 16,
                VerticalAlignment = VerticalAlignment.Center
            };
        }
    }

    // Per-frame clock synced to the display refresh. Runs only while the overlay is visible.
    static class Animator
    {
        public static event Action<double> Frame;
        public static double MaxFps = 60;
        static TimeSpan last;
        static double pending;
        static bool running;

        public static void Start()
        {
            if (running) return;
            running = true;
            last = TimeSpan.Zero;
            CompositionTarget.Rendering += OnRendering;
        }

        public static void Stop()
        {
            if (!running) return;
            running = false;
            CompositionTarget.Rendering -= OnRendering;
        }

        static void OnRendering(object sender, EventArgs e)
        {
            var now = ((RenderingEventArgs)e).RenderingTime;
            if (now == last) return; // Rendering can fire more than once per frame
            pending += last == TimeSpan.Zero ? 1 / 60.0 : (now - last).TotalSeconds;
            last = now;
            // High-refresh monitors fire this up to 240 times a second; redraw at most MaxFps.
            if (pending < 1 / MaxFps - 0.002) return;
            double dt = Math.Min(0.1, pending);
            pending = 0;
            var handler = Frame;
            if (handler != null) handler(dt);
        }
    }

    // Critically damped spring toward Target. Retargeting keeps velocity, so motion never jerks.
    class Smooth
    {
        public double Value, Target, Velocity;
        readonly double time;

        public Smooth(double time) { this.time = time; }

        public bool Step(double dt)
        {
            if (Value == Target && Velocity == 0) return false;
            double omega = 2.0 / time, x = omega * dt;
            double decay = 1.0 / (1 + x + 0.48 * x * x + 0.235 * x * x * x);
            double change = Value - Target, temp = (Velocity + omega * change) * dt;
            Velocity = (Velocity - omega * temp) * decay;
            Value = Target + (change + temp) * decay;
            double scale = Math.Max(1e-3, Math.Abs(Target));
            if (Math.Abs(Value - Target) < scale * 1e-4 && Math.Abs(Velocity) < scale * 1e-3)
            {
                Value = Target;
                Velocity = 0;
            }
            return true;
        }
    }

    // 270-degree arc gauge with a soft glow.
    class Gauge : FrameworkElement
    {
        public readonly Smooth Load = new Smooth(0.45);
        const double StartAngle = 135, Sweep = 270, Stroke = 8;
        readonly SolidColorBrush arcBrush = new SolidColorBrush(Theme.Green), glowBrush = new SolidColorBrush(Color.FromArgb(0x26, 0x34, 0xD3, 0x99));
        readonly Pen arcPen, glowPen;
        static readonly Pen TrackPen = MakeTrack();

        static Pen MakeTrack()
        {
            var pen = new Pen(Theme.Track, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            pen.Freeze();
            return pen;
        }

        public Gauge(double size)
        {
            Width = size;
            Height = size;
            arcPen = new Pen(arcBrush, Stroke) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            glowPen = new Pen(glowBrush, Stroke + 7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        }

        public void Step(double dt)
        {
            if (!Load.Step(dt)) return;
            var c = Theme.ForLoad(Load.Value);
            arcBrush.Color = c;
            glowBrush.Color = Color.FromArgb(0x26, c.R, c.G, c.B);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double r = Width / 2 - Stroke / 2 - 4;
            dc.DrawGeometry(null, TrackPen, Arc(r, Sweep));
            double v = Theme.Clamp01(Load.Value);
            if (v < 0.003) return;
            var arc = Arc(r, Sweep * v);
            dc.DrawGeometry(null, glowPen, arc);
            dc.DrawGeometry(null, arcPen, arc);
        }

        StreamGeometry Arc(double r, double sweep)
        {
            double c = Width / 2;
            Func<double, Point> at = deg => new Point(c + r * Math.Cos(deg * Math.PI / 180), c + r * Math.Sin(deg * Math.PI / 180));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                ctx.BeginFigure(at(StartAngle), false, false);
                ctx.ArcTo(at(StartAngle + Math.Max(0.01, sweep)), new Size(r, r), 0, sweep > 180, SweepDirection.Clockwise, true, false);
            }
            g.Freeze();
            return g;
        }
    }

    // Rounded progress bar with a load-colored gradient.
    class Bar : FrameworkElement
    {
        public readonly Smooth Load = new Smooth(0.45);
        readonly GradientStop from = new GradientStop(Color.FromArgb(0x80, 0x34, 0xD3, 0x99), 0), to = new GradientStop(Theme.Green, 1);
        readonly LinearGradientBrush fill;

        public Bar(double height)
        {
            Height = height;
            fill = new LinearGradientBrush(new GradientStopCollection { from, to }, new Point(0, 0), new Point(1, 0));
        }

        public void Step(double dt)
        {
            if (!Load.Step(dt)) return;
            var c = Theme.ForLoad(Load.Value);
            from.Color = Color.FromArgb(0x80, c.R, c.G, c.B);
            to.Color = c;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight, r = h / 2;
            dc.DrawRoundedRectangle(Theme.Track, null, new Rect(0, 0, w, h), r, r);
            double v = Theme.Clamp01(Load.Value);
            if (v < 0.003) return;
            dc.DrawRoundedRectangle(fill, null, new Rect(0, 0, Math.Max(h, w * v), h), r, r);
        }
    }

    // Label + animated value + bar + optional caption.
    class Metric
    {
        public readonly StackPanel Root = new StackPanel();
        public readonly Bar Bar;
        readonly TextBlock value, caption;
        readonly Smooth amount = new Smooth(0.45);
        readonly Func<double, string> format;

        public Metric(string label, double fontSize, double barHeight, Func<double, string> format)
        {
            this.format = format;
            var top = new Grid();
            top.Children.Add(Theme.TextBlock(label, fontSize, Theme.Label, FontWeights.Normal));
            value = Theme.TextBlock("", fontSize, Theme.Text, FontWeights.SemiBold);
            value.HorizontalAlignment = HorizontalAlignment.Right;
            top.Children.Add(value);
            Bar = new Bar(barHeight) { Margin = new Thickness(0, 6, 0, 0) };
            caption = Theme.TextBlock("", 11, Theme.Muted, FontWeights.Normal);
            caption.Margin = new Thickness(0, 5, 0, 0);
            caption.Visibility = Visibility.Collapsed;
            Root.Children.Add(top);
            Root.Children.Add(Bar);
            Root.Children.Add(caption);
        }

        public void Set(double fraction, double amountValue)
        {
            Bar.Load.Target = Theme.Clamp01(fraction);
            amount.Target = amountValue;
        }

        public void SetCaption(string text)
        {
            caption.Text = text;
            caption.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        public void Step(double dt)
        {
            Bar.Step(dt);
            amount.Step(dt);
            value.Text = format(amount.Value);
        }
    }

    // One vertical bar per logical core.
    class CoreBars : FrameworkElement
    {
        Smooth[] loads = new Smooth[0];

        public void Set(double[] values)
        {
            if (loads.Length != values.Length)
            {
                loads = new Smooth[values.Length];
                for (int i = 0; i < loads.Length; i++) loads[i] = new Smooth(0.4);
            }
            for (int i = 0; i < loads.Length; i++) loads[i].Target = values[i];
        }

        public void Step(double dt)
        {
            bool moved = false;
            foreach (var s in loads) moved |= s.Step(dt);
            if (moved) InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            int n = loads.Length;
            if (n == 0) return;
            const double gap = 3;
            double w = (ActualWidth - gap * (n - 1)) / n, h = ActualHeight;
            for (int i = 0; i < n; i++)
            {
                double x = i * (w + gap), v = Theme.Clamp01(loads[i].Value);
                dc.DrawRoundedRectangle(Theme.Track, null, new Rect(x, 0, w, h), 2.5, 2.5);
                if (v < 0.004) continue;
                double fh = Math.Max(3, h * v);
                dc.DrawRoundedRectangle(Theme.LoadBrush(v), null, new Rect(x, h - fh, w, fh), 2.5, 2.5);
            }
        }
    }

    // Download (green) and upload (blue) history, scrolling continuously between samples.
    class Sparkline : FrameworkElement
    {
        const int Points = 121;
        readonly double[] down = new double[Points], up = new double[Points];
        readonly Smooth scale = new Smooth(0.8);
        readonly double interval;
        double phase = 1;
        static readonly Pen DownPen = Theme.Line(Theme.Green, 1.5), UpPen = Theme.Line(Theme.Blue, 1.5);
        static readonly Brush DownFill = Theme.Fade(Theme.Green, 0x59), UpFill = Theme.Fade(Theme.Blue, 0x26);

        public Sparkline(double interval)
        {
            this.interval = interval;
            scale.Value = scale.Target = 64 * 1024;
        }

        public void Push(double d, double u)
        {
            Array.Copy(down, 1, down, 0, Points - 1);
            Array.Copy(up, 1, up, 0, Points - 1);
            down[Points - 1] = d;
            up[Points - 1] = u;
            phase = 0;
            scale.Target = Math.Max(64 * 1024, Math.Max(down.Max(), up.Max()) * 1.25);
        }

        public void Step(double dt)
        {
            phase = Math.Min(1, phase + dt / interval);
            scale.Step(dt);
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            var bounds = new Rect(0, 0, w, h);
            dc.DrawRoundedRectangle(Theme.Track, null, bounds, 6, 6);
            dc.PushClip(new RectangleGeometry(bounds, 6, 6));
            double step = w / (Points - 2);
            dc.DrawGeometry(DownFill, null, Curve(down, step, h, true));
            dc.DrawGeometry(UpFill, null, Curve(up, step, h, true));
            dc.DrawGeometry(null, UpPen, Curve(up, step, h, false));
            dc.DrawGeometry(null, DownPen, Curve(down, step, h, false));
            dc.Pop();
        }

        StreamGeometry Curve(double[] values, double step, double h, bool closed)
        {
            double s = scale.Value;
            Func<int, Point> at = i => new Point((i - phase) * step, h - 2 - (h - 6) * Math.Min(1, values[i] / s));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                Point first = at(0);
                if (closed)
                {
                    ctx.BeginFigure(new Point(first.X, h), true, true);
                    ctx.LineTo(first, false, false);
                }
                else ctx.BeginFigure(first, false, false);
                // Quadratic curves through midpoints give a smooth line with no overshoot.
                for (int i = 1; i < Points - 1; i++)
                {
                    Point p = at(i), n = at(i + 1);
                    ctx.QuadraticBezierTo(p, new Point((p.X + n.X) / 2, (p.Y + n.Y) / 2), true, true);
                }
                Point last = at(Points - 1);
                ctx.LineTo(last, true, true);
                if (closed) ctx.LineTo(new Point(last.X, h), false, false);
            }
            g.Freeze();
            return g;
        }
    }

    class Overlay : Window
    {
        const int HotkeyId = 0x5247;
        const double SampleSeconds = 0.5;
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ShrinkIcon = "M13.5,2.5 L9,7 M9,3.8 L9,7 L12.2,7 M2.5,13.5 L7,9 M7,12.2 L7,9 L3.8,9";
        const string ExpandIcon = "M9,7 L13.5,2.5 M10.3,2.5 L13.5,2.5 L13.5,5.7 M7,9 L2.5,13.5 M2.5,10.3 L2.5,13.5 L5.7,13.5";
        const string TempHint = "CPU temperature needs LibreHardwareMonitor running as administrator, with Options > Remote Web Server > Run enabled.";

        readonly Settings settings;
        readonly Gpu gpu = new Gpu();
        readonly Cpu cpu = new Cpu();
        readonly CpuTemp cpuTemp;
        readonly Net net = new Net();
        readonly DispatcherTimer timer;

        readonly Gauge cpuGauge = new Gauge(108), gpuGauge = new Gauge(108);
        TextBlock cpuValue, gpuValue, cpuTempText, gpuTempText, cpuDetail, gpuDetail;
        readonly Metric vram, ram, power, miniCpu, miniGpu, miniRam;
        readonly CoreBars cores = new CoreBars { Height = 26, Margin = new Thickness(0, 7, 0, 0) };
        readonly Sparkline spark = new Sparkline(SampleSeconds) { Height = 44, Margin = new Thickness(0, 7, 0, 0) };
        readonly Smooth netDownRate = new Smooth(0.5), netUpRate = new Smooth(0.5), dotLoad = new Smooth(0.6), hover = new Smooth(0.15);
        readonly Run netDown = new Run { Foreground = Theme.GreenBrush }, netUp = new Run { Foreground = Theme.BlueBrush };
        readonly TextBlock threadsCount, uptime, clockText;
        readonly StackPanel body;
        readonly FrameworkElement compactRow, content;
        readonly Grid card;
        readonly TranslateTransform slide = new TranslateTransform();
        readonly Ellipse dot;
        readonly SolidColorBrush dotBrush = new SolidColorBrush(Theme.Green);
        readonly Border collapse;
        readonly Path collapseIcon;
        readonly Dictionary<string, UIElement[]> sections = new Dictionary<string, UIElement[]>();
        readonly HashSet<string> hidden;
        readonly WF.NotifyIcon tray;
        readonly WF.ToolStripMenuItem toggleItem, compactItem, clickThroughItem, startupItem;
        readonly string hotkeyText;
        ulong vramTotal, ramTotal;
        double powerLimit, pulse, opacity;
        IntPtr hwnd;
        Native.SubclassProc styleGuard; // field keeps the delegate alive for native code
        bool compact, clickThrough, hiding;

        public Overlay(Settings settings)
        {
            this.settings = settings;
            hotkeyText = settings.Get("Hotkey", "Ctrl+Alt+R");
            compact = settings.Get("Compact", "false") == "true";
            cpuTemp = new CpuTemp((int)settings.GetDouble("LhmPort", 8085));
            hidden = new HashSet<string>(settings.Get("Hidden", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()),
                StringComparer.OrdinalIgnoreCase);

            Title = "ResGUI";
            WindowStyle = WindowStyle.None;
            // Not AllowsTransparency: per-pixel transparent windows are copied back from the GPU on every frame,
            // which is expensive while animating. Windows 11 rounds the corners instead (see OnSourceInitialized).
            Background = Theme.Panel;
            Topmost = true;
            ShowInTaskbar = false;
            ResizeMode = ResizeMode.NoResize;
            Width = 320;
            SizeToContent = SizeToContent.Height;
            FontFamily = Theme.UiFont;
            UseLayoutRounding = true;
            Typography.SetNumeralAlignment(this, FontNumeralAlignment.Tabular); // digits don't shift as values change
            opacity = Math.Max(0.2, Math.Min(1, settings.GetDouble("Opacity", 1)));
            Animator.MaxFps = Math.Max(15, Math.Min(240, settings.GetDouble("Fps", 60)));

            // Header: pulsing status dot, CPU + GPU name, compact button
            var header = new DockPanel();
            dot = new Ellipse { Width = 7, Height = 7, Fill = dotBrush, Margin = new Thickness(1, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(dot, Dock.Left);
            header.Children.Add(dot);
            collapseIcon = Theme.Icon16(compact ? ExpandIcon : ShrinkIcon);
            collapse = new Border { Width = 22, Height = 22, Background = Brushes.Transparent, Cursor = Cursors.Hand, Child = collapseIcon, ToolTip = "Compact" };
            collapse.MouseEnter += delegate { collapseIcon.Stroke = Theme.IconHover; };
            collapse.MouseLeave += delegate { collapseIcon.Stroke = Theme.Icon; };
            collapse.MouseLeftButtonDown += (s, e) => e.Handled = true; // don't start a drag
            collapse.MouseLeftButtonUp += (s, e) => { SetCompact(!compact); e.Handled = true; };
            collapse.Opacity = 0.35;
            DockPanel.SetDock(collapse, Dock.Right);
            header.Children.Add(collapse);
            string name = ShortCpuName() + (gpu.Ok ? "  ·  " + gpu.Name : "");
            var title = Theme.TextBlock(name, 13, Theme.Text, FontWeights.SemiBold);
            title.TextTrimming = TextTrimming.CharacterEllipsis;
            header.Children.Add(title);

            // Gauges
            var gauges = new UniformGrid { Columns = gpu.Ok ? 2 : 1, Margin = new Thickness(0, 14, 0, 0) };
            gauges.Children.Add(GaugeCard("CPU", cpuGauge, out cpuValue, out cpuTempText, out cpuDetail));
            if (gpu.Ok) gauges.Children.Add(GaugeCard("GPU", gpuGauge, out gpuValue, out gpuTempText, out gpuDetail));

            // Bars
            vram = new Metric("VRAM", 12.5, 5, v => Gb(v) + " / " + Gb(vramTotal));
            ram = new Metric("RAM", 12.5, 5, v => Gb(v) + " / " + Gb(ramTotal));
            power = new Metric("Power", 12.5, 5, v => string.Format("{0:0} / {1:0} W", v, powerLimit));
            vram.Root.Margin = ram.Root.Margin = power.Root.Margin = new Thickness(0, 14, 0, 0);

            // Threads
            var threads = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var threadsTop = new Grid();
            threadsTop.Children.Add(Theme.TextBlock("Threads", 12.5, Theme.Label, FontWeights.Normal));
            threadsCount = Theme.TextBlock("", 12.5, Theme.Muted, FontWeights.Normal);
            threadsCount.HorizontalAlignment = HorizontalAlignment.Right;
            threadsTop.Children.Add(threadsCount);
            threads.Children.Add(threadsTop);
            threads.Children.Add(cores);

            // Network
            var network = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var netTop = new Grid();
            netTop.Children.Add(Theme.TextBlock("Network", 12.5, Theme.Label, FontWeights.Normal));
            var netValue = Theme.TextBlock("", 12.5, Theme.Text, FontWeights.SemiBold);
            netValue.HorizontalAlignment = HorizontalAlignment.Right;
            netValue.Inlines.Add(netDown);
            netValue.Inlines.Add(new Run("    "));
            netValue.Inlines.Add(netUp);
            netTop.Children.Add(netValue);
            network.Children.Add(netTop);
            network.Children.Add(spark);

            // Footer
            var footerGrid = new Grid();
            uptime = Theme.TextBlock("", 11.5, Theme.Muted, FontWeights.Normal);
            clockText = Theme.TextBlock("", 11.5, Theme.Muted, FontWeights.Normal);
            clockText.HorizontalAlignment = HorizontalAlignment.Right;
            footerGrid.Children.Add(uptime);
            footerGrid.Children.Add(clockText);
            var footer = new Border
            {
                BorderBrush = Theme.Track,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Margin = new Thickness(0, 16, 0, 0),
                Padding = new Thickness(0, 10, 0, 0),
                Child = footerGrid
            };

            body = new StackPanel();
            body.Children.Add(gauges);
            if (gpu.Ok) body.Children.Add(vram.Root);
            body.Children.Add(ram.Root);
            if (gpu.Ok) body.Children.Add(power.Root);
            body.Children.Add(threads);
            body.Children.Add(network);
            body.Children.Add(footer);

            sections["Gauges"] = new UIElement[] { gauges };
            sections["Memory + power"] = gpu.Ok ? new UIElement[] { vram.Root, ram.Root, power.Root } : new UIElement[] { ram.Root };
            sections["Threads"] = new UIElement[] { threads };
            sections["Network"] = new UIElement[] { network };
            sections["Uptime + clock"] = new UIElement[] { footer };

            // Compact mode: three mini meters
            miniCpu = new Metric("CPU", 11.5, 4, v => Pct(v) + "%");
            miniGpu = new Metric("GPU", 11.5, 4, v => Pct(v) + "%");
            miniRam = new Metric("RAM", 11.5, 4, v => Pct(v) + "%");
            var mini = new UniformGrid { Columns = gpu.Ok ? 3 : 2, Margin = new Thickness(0, 12, 0, 0) };
            var minis = gpu.Ok ? new[] { miniCpu, miniGpu, miniRam } : new[] { miniCpu, miniRam };
            for (int i = 0; i < minis.Length; i++)
            {
                minis[i].Root.Margin = new Thickness(i == 0 ? 0 : 7, 0, i == minis.Length - 1 ? 0 : 7, 0);
                mini.Children.Add(minis[i].Root);
            }
            compactRow = mini;

            var stack = new StackPanel();
            stack.Children.Add(header);
            stack.Children.Add(body);
            stack.Children.Add(compactRow);
            content = stack;

            var front = new Border { Padding = new Thickness(18, 14, 18, 16), Child = content };
            card = new Grid { RenderTransform = slide };
            card.Children.Add(front);
            Content = card;

            // Tray icon and menu (also shown on right-click of the overlay)
            var menu = new WF.ContextMenuStrip();
            toggleItem = new WF.ToolStripMenuItem("Hide", null, delegate { Toggle(); }) { ShortcutKeyDisplayString = hotkeyText };
            compactItem = new WF.ToolStripMenuItem("Compact", null, delegate { SetCompact(!compact); }) { Checked = compact };
            clickThroughItem = new WF.ToolStripMenuItem("Click-through", null, delegate { SetClickThrough(!clickThrough); });
            var sectionsItem = new WF.ToolStripMenuItem("Sections");
            foreach (var key in sections.Keys)
            {
                string section = key;
                var item = new WF.ToolStripMenuItem(section) { Checked = !hidden.Contains(section), CheckOnClick = true };
                item.CheckedChanged += delegate { SetSection(section, item.Checked); };
                sectionsItem.DropDownItems.Add(item);
            }
            var opacityItem = new WF.ToolStripMenuItem("Opacity");
            foreach (int pct in new[] { 100, 90, 80, 70, 60, 50 })
            {
                double o = pct / 100.0;
                opacityItem.DropDownItems.Add(new WF.ToolStripMenuItem(pct + "%", null, delegate { opacity = o; settings.Set("Opacity", o); ApplyLayering(); }));
            }
            startupItem = new WF.ToolStripMenuItem("Start with Windows", null, delegate { SetStartup(!startupItem.Checked); }) { Checked = StartupEnabled() };
            menu.Items.Add(toggleItem);
            menu.Items.Add(compactItem);
            menu.Items.Add(clickThroughItem);
            menu.Items.Add(sectionsItem);
            menu.Items.Add(opacityItem);
            menu.Items.Add(startupItem);
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(new WF.ToolStripMenuItem("Edit settings...", null, delegate { Process.Start("notepad.exe", "\"" + settings.Path + "\""); }));
            menu.Items.Add(new WF.ToolStripMenuItem("Exit", null, delegate { Close(); }));
            tray = new WF.NotifyIcon { Icon = MakeTrayIcon(), Text = "ResGUI", ContextMenuStrip = menu, Visible = true };
            tray.MouseClick += (s, e) => { if (e.Button == WF.MouseButtons.Left) Toggle(); };

            MouseLeftButtonDown += delegate
            {
                DragMove();
                settings.Set("Left", Math.Round(Left));
                settings.Set("Top", Math.Round(Top));
            };
            MouseRightButtonUp += delegate { menu.Show(WF.Cursor.Position); };
            MouseEnter += delegate { hover.Target = 1; };
            MouseLeave += delegate { hover.Target = 0; };

            foreach (var key in sections.Keys) ApplySection(key);
            RestorePosition();
            ApplyCompact();
            SourceInitialized += OnSourceInitialized;
            Loaded += delegate
            {
                Refresh();
                Animator.Frame += OnFrame;
                Animator.Start();
                AnimateIn(true);
            };
            Closed += OnClosed;

            timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(SampleSeconds) };
            timer.Tick += delegate { Refresh(); };
            timer.Start();
        }

        static FrameworkElement GaugeCard(string label, Gauge gauge, out TextBlock value, out TextBlock tempText, out TextBlock detail)
        {
            value = Theme.TextBlock("0%", 24, Theme.Text, FontWeights.SemiBold);
            value.FontFamily = Theme.NumberFont;
            value.HorizontalAlignment = HorizontalAlignment.Center;
            tempText = Theme.TextBlock("", 12, Theme.Label, FontWeights.Normal);
            tempText.HorizontalAlignment = HorizontalAlignment.Center;
            tempText.Margin = new Thickness(0, -2, 0, 0);
            var center = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) };
            center.Children.Add(value);
            center.Children.Add(tempText);
            // The label sits in the gap at the bottom of the arc.
            var name = Theme.TextBlock(label, 11, Theme.Label, FontWeights.SemiBold);
            name.HorizontalAlignment = HorizontalAlignment.Center;
            name.VerticalAlignment = VerticalAlignment.Bottom;
            name.Margin = new Thickness(0, 0, 0, 7);
            var dial = new Grid { Width = gauge.Width, Height = gauge.Height };
            dial.Children.Add(gauge);
            dial.Children.Add(center);
            dial.Children.Add(name);
            detail = Theme.TextBlock("", 11.5, Theme.Muted, FontWeights.Normal);
            detail.HorizontalAlignment = HorizontalAlignment.Center;
            var cardPanel = new StackPanel();
            cardPanel.Children.Add(dial);
            cardPanel.Children.Add(detail);
            return cardPanel;
        }

        static string ShortCpuName()
        {
            string name = "";
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
                    if (key != null) name = Convert.ToString(key.GetValue("ProcessorNameString")).Trim();
            }
            catch (Exception) { }
            // "AMD Ryzen 7 7800X3D 8-Core Processor" -> "7800X3D", "Intel(R) Core(TM) i7-13700K" -> "i7-13700K"
            var m = Regex.Match(name, @"\b(i[3579]-\d{4,5}[A-Z]*|\d{3,5}[A-Z][A-Z0-9]*|\d{4,5})\b");
            return m.Success ? m.Value : name;
        }

        void OnSourceInitialized(object sender, EventArgs e)
        {
            hwnd = new WindowInteropHelper(this).Handle;
            // Tool window: hidden from Alt+Tab. No-activate: clicking it never steals focus from a game.
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | Native.WS_EX_TOOLWINDOW | Native.WS_EX_NOACTIVATE);
            HwndSource.FromHwnd(hwnd).AddHook(WndProc);
            styleGuard = StyleGuard;
            Native.SetWindowSubclass(hwnd, styleGuard, UIntPtr.Zero, UIntPtr.Zero);
            int round = Native.DWMWCP_ROUND, border = 0x00382F2F; // COLORREF 0x00BBGGRR = #2F2F38
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
            Native.DwmSetWindowAttribute(hwnd, Native.DWMWA_BORDER_COLOR, ref border, 4);
            ApplyLayering();

            uint mods, vk;
            if (!ParseHotkey(hotkeyText, out mods, out vk))
                tray.ShowBalloonTip(5000, "ResGUI", "Invalid hotkey \"" + hotkeyText + "\" in settings.ini.", WF.ToolTipIcon.Warning);
            else if (!Native.RegisterHotKey(hwnd, HotkeyId, mods | Native.MOD_NOREPEAT, vk))
                tray.ShowBalloonTip(5000, "ResGUI", "Hotkey " + hotkeyText + " is taken by another app. Change it via tray > Edit settings.", WF.ToolTipIcon.Warning);
        }

        // WPF strips WS_EX_LAYERED from windows without AllowsTransparency. This subclass runs after WPF's
        // handler, so it has the last word and keeps the bits that opacity and click-through need.
        IntPtr StyleGuard(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
        {
            IntPtr result = Native.DefSubclassProc(h, msg, wParam, lParam);
            if (msg == Native.WM_STYLECHANGING && wParam.ToInt32() == Native.GWL_EXSTYLE)
            {
                var style = (Native.STYLESTRUCT)Marshal.PtrToStructure(lParam, typeof(Native.STYLESTRUCT));
                int wanted = (style.StyleNew & ~(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT)) | LayerBits();
                if (wanted != style.StyleNew)
                {
                    style.StyleNew = wanted;
                    Marshal.StructureToPtr(style, lParam, false);
                }
            }
            return result;
        }

        IntPtr WndProc(IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == Native.WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                Toggle();
                handled = true;
            }
            return IntPtr.Zero;
        }

        void OnClosed(object sender, EventArgs e)
        {
            timer.Stop();
            Animator.Stop();
            if (hwnd != IntPtr.Zero) Native.UnregisterHotKey(hwnd, HotkeyId);
            tray.Visible = false;
            tray.Dispose();
            if (gpu.Ok) Nvml.Shutdown();
            Application.Current.Shutdown();
        }

        static DoubleAnimation Ease(double to, int ms, EasingMode mode)
        {
            return new DoubleAnimation(to, new Duration(TimeSpan.FromMilliseconds(ms))) { EasingFunction = new CubicEase { EasingMode = mode } };
        }

        void AnimateIn(bool fromStart)
        {
            if (fromStart)
            {
                card.Opacity = 0;
                slide.Y = -10;
            }
            card.BeginAnimation(OpacityProperty, Ease(1, 220, EasingMode.EaseOut));
            slide.BeginAnimation(TranslateTransform.YProperty, Ease(0, 320, EasingMode.EaseOut));
        }

        void Toggle()
        {
            if (IsVisible && !hiding)
            {
                hiding = true;
                var fade = Ease(0, 150, EasingMode.EaseIn);
                fade.Completed += delegate
                {
                    if (!hiding) return; // shown again mid-fade
                    hiding = false;
                    Hide();
                    Animator.Stop();
                };
                card.BeginAnimation(OpacityProperty, fade);
                slide.BeginAnimation(TranslateTransform.YProperty, Ease(-8, 150, EasingMode.EaseIn));
                toggleItem.Text = "Show";
            }
            else
            {
                bool wasHidden = !IsVisible;
                hiding = false;
                if (wasHidden)
                {
                    Show();
                    KeepOnTop();
                    Refresh();
                    Animator.Start();
                }
                AnimateIn(false);
                toggleItem.Text = "Hide";
            }
        }

        void SetCompact(bool on)
        {
            compact = on;
            ApplyCompact();
            settings.Set("Compact", on ? "true" : "false");
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0.2, 1, new Duration(TimeSpan.FromMilliseconds(260))) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        }

        void ApplyCompact()
        {
            body.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
            compactRow.Visibility = compact ? Visibility.Visible : Visibility.Collapsed;
            collapseIcon.Data = Geometry.Parse(compact ? ExpandIcon : ShrinkIcon);
            collapse.ToolTip = compact ? "Expand" : "Compact";
            compactItem.Checked = compact;
        }

        void SetSection(string name, bool visible)
        {
            if (visible) hidden.Remove(name);
            else hidden.Add(name);
            settings.Set("Hidden", string.Join(",", hidden));
            ApplySection(name);
        }

        void ApplySection(string name)
        {
            foreach (var element in sections[name])
                element.Visibility = hidden.Contains(name) ? Visibility.Collapsed : Visibility.Visible;
        }

        void SetClickThrough(bool on)
        {
            clickThrough = on;
            clickThroughItem.Checked = on;
            ApplyLayering();
        }

        // Whole-window opacity and click-through both need WS_EX_LAYERED; leave it off otherwise (cheapest to draw).
        void ApplyLayering()
        {
            if (hwnd == IntPtr.Zero) return;
            int bits = LayerBits();
            int ex = Native.GetWindowLong(hwnd, Native.GWL_EXSTYLE) & ~(Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT);
            Native.SetWindowLong(hwnd, Native.GWL_EXSTYLE, ex | bits);
            if (bits != 0) Native.SetLayeredWindowAttributes(hwnd, 0, (byte)Math.Round(opacity * 255), Native.LWA_ALPHA);
        }

        int LayerBits()
        {
            int bits = opacity < 0.999 || clickThrough ? Native.WS_EX_LAYERED : 0;
            return clickThrough ? bits | Native.WS_EX_TRANSPARENT : bits;
        }

        static bool StartupEnabled()
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RunKey))
                return key != null && key.GetValue("ResGUI") != null;
        }

        void SetStartup(bool on)
        {
            using (var key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (on) key.SetValue("ResGUI", "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                else key.DeleteValue("ResGUI", false);
            }
            startupItem.Checked = on;
        }

        void RestorePosition()
        {
            Rect work = SystemParameters.WorkArea;
            Rect screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            double left = settings.GetDouble("Left", double.NaN), top = settings.GetDouble("Top", double.NaN);
            if (double.IsNaN(left) || double.IsNaN(top) || !screen.Contains(new Point(left + 60, top + 40)))
            {
                left = work.Right - Width - 24;
                top = work.Top + 24;
            }
            Left = left;
            Top = top;
        }

        void KeepOnTop()
        {
            if (hwnd != IntPtr.Zero)
                Native.SetWindowPos(hwnd, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        }

        static string Gb(double bytes)
        {
            return (bytes / 1073741824.0).ToString("0.0", CultureInfo.InvariantCulture) + " GB";
        }

        static int Pct(double frac)
        {
            return (int)Math.Round(Theme.Clamp01(frac) * 100);
        }

        static string Rate(double bytesPerSec)
        {
            if (bytesPerSec >= 1048576) return (bytesPerSec / 1048576).ToString(bytesPerSec >= 10485760 ? "0" : "0.0", CultureInfo.InvariantCulture) + " MB/s";
            return (bytesPerSec / 1024).ToString("0", CultureInfo.InvariantCulture) + " KB/s";
        }

        static string Uptime()
        {
            var t = TimeSpan.FromMilliseconds(Native.GetTickCount64());
            if (t.TotalDays >= 1) return string.Format("Up {0}d {1}h {2}m", (int)t.TotalDays, t.Hours, t.Minutes);
            return string.Format("Up {0}h {1}m", t.Hours, t.Minutes);
        }

        // Runs every display frame while visible: advances all springs and redraws what moved.
        void OnFrame(double dt)
        {
            cpuGauge.Step(dt);
            cpuValue.Text = Pct(cpuGauge.Load.Value) + "%";
            if (gpu.Ok)
            {
                gpuGauge.Step(dt);
                gpuValue.Text = Pct(gpuGauge.Load.Value) + "%";
                vram.Step(dt);
                power.Step(dt);
                miniGpu.Step(dt);
            }
            ram.Step(dt);
            miniCpu.Step(dt);
            miniRam.Step(dt);
            cores.Step(dt);
            spark.Step(dt);
            netDownRate.Step(dt);
            netUpRate.Step(dt);
            netDown.Text = "↓ " + Rate(netDownRate.Value);
            netUp.Text = "↑ " + Rate(netUpRate.Value);

            dotLoad.Step(dt);
            dotBrush.Color = Theme.ForLoad(dotLoad.Value);
            pulse += dt;
            dot.Opacity = 0.55 + 0.45 * (0.5 + 0.5 * Math.Sin(pulse * 2 * Math.PI / 2.4));
            if (hover.Step(dt)) collapse.Opacity = 0.35 + 0.65 * hover.Value;
        }

        // Every SampleSeconds: read sensors and set new spring targets.
        void Refresh()
        {
            cpu.Sample();
            net.Sample();
            spark.Push(net.Down, net.Up);
            var mem = new Native.MEMORYSTATUSEX();
            mem.dwLength = (uint)Marshal.SizeOf(mem);
            Native.GlobalMemoryStatusEx(ref mem);
            ulong ramUsed = mem.ullTotalPhys - mem.ullAvailPhys;
            double ramLoad = (double)ramUsed / mem.ullTotalPhys;
            double cpuLoad = cpu.Total;

            if (!IsVisible) return;
            KeepOnTop();
            var g = gpu.Read();

            cpuGauge.Load.Target = cpuLoad;
            float cpuC = cpuTemp.Celsius;
            cpuTempText.Text = float.IsNaN(cpuC) ? "– °C" : Math.Round(cpuC) + " °C";
            cpuTempText.Foreground = float.IsNaN(cpuC) ? Theme.Muted : Theme.TempBrush(cpuC);
            cpuTempText.ToolTip = float.IsNaN(cpuC) ? TempHint : null;
            double mhz = cpu.ClockMhz();
            cpuDetail.Text = mhz > 0 ? (mhz / 1000).ToString("0.00", CultureInfo.InvariantCulture) + " GHz" : "";

            ramTotal = mem.ullTotalPhys;
            ram.Set(ramLoad, ramUsed);
            miniCpu.Set(cpuLoad, cpuLoad);
            miniRam.Set(ramLoad, ramLoad);
            cores.Set(cpu.Cores);
            threadsCount.Text = cpu.Cores.Length.ToString();
            netDownRate.Target = net.Down;
            netUpRate.Target = net.Up;

            double peak = Math.Max(cpuLoad, ramLoad);
            string text = string.Format("CPU {0}%   RAM {1}%", Pct(cpuLoad), Pct(ramLoad));
            if (g != null)
            {
                double gpuLoad = g.Util / 100.0;
                gpuGauge.Load.Target = gpuLoad;
                gpuTempText.Text = g.Temp > 0 ? g.Temp + " °C" : "";
                gpuTempText.Foreground = Theme.TempBrush(g.Temp);
                gpuDetail.Text = g.CoreMhz + " MHz  ·  " + (g.Fan < 0 ? "" : g.Fan == 0 ? "Fan off" : "Fan " + g.Fan + "%"); // 0% = zero-RPM idle mode
                vramTotal = g.VramTotal;
                vram.Set((double)g.VramUsed / g.VramTotal, g.VramUsed);
                vram.SetCaption("Mem " + g.MemMhz + " MHz  ·  bus " + g.MemLoad + "%");
                powerLimit = g.PowerLimit;
                power.Set(g.PowerLimit > 0 ? g.Power / g.PowerLimit : 0, g.Power);
                miniGpu.Set(gpuLoad, gpuLoad);
                peak = Math.Max(peak, gpuLoad);
                text = string.Format("CPU {0}%   GPU {1}%   RAM {2}%", Pct(cpuLoad), g.Util, Pct(ramLoad));
            }
            dotLoad.Target = peak;

            uptime.Text = Uptime();
            clockText.Text = DateTime.Now.ToString("t");
            tray.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }

        static bool ParseHotkey(string text, out uint mods, out uint vk)
        {
            mods = 0;
            vk = 0;
            foreach (var raw in text.Split('+'))
            {
                string part = raw.Trim();
                if (part.Length == 0) continue;
                switch (part.ToLowerInvariant())
                {
                    case "ctrl": case "control": mods |= Native.MOD_CONTROL; break;
                    case "alt": mods |= Native.MOD_ALT; break;
                    case "shift": mods |= Native.MOD_SHIFT; break;
                    case "win": mods |= Native.MOD_WIN; break;
                    default:
                        Key key;
                        string name = part.Length == 1 && char.IsDigit(part[0]) ? "D" + part : part;
                        if (!Enum.TryParse(name, true, out key)) return false;
                        vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                        break;
                }
            }
            return vk != 0;
        }

        static SD.Icon MakeTrayIcon()
        {
            try
            {
                var icon = SD.Icon.ExtractAssociatedIcon(Assembly.GetExecutingAssembly().Location); // embedded ResGUI.ico
                if (icon != null) return icon;
            }
            catch (Exception) { }
            using (var bmp = new SD.Bitmap(32, 32))
            {
                using (var g = SD.Graphics.FromImage(bmp))
                using (var bg = new SD.SolidBrush(SD.Color.FromArgb(27, 27, 31)))
                using (var green = new SD.SolidBrush(SD.Color.FromArgb(52, 211, 153)))
                {
                    g.FillRectangle(bg, 0, 0, 32, 32);
                    g.FillRectangle(green, 5, 19, 6, 9);
                    g.FillRectangle(green, 13, 11, 6, 17);
                    g.FillRectangle(green, 21, 4, 6, 24);
                }
                return SD.Icon.FromHandle(bmp.GetHicon());
            }
        }
    }

    static class Program
    {
        [STAThread]
        static void Main()
        {
            bool created;
            using (new Mutex(true, "ResGUI.SingleInstance", out created))
            {
                if (!created) return;
                WF.Application.EnableVisualStyles();
                var settings = new Settings();
                if (settings.Get("Hotkey", "") == "") settings.Set("Hotkey", "Ctrl+Alt+R");
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                app.Run(new Overlay(settings));
            }
        }
    }
}
