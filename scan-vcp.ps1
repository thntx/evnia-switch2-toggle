# Escaneja tots els registres VCP (0x00..0xFF) del monitor per DDC/CI i escriu
# el resultat a un fitxer, per poder comparar dos estats del monitor.
#
#   .\scan-vcp.ps1 -Out abans.txt
#   ... canvia alguna cosa des de l'OSD ...
#   .\scan-vcp.ps1 -Out despres.txt
#   Compare-Object (gc abans.txt) (gc despres.txt)

param(
  [string]$Out = "vcp-scan.txt",
  [string]$MonitorId = 'PHLC29F',
  [int]$DelayMs = 40
)

$src = @'
using System;
using System.Runtime.InteropServices;
public class Scan {
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct PM { public IntPtr h; [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string d; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct MI { public int cb; public int l, t, r, b; public int wl, wt, wr, wb; public uint f;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dev; }
  [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
  public struct DD { public int cb;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string name;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string str;
    public int flags;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string id;
    [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string key; }
  public delegate bool Proc(IntPtr h, IntPtr hdc, IntPtr r, IntPtr d);
  [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr a, IntPtr b, Proc p, IntPtr d);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr h, ref MI mi);
  [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string dev, uint n, ref DD dd, uint flags);
  [DllImport("dxva2.dll")] static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr h, out uint n);
  [DllImport("dxva2.dll")] static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr h, uint n, [Out] PM[] a);
  [DllImport("dxva2.dll")] public static extern bool DestroyPhysicalMonitor(IntPtr h);
  [DllImport("dxva2.dll")] public static extern bool GetVCPFeatureAndVCPFeatureReply(IntPtr h, byte c, out uint t, out uint cur, out uint max);

  static bool Matches(string display, string monitorId) {
    var dd = new DD(); dd.cb = Marshal.SizeOf(typeof(DD));
    for (uint i = 0; EnumDisplayDevices(display, i, ref dd, 0); i++) {
      if (dd.id != null && dd.id.IndexOf(monitorId, StringComparison.OrdinalIgnoreCase) >= 0) return true;
      dd.cb = Marshal.SizeOf(typeof(DD));
    }
    return false;
  }
  public static bool TryOpen(string monitorId, out IntPtr physical, out string device) {
    IntPtr res = IntPtr.Zero; string dev = null; bool ok = false;
    Proc p = delegate(IntPtr h, IntPtr hdc, IntPtr r, IntPtr d) {
      var mi = new MI(); mi.cb = Marshal.SizeOf(typeof(MI));
      if (!GetMonitorInfo(h, ref mi)) return true;
      if (!string.IsNullOrEmpty(monitorId) && !Matches(mi.dev, monitorId)) return true;
      uint n; if (!GetNumberOfPhysicalMonitorsFromHMONITOR(h, out n) || n == 0) return true;
      var a = new PM[n];
      if (!GetPhysicalMonitorsFromHMONITOR(h, n, a)) return true;
      res = a[0].h; dev = mi.dev; ok = true;
      for (int i = 1; i < n; i++) DestroyPhysicalMonitor(a[i].h);
      return false;
    };
    EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, p, IntPtr.Zero);
    physical = res; device = dev; return ok;
  }
}
'@
Add-Type -TypeDefinition $src -Language CSharp

$h = [IntPtr]::Zero
$dev = ''
if (-not [Scan]::TryOpen($MonitorId, [ref]$h, [ref]$dev)) { Write-Output "no trobo cap monitor amb l'EDID '$MonitorId'"; exit 1 }

$lines = @()
try {
  for ($c = 0; $c -le 255; $c++) {
    $t = 0; $cur = 0; $max = 0
    if ([Scan]::GetVCPFeatureAndVCPFeatureReply($h, [byte]$c, [ref]$t, [ref]$cur, [ref]$max)) {
      $lines += ("0x{0:X2}  val={1,-6} max={2,-6} type={3}" -f $c, $cur, $max, $t)
    }
    Start-Sleep -Milliseconds $DelayMs
  }
}
finally { [void][Scan]::DestroyPhysicalMonitor($h) }

$lines | Set-Content -Path $Out -Encoding utf8
Write-Output "$($lines.Count) registres responen. Guardat a $Out"
$lines | ForEach-Object { Write-Output $_ }
