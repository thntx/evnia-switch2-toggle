// InputToggle - canvia l'entrada del monitor amb una drecera de teclat.
//
// Parla DDC/CI directament amb el monitor (dxva2.dll, codi VCP 0x60), sense
// dependre de l'Evnia Precision Center. Viu a la safata del sistema.
//
// Compila amb build.ps1 (csc.exe del .NET Framework -> C# 5, res mes modern).

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MonitorInput
{
    static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int left, top, right, bottom; }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct PHYSICAL_MONITOR
        {
            public IntPtr handle;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string description;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct MONITORINFOEX
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public uint dwFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct DISPLAY_DEVICE
        {
            public int cb;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
            public int StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
        }

        public delegate bool MonitorEnumProc(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data);

        [DllImport("user32.dll")]
        public static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr rect, MonitorEnumProc cb, IntPtr data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool GetMonitorInfo(IntPtr hMon, ref MONITORINFOEX mi);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool EnumDisplayDevices(string device, uint devNum, ref DISPLAY_DEVICE dd, uint flags);
        [DllImport("user32.dll")]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
        [DllImport("user32.dll")]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("dxva2.dll")]
        public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMon, out uint count);
        [DllImport("dxva2.dll")]
        public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMon, uint count, [Out] PHYSICAL_MONITOR[] arr);
        [DllImport("dxva2.dll")]
        public static extern bool DestroyPhysicalMonitor(IntPtr hPhysical);
        [DllImport("dxva2.dll")]
        public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte code, out uint type, out uint current, out uint max);
        [DllImport("dxva2.dll")]
        public static extern bool SetVCPFeature(IntPtr h, byte code, uint value);

        public const int WM_HOTKEY = 0x0312;
        public const uint MOD_ALT = 0x0001, MOD_CONTROL = 0x0002, MOD_SHIFT = 0x0004, MOD_WIN = 0x0008, MOD_NOREPEAT = 0x4000;
        public const byte VCP_INPUT_SOURCE = 0x60;
        public const byte VCP_SCALING = 0x86;
        public const byte VCP_VERTICAL_FREQUENCY = 0xAE;
        public const byte VCP_AUDIO_MUTE = 0x8D;
    }

    /// <summary>Localitza el monitor per identificador EDID i li escriu el VCP 0x60.</summary>
    static class Ddc
    {
        /// <summary>
        /// Torna el handle fisic del monitor l'EDID del qual conte <paramref name="monitorId"/>
        /// (p.ex. "PHLC29F"). Si monitorId es buit, agafa el monitor principal.
        /// Cal alliberar-lo amb DestroyPhysicalMonitor.
        /// </summary>
        public static bool TryOpen(string monitorId, out IntPtr physical, out string device)
        {
            IntPtr found = IntPtr.Zero;
            string foundDevice = null;
            bool ok = false;

            Native.MonitorEnumProc cb = delegate(IntPtr hMon, IntPtr hdc, IntPtr rect, IntPtr data)
            {
                Native.MONITORINFOEX mi = new Native.MONITORINFOEX();
                mi.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (!Native.GetMonitorInfo(hMon, ref mi)) return true;

                if (!string.IsNullOrEmpty(monitorId) && !MatchesEdid(mi.szDevice, monitorId)) return true;

                uint count;
                if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMon, out count) || count == 0) return true;

                Native.PHYSICAL_MONITOR[] arr = new Native.PHYSICAL_MONITOR[count];
                if (!Native.GetPhysicalMonitorsFromHMONITOR(hMon, count, arr)) return true;

                // El handle valid pot ser 0: cal una bandera a part, no un sentinella.
                found = arr[0].handle;
                foundDevice = mi.szDevice;
                ok = true;
                for (int i = 1; i < count; i++) Native.DestroyPhysicalMonitor(arr[i].handle);
                return false;
            };

            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
            physical = found;
            device = foundDevice;
            return ok;
        }

        /// <summary>Mira si el \\.\DISPLAYn correspon a un monitor amb aquest id EDID.</summary>
        static bool MatchesEdid(string displayName, string monitorId)
        {
            Native.DISPLAY_DEVICE dd = new Native.DISPLAY_DEVICE();
            dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
            for (uint i = 0; Native.EnumDisplayDevices(displayName, i, ref dd, 0); i++)
            {
                if (dd.DeviceID != null && dd.DeviceID.IndexOf(monitorId, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
                dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
            }
            return false;
        }

        public static int Read(IntPtr physical, byte code)
        {
            uint type, current, max;
            if (!Native.GetVCPFeatureAndVCPFeatureReply(physical, code, out type, out current, out max))
                return -1;
            return (int)current;
        }

        public static bool Write(IntPtr physical, byte code, int value)
        {
            return Native.SetVCPFeature(physical, code, (uint)value);
        }
    }

    class Config
    {
        public string MonitorId = "PHLC29F";
        public int InputA = 15;
        public int InputB = 18;
        public string NameA = "Aquest PC";
        public string NameB = "L'altre aparell";
        public List<string> Hotkeys = new List<string>();
        public List<string> MuteHotkeys = new List<string>();
        public int MuteOn = 1;   // 0x8D: 1 = silenci
        public int MuteOff = 2;  //       2 = so
        public bool Notify = true;
        public int ScaleA = -1, ScaleB = -1;
        public int PcOnlyAboveHz = 0;
        public bool SizeEnabled { get { return ScaleA >= 0 && ScaleB >= 0; } }

        public static string Path
        {
            get
            {
                string exe = Assembly.GetExecutingAssembly().Location;
                return System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exe), "input-toggle.ini");
            }
        }

        public static Config Load()
        {
            Config c = new Config();
            if (!File.Exists(Path)) { c.Hotkeys.Add("Ctrl+Alt+Shift+F10"); c.MuteHotkeys.Add("Ctrl+Alt+Shift+F11"); return c; }

            foreach (string raw in File.ReadAllLines(Path))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                string val = line.Substring(eq + 1).Trim();

                switch (key)
                {
                    case "monitor": c.MonitorId = val; break;
                    case "entrada_a": c.InputA = ParseInt(val, c.InputA); break;
                    case "entrada_b": c.InputB = ParseInt(val, c.InputB); break;
                    case "escala_a": c.ScaleA = ParseInt(val, -1); break;
                    case "escala_b": c.ScaleB = ParseInt(val, -1); break;
                    case "pc_per_sobre_hz": c.PcOnlyAboveHz = ParseInt(val, 0); break;
                    case "nom_a": c.NameA = val; break;
                    case "nom_b": c.NameB = val; break;
                    case "notificacions": c.Notify = val.ToLowerInvariant() != "no" && val != "0"; break;
                    case "silenci_valor": c.MuteOn = ParseInt(val, c.MuteOn); break;
                    case "so_valor": c.MuteOff = ParseInt(val, c.MuteOff); break;
                    case "dreceres":
                        foreach (string hk in val.Split(','))
                            if (hk.Trim().Length > 0) c.Hotkeys.Add(hk.Trim());
                        break;
                    case "dreceres_silenci":
                        foreach (string hk in val.Split(','))
                            if (hk.Trim().Length > 0) c.MuteHotkeys.Add(hk.Trim());
                        break;
                }
            }
            if (c.Hotkeys.Count == 0) c.Hotkeys.Add("Ctrl+Alt+Shift+F10");
            return c;
        }

        static int ParseInt(string s, int fallback)
        {
            s = s.Trim();
            try
            {
                if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                    return int.Parse(s.Substring(2), NumberStyles.HexNumber);
                return int.Parse(s, CultureInfo.InvariantCulture);
            }
            catch { return fallback; }
        }

        /// <summary>"Ctrl+Alt+Shift+F10" -> modificadors + codi de tecla virtual.</summary>
        public static bool ParseHotkey(string text, out uint modifiers, out uint vk)
        {
            modifiers = Native.MOD_NOREPEAT;
            vk = 0;
            string[] parts = text.Split('+');
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": case "control": modifiers |= Native.MOD_CONTROL; break;
                    case "alt": modifiers |= Native.MOD_ALT; break;
                    case "shift": modifiers |= Native.MOD_SHIFT; break;
                    case "win": case "windows": modifiers |= Native.MOD_WIN; break;
                    default:
                        try { vk = (uint)(Keys)Enum.Parse(typeof(Keys), p, true); }
                        catch { return false; }
                        break;
                }
            }
            return vk != 0;
        }
    }

    static class InputControl
    {
        // L'Evnia pot conservar l'ultim 0x60 escrit despres del retorn automatic.
        // Per sobre del limit de la consola, el senyal actiu nomes pot ser el PC.
        public static int Current(IntPtr h, Config cfg)
        {
            int hz100;
            return Current(h, cfg, out hz100);
        }

        static int Current(IntPtr h, Config cfg, out int hz100)
        {
            int source = Ddc.Read(h, Native.VCP_INPUT_SOURCE);
            hz100 = cfg.SizeEnabled ? Ddc.Read(h, Native.VCP_VERTICAL_FREQUENCY) : -1;
            if (cfg.PcOnlyAboveHz > 0 && hz100 > cfg.PcOnlyAboveHz * 100 && hz100 <= 18000 &&
                (source == cfg.InputA || source == cfg.InputB || source < 0)) return cfg.InputA;
            return source;
        }

        // El canvi de senyal pot invalidar el handle i restablir la mida.
        // Obre el monitor de nou i espera un senyal estable abans d'escriure.
        static bool FinishSwitch(Config cfg, int target, out int active)
        {
            active = -1;
            int size = target == cfg.InputA ? cfg.ScaleA : cfg.ScaleB;
            bool primeZoom = target == cfg.InputB && size == 8 &&
                string.Equals(cfg.MonitorId, "PHLC29F", StringComparison.OrdinalIgnoreCase);

            // El mode 8 pot estar seleccionat sense haver aplicat la mida de 27".
            // Amb HDMI actiu, aplica 1:1, confirma'l i deixa acabar el canvi.
            if (primeZoom && !ApplySizeOnInput(cfg, target, 1, true, out active))
                return false;
            if (primeZoom) Thread.Sleep(2000);
            return ApplySizeOnInput(cfg, target, size, primeZoom, out active);
        }

        static bool ApplySizeOnInput(Config cfg, int target, int size, bool forceWrite, out int active)
        {
            active = -1;
            int stable = 0, previousHz = -1;
            bool sent = false;
            DateTime nextWrite = DateTime.MinValue;
            DateTime deadline = DateTime.UtcNow.AddSeconds(17);
            while (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(400);
                IntPtr handle;
                string device;
                if (!Ddc.TryOpen(cfg.MonitorId, out handle, out device)) continue;
                try
                {
                    int hz;
                    active = Current(handle, cfg, out hz);
                    if (active < 0 || hz < 0) continue;
                    if (active != target || hz < 2000 || hz > 24000)
                    {
                        stable = 0;
                        sent = false;
                        continue;
                    }
                    if (Math.Abs(previousHz - hz) > 100) stable = 0;
                    previousHz = hz;
                    if (++stable < 2) continue;
                    int actual = Ddc.Read(handle, Native.VCP_SCALING);
                    if (actual == size && (!forceWrite || sent)) return true;
                    if (DateTime.UtcNow < nextWrite || (sent && actual < 0)) continue;
                    if (Ddc.Write(handle, Native.VCP_SCALING, size))
                    {
                        sent = true;
                        nextWrite = DateTime.UtcNow.AddSeconds(3);
                        Thread.Sleep(850);
                    }
                }
                finally { Native.DestroyPhysicalMonitor(handle); }
            }
            return false;
        }

        public static bool Run(IntPtr h, Config cfg, int? requested, out int result, out bool repaired, out string error)
        {
            result = Current(h, cfg);
            repaired = false;
            error = "";
            if (cfg.SizeEnabled && !requested.HasValue)
            {
                int size = -1;
                // Durant la negociacio HDMI, el monitor pot respondre nomes a una
                // de les dues consultes. Pren una parella valida abans de decidir.
                for (int i = 0; i < 9; i++)
                {
                    int hz;
                    result = Current(h, cfg, out hz);
                    size = Ddc.Read(h, Native.VCP_SCALING);
                    if ((result == cfg.InputA || result == cfg.InputB) && size >= 0 && hz > 0) break;
                    size = -1;
                    Thread.Sleep(300);
                }
                if (size < 0)
                {
                    error = "No es pot llegir una entrada amb senyal i una mida estables.";
                    return false;
                }
                int expected = result == cfg.InputA ? cfg.ScaleA : cfg.ScaleB;
                if (size != expected)
                {
                    repaired = true;
                    error = "No es pot confirmar la mida.";
                    int currentInput = result;
                    return FinishSwitch(cfg, currentInput, out result);
                }
            }

            int target = requested ?? (result == cfg.InputB ? cfg.InputA : cfg.InputB);
            if (target != result || requested.HasValue)
            {
                if (!Ddc.Write(h, Native.VCP_INPUT_SOURCE, target))
                {
                    error = "El monitor ha rebutjat el canvi d'entrada.";
                    return false;
                }
            }
            if (cfg.SizeEnabled && (target == cfg.InputA || target == cfg.InputB))
            {
                Thread.Sleep(1600);
                if (!FinishSwitch(cfg, target, out result))
                {
                    error = "No es pot confirmar el senyal i la mida de l'entrada de desti.";
                    return false;
                }
            }
            result = target;
            return true;
        }
    }

    class TrayApp : Form
    {
        Config cfg;
        NotifyIcon tray;
        ToolStripMenuItem itemState, itemStartup, itemMute;
        List<Action> hotkeyActions = new List<Action>();
        DateTime lastSwitch = DateTime.MinValue;
        int assumedInput = -1;
        int assumedMute = -1;

        public TrayApp()
        {
            cfg = Config.Load();

            tray = new NotifyIcon();
            tray.Visible = true;
            tray.MouseClick += TrayClicked;

            ContextMenuStrip menu = new ContextMenuStrip();
            ToolStripMenuItem toggle = new ToolStripMenuItem("Canvia d'entrada", null, delegate { Toggle(); });
            toggle.Font = new Font(toggle.Font, FontStyle.Bold);
            menu.Items.Add(toggle);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(new ToolStripMenuItem("Ves a " + cfg.NameA, null, delegate { SwitchTo(cfg.InputA); }));
            menu.Items.Add(new ToolStripMenuItem("Ves a " + cfg.NameB, null, delegate { SwitchTo(cfg.InputB); }));
            menu.Items.Add(new ToolStripSeparator());
            itemMute = new ToolStripMenuItem("Silenci de la pantalla", null, delegate { ToggleMute(); });
            menu.Items.Add(itemMute);
            menu.Items.Add(new ToolStripSeparator());
            itemState = new ToolStripMenuItem("(estat desconegut)");
            itemState.Enabled = false;
            menu.Items.Add(itemState);
            menu.Items.Add(new ToolStripSeparator());
            itemStartup = new ToolStripMenuItem("Arrenca amb Windows", null, delegate { ToggleStartup(); });
            itemStartup.Checked = IsStartupEnabled();
            menu.Items.Add(itemStartup);
            menu.Items.Add(new ToolStripMenuItem("Obre la configuracio", null, delegate { OpenConfig(); }));
            menu.Items.Add(new ToolStripMenuItem("Surt", null, delegate { Application.Exit(); }));
            tray.ContextMenuStrip = menu;

            RegisterHotkeys();
            RefreshState();
        }

        // El formulari nomes existeix per tenir un handle on rebre WM_HOTKEY: mai es mostra.
        protected override void SetVisibleCore(bool value) { base.SetVisibleCore(false); }

        void RegisterHotkeys()
        {
            List<string> failed = new List<string>();
            RegisterGroup(cfg.Hotkeys, new Action(Toggle), failed);
            RegisterGroup(cfg.MuteHotkeys, new Action(ToggleMute), failed);
            if (failed.Count > 0)
                Balloon("Dreceres no registrades", string.Join("\n", failed.ToArray()), ToolTipIcon.Warning);
        }

        void RegisterGroup(List<string> hotkeys, Action action, List<string> failed)
        {
            foreach (string hk in hotkeys)
            {
                uint mods, vk;
                if (!Config.ParseHotkey(hk, out mods, out vk)) { failed.Add(hk + " (no s'enten)"); continue; }
                if (!Native.RegisterHotKey(Handle, hotkeyActions.Count, mods, vk)) { failed.Add(hk + " (ja la fa servir un altre programa)"); continue; }
                hotkeyActions.Add(action);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                int id = m.WParam.ToInt32();
                if (id >= 0 && id < hotkeyActions.Count) hotkeyActions[id]();
            }
            base.WndProc(ref m);
        }

        void TrayClicked(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Toggle();
        }

        void Toggle()
        {
            // El monitor triga a canviar; ignora rafegues de pulsacions.
            if ((DateTime.Now - lastSwitch).TotalMilliseconds < 1500) return;

            IntPtr h;
            string dev;
            if (!Ddc.TryOpen(cfg.MonitorId, out h, out dev))
            {
                Balloon("No trobo el monitor", "Cap pantalla amb l'EDID '" + cfg.MonitorId + "'.", ToolTipIcon.Error);
                return;
            }
            try
            {
                Apply(h, null);
            }
            finally { Native.DestroyPhysicalMonitor(h); }
        }

        /// <summary>Silencia o desilencia els altaveus del monitor (VCP 0x8D).</summary>
        void ToggleMute()
        {
            IntPtr h;
            string dev;
            if (!Ddc.TryOpen(cfg.MonitorId, out h, out dev))
            {
                Balloon("No trobo el monitor", "Cap pantalla amb l'EDID '" + cfg.MonitorId + "'.", ToolTipIcon.Error);
                return;
            }
            try
            {
                // Si la lectura falla no es pot deduir l'estat: cal el record, o el
                // toggle es queda encallat silenciant una vegada i una altra.
                int current = Ddc.Read(h, Native.VCP_AUDIO_MUTE);
                if (current < 0) current = assumedMute;
                int target = (current == cfg.MuteOn) ? cfg.MuteOff : cfg.MuteOn;
                if (Ddc.Write(h, Native.VCP_AUDIO_MUTE, target))
                {
                    assumedMute = target;
                    itemMute.Checked = target == cfg.MuteOn;
                    if (cfg.Notify) Balloon("Pantalla", target == cfg.MuteOn ? "silenciada" : "amb so", ToolTipIcon.None);
                }
                else Balloon("El monitor ha rebutjat el canvi", "VCP 0x8D = " + target, ToolTipIcon.Error);
            }
            finally { Native.DestroyPhysicalMonitor(h); }
        }

        void SwitchTo(int input)
        {
            IntPtr h;
            string dev;
            if (!Ddc.TryOpen(cfg.MonitorId, out h, out dev))
            {
                Balloon("No trobo el monitor", "Cap pantalla amb l'EDID '" + cfg.MonitorId + "'.", ToolTipIcon.Error);
                return;
            }
            try { Apply(h, input); }
            finally { Native.DestroyPhysicalMonitor(h); }
        }

        void Apply(IntPtr h, int? target)
        {
            int input;
            bool repaired;
            string error;
            bool ok = InputControl.Run(h, cfg, target, out input, out repaired, out error);
            lastSwitch = DateTime.Now;
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Config.Path), "last-action.txt");
                File.WriteAllText(path, DateTime.Now.ToString("s") + " input=" + input +
                    " repair=" + repaired + " ok=" + ok + (ok ? "" : " " + error));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            assumedInput = input;
            UpdateIcon(input);
            if (!ok) Balloon("Control de pantalla", error, ToolTipIcon.Warning);
            else if (cfg.Notify) Balloon(repaired ? "Mida corregida" : "Entrada canviada", NameOf(input), ToolTipIcon.None);
        }

        void RefreshState()
        {
            IntPtr h;
            string dev;
            if (!Ddc.TryOpen(cfg.MonitorId, out h, out dev)) { UpdateIcon(-1); return; }
            try
            {
                assumedInput = InputControl.Current(h, cfg);
                assumedMute = Ddc.Read(h, Native.VCP_AUDIO_MUTE);
                itemMute.Checked = assumedMute == cfg.MuteOn;
            }
            finally { Native.DestroyPhysicalMonitor(h); }
            UpdateIcon(assumedInput);
        }

        string NameOf(int input)
        {
            if (input == cfg.InputA) return cfg.NameA;
            if (input == cfg.InputB) return cfg.NameB;
            return "entrada " + input;
        }

        void UpdateIcon(int input)
        {
            Color fill = input == cfg.InputA ? Color.FromArgb(80, 170, 255)
                       : input == cfg.InputB ? Color.FromArgb(255, 150, 60)
                       : Color.FromArgb(150, 150, 150);
            tray.Icon = MakeIcon(fill);
            tray.Text = "Entrada del monitor: " + NameOf(input);
            itemState.Text = "Ultima entrada escrita: " + NameOf(input);
        }

        /// <summary>Dibuixa una pantalleta del color de l'entrada activa.</summary>
        static Icon MakeIcon(Color fill)
        {
            using (Bitmap bmp = new Bitmap(16, 16))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (Brush b = new SolidBrush(fill))
                        g.FillRectangle(b, 1, 2, 14, 10);
                    using (Pen p = new Pen(Color.FromArgb(230, 245, 245, 245), 1))
                    {
                        g.DrawRectangle(p, 1, 2, 13, 9);
                        g.DrawLine(p, 6, 13, 10, 13);
                    }
                }
                return Icon.FromHandle(bmp.GetHicon());
            }
        }

        void Balloon(string title, string text, ToolTipIcon icon)
        {
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = text;
            tray.BalloonTipIcon = icon;
            tray.ShowBalloonTip(2000);
        }

        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string RunName = "MonitorInputToggle";

        static bool IsStartupEnabled()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey))
                return k != null && k.GetValue(RunName) != null;
        }

        void ToggleStartup()
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
            {
                if (k == null) return;
                if (IsStartupEnabled()) { k.DeleteValue(RunName, false); itemStartup.Checked = false; }
                else
                {
                    k.SetValue(RunName, "\"" + Assembly.GetExecutingAssembly().Location + "\"");
                    itemStartup.Checked = true;
                }
            }
        }

        void OpenConfig()
        {
            if (!File.Exists(Config.Path)) return;
            System.Diagnostics.Process.Start("notepad.exe", "\"" + Config.Path + "\"");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                for (int i = 0; i < hotkeyActions.Count; i++) Native.UnregisterHotKey(Handle, i);
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
            }
            base.Dispose(disposing);
        }

        [STAThread]
        static void Main(string[] args)
        {
            Config cfg = Config.Load();

            // Mode d'un sol tret: "InputToggle.exe toggle|a|b|<numero>" canvia i plega.
            if (args.Length > 0)
            {
                IntPtr h;
                string dev;
                if (!Ddc.TryOpen(cfg.MonitorId, out h, out dev)) { Environment.ExitCode = 2; return; }
                try
                {
                    int target;
                    string a = args[0].ToLowerInvariant();
                    if (a == "mute")
                    {
                        int curMute = Ddc.Read(h, Native.VCP_AUDIO_MUTE);
                        int muteTarget = (curMute == cfg.MuteOn) ? cfg.MuteOff : cfg.MuteOn;
                        Environment.ExitCode = Ddc.Write(h, Native.VCP_AUDIO_MUTE, muteTarget) ? 0 : 1;
                        return;
                    }
                    if (a == "a") target = cfg.InputA;
                    else if (a == "b") target = cfg.InputB;
                    else if (a == "toggle") target = -1;
                    else if (!int.TryParse(a, out target)) { Environment.ExitCode = 3; return; }
                    int input;
                    bool repaired;
                    string error;
                    Environment.ExitCode = InputControl.Run(h, cfg, a == "toggle" ? (int?)null : target,
                        out input, out repaired, out error) ? 0 : 1;
                }
                finally { Native.DestroyPhysicalMonitor(h); }
                return;
            }

            bool isNew;
            using (Mutex mutex = new Mutex(true, "MonitorInputToggle.SingleInstance", out isNew))
            {
                if (!isNew) return;
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayApp());
            }
        }
    }
}
