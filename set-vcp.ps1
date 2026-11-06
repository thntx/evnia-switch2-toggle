# Llegeix o escriu qualsevol codi VCP del monitor per DDC/CI.
#
#   .\set-vcp.ps1 -Code 0x86            -> nomes llegeix
#   .\set-vcp.ps1 -Code 0x86 -Value 1   -> escriu (1 = No scaling)
#
# Ull: aquest monitor no actualitza els registres quan canvies coses des de
# l'OSD, aixi que la lectura pot ser rancia. Escriure sempre va be.

param(
  [Parameter(Mandatory = $true)] $Code,
  $Value = $null,
  [string]$MonitorId = 'PHLC29F'
)

$src = @'
using System;
using System.Runtime.InteropServices;
public class Vcp {
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
  [DllImport("dxva2.dll")] public static extern bool SetVCPFeature(IntPtr h, byte c, uint v);
  [DllImport("kernel32.dll")] public static extern uint GetLastError();

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

function ToInt($v) {
  if ($v -is [int]) { return $v }
  $s = [string]$v
  if ($s -match '^0[xX]') { return [Convert]::ToInt32($s.Substring(2), 16) }
  return [int]$s
}

[int]$codeInt = ToInt $Code
$h = [IntPtr]::Zero
$dev = ''
if (-not [Vcp]::TryOpen($MonitorId, [ref]$h, [ref]$dev)) { Write-Output "no trobo cap monitor amb l'EDID '$MonitorId'"; exit 1 }
Write-Output ("monitor '{0}' a {1} (handle {2})" -f $MonitorId, $dev, $h)

try {
  $t = 0; $cur = 0; $max = 0
  if ([Vcp]::GetVCPFeatureAndVCPFeatureReply($h, [byte]$codeInt, [ref]$t, [ref]$cur, [ref]$max)) {
    Write-Output ("  0x{0:X2} val actual (rancio) = {1}" -f $codeInt, $cur)
  } else {
    Write-Output ("  0x{0:X2} no es pot llegir (err=0x{1:X8})" -f $codeInt, [Vcp]::GetLastError())
  }

  if ($null -ne $Value) {
    $valInt = ToInt $Value
    if ([Vcp]::SetVCPFeature($h, [byte]$codeInt, $valInt)) {
      Write-Output ("  0x{0:X2} <- {1}  ACCEPTAT" -f $codeInt, $valInt)
    } else {
      Write-Output ("  0x{0:X2} <- {1}  REBUTJAT (err=0x{2:X8})" -f $codeInt, $valInt, [Vcp]::GetLastError())
      exit 1
    }
  }
}
finally { [void][Vcp]::DestroyPhysicalMonitor($h) }
