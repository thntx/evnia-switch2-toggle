# Compila InputToggle.cs amb el csc.exe que ja ve amb Windows (.NET Framework 4).
# No cal instal.lar res.

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "no trobo csc.exe a $csc" }

$out = Join-Path $dir 'InputToggle.exe'
$src = Join-Path $dir 'InputToggle.cs'

& $csc /nologo /target:winexe /optimize+ /platform:x64 `
  /reference:System.dll `
  /reference:System.Drawing.dll `
  /reference:System.Windows.Forms.dll `
  /out:"$out" "$src"

if ($LASTEXITCODE -ne 0) { throw "la compilacio ha fallat (codi $LASTEXITCODE)" }
Write-Output "compilat: $out"
