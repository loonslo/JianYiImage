# Walks the installer wizard with Win32 messages (no real mouse) and saves a PNG of every page up to "ready to install".
# It never presses Install, so nothing is installed and no administrator prompt can appear.
#
#   powershell -File capture-installer.ps1 -Setup <setup.exe> -Dir <a folder that will stay empty> -OutDir <folder for the PNGs>
#
# ASCII only on purpose (Windows PowerShell 5.1 reads BOM-less UTF-8 scripts as the ANSI code page).
param([string]$Setup, [string]$Dir, [string]$OutDir)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
public static class W32 {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr p, EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool IsWindowEnabled(IntPtr h);
    [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool SetFocus(IntPtr h);

    public class Child { public IntPtr Handle; public string Class; public string Text; public bool Visible; public bool Enabled; }
    public static List<Child> Children(IntPtr parent) {
        var list = new List<Child>();
        EnumChildWindows(parent, (h, l) => {
            var cls = new StringBuilder(256); GetClassName(h, cls, 256);
            var txt = new StringBuilder(512); GetWindowText(h, txt, 512);
            list.Add(new Child { Handle = h, Class = cls.ToString(), Text = txt.ToString(), Visible = IsWindowVisible(h), Enabled = IsWindowEnabled(h) });
            return true;
        }, IntPtr.Zero);
        return list;
    }
    public static Bitmap Capture(IntPtr h) {
        RECT r; GetWindowRect(h, out r);
        var bmp = new Bitmap(r.Right - r.Left, r.Bottom - r.Top);
        using (var g = Graphics.FromImage(bmp)) { IntPtr dc = g.GetHdc(); try { PrintWindow(h, dc, 2); } finally { g.ReleaseHdc(dc); } }
        return bmp;
    }
}
'@
[void][W32]::SetProcessDPIAware()
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
function N([int[]]$cps) { -join ([char[]]$cps) }
function Esc([string]$n) { -join ($n.ToCharArray() | ForEach-Object { if ([int]$_ -gt 127) { '\u{0:x4}' -f [int]$_ } else { [string]$_ } }) }
$nextText = N 0x4e0b, 0x4e00, 0x6b65         # "next"
$cancelText = N 0x53d6, 0x6d88                # "cancel"

$args2 = @('/CURRENTUSER', ('/DIR="' + $Dir + '"'))
$p = Start-Process -FilePath $Setup -ArgumentList $args2 -PassThru
$child = $null
try {
    $hwnd = [IntPtr]::Zero
    for ($i = 0; $i -lt 120 -and $hwnd -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 250
        $child = Get-CimInstance Win32_Process | Where-Object { $_.Name -like 'JianYiImage-v*setup.tmp' -and $_.ExecutablePath -like ($env:TEMP + '\is-*') } | Select-Object -First 1
        if ($child) {
            $proc = Get-Process -Id ([int]$child.ProcessId) -ErrorAction SilentlyContinue
            if ($proc -and $proc.MainWindowHandle -ne [IntPtr]::Zero -and $proc.MainWindowTitle) { $hwnd = $proc.MainWindowHandle }
        }
    }
    if ($hwnd -eq [IntPtr]::Zero) { throw 'wizard window not found' }
    Start-Sleep -Milliseconds 1200

    function Shot([string]$name) {
        $bmp = [W32]::Capture($hwnd)
        $path = Join-Path $OutDir ($name + '.png')
        $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
        'saved ' + $name
    }
    function Click-Button([string]$contains) {
        foreach ($c in [W32]::Children($hwnd)) {
            if ($c.Visible -and $c.Enabled -and $c.Class -like '*Button*' -and $c.Text.Contains($contains)) {
                [void][W32]::SendMessage($c.Handle, 0x00F5, [IntPtr]::Zero, [IntPtr]::Zero)    # BM_CLICK
                return $true
            }
        }
        return $false
    }
    function List-Controls([string]$label) {
        '--- ' + $label + ' ---'
        foreach ($c in [W32]::Children($hwnd)) {
            if ($c.Visible) { '{0} | {1} | enabled={2}' -f $c.Class, (Esc $c.Text), $c.Enabled }
        }
    }

    # pages: install location -> components -> tasks -> ready. The Install button is never pressed.
    Shot 'p1-dir'
    [void](Click-Button $nextText); Start-Sleep -Milliseconds 2500      # components page: the language detection runs here
    Shot 'p2-components-recommended'
    List-Controls 'components'
    $combo = [W32]::Children($hwnd) | Where-Object { $_.Visible -and $_.Class -like '*ComboBox*' } | Select-Object -First 1
    if ($combo) {
        # Down twice = the third entry, "custom"
        foreach ($k in 1..2) { [void][W32]::SendMessage($combo.Handle, 0x0100, [IntPtr]0x28, [IntPtr]::Zero); Start-Sleep -Milliseconds 200; [void][W32]::SendMessage($combo.Handle, 0x0101, [IntPtr]0x28, [IntPtr]::Zero) }
        Start-Sleep -Milliseconds 800
        Shot 'p3-components-custom'
    } else { 'no combo box found' }
    [void](Click-Button $nextText); Start-Sleep -Milliseconds 1000
    Shot 'p4-tasks'
    [void](Click-Button $nextText); Start-Sleep -Milliseconds 1000
    Shot 'p5-ready'
}
finally {
    if ($child) { Stop-Process -Id ([int]$child.ProcessId) -Force -ErrorAction SilentlyContinue }
    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
}
