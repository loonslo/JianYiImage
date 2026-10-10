# Drives a real JianYiImage window (real mouse + keyboard, with the user's permission) and saves screenshots.
# ASCII only on purpose: Windows PowerShell 5.1 reads BOM-less UTF-8 scripts as GBK. Chinese text is never needed here;
# targets arrive as numbers in a plan file computed by plan.py.
param(
    [Parameter(Mandatory)][string]$Exe,
    [Parameter(Mandatory)][string]$Image,
    [Parameter(Mandatory)][string]$OutDir,
    [Parameter(Mandatory)][string]$Scenario,
    [string]$Plan = '',
    [int]$Width = 1440,
    [int]$Height = 900,
    [int]$Left = 40,
    [int]$Top = 30
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public static class Win {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, int dx, int dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr e);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr h);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out RECT r, int size);

    // The visible frame (without the invisible resize border) expressed in the coordinates of the Capture() bitmap.
    public static Rectangle VisibleFrame(IntPtr h) {
        RECT w; GetWindowRect(h, out w);
        RECT f; DwmGetWindowAttribute(h, 9, out f, Marshal.SizeOf(typeof(RECT)));
        return new Rectangle(f.Left - w.Left, f.Top - w.Top, f.Right - f.Left, f.Bottom - f.Top);
    }

    public static Bitmap Capture(IntPtr h) {
        RECT r; GetWindowRect(h, out r);
        var bmp = new Bitmap(r.Right - r.Left, r.Bottom - r.Top);
        using (var g = Graphics.FromImage(bmp)) {
            IntPtr dc = g.GetHdc();
            try { PrintWindow(h, dc, 2); } finally { g.ReleaseHdc(dc); }
        }
        return bmp;
    }

    public static Bitmap CaptureScreen(int x, int y, int w, int h) {
        var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp)) { g.CopyFromScreen(x, y, 0, 0, new Size(w, h)); }
        return bmp;
    }

    // Bounding box of the pixels inside 'area' that differ from 'bg' by more than 'tol' (max channel difference).
    public static Rectangle FindContent(Bitmap bmp, Rectangle area, Color bg, int tol) {
        area.Intersect(new Rectangle(0, 0, bmp.Width, bmp.Height));
        var data = bmp.LockBits(area, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        try {
            var row = new byte[data.Stride];
            for (int y = 0; y < area.Height; y++) {
                Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, data.Stride);
                for (int x = 0; x < area.Width; x++) {
                    int i = x * 4;
                    int d = Math.Max(Math.Abs(row[i] - bg.B), Math.Max(Math.Abs(row[i + 1] - bg.G), Math.Abs(row[i + 2] - bg.R)));
                    if (d > tol) {
                        if (x < minX) minX = x; if (x > maxX) maxX = x;
                        if (y < minY) minY = y; if (y > maxY) maxY = y;
                    }
                }
            }
        } finally { bmp.UnlockBits(data); }
        if (maxX < 0) return Rectangle.Empty;
        return new Rectangle(area.X + minX, area.Y + minY, maxX - minX + 1, maxY - minY + 1);
    }
}
'@
[void][Win]::SetProcessDPIAware()
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]
$OcrReady = -join ([char[]](0x6587, 0x5b57, 0x5df2, 0x8bc6, 0x522b))       # the status text shown when OCR has finished
$LangMenu = -join ([char[]](0x8bc6, 0x522b, 0x8bed, 0x8a00))               # the "recognition language" menu entry

function Find-ById($root, [string]$id) {
    return $root.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, $id)))
}
function Find-ByName($root, [string]$name) {
    return $root.FindFirst($TS::Descendants, (New-Object $PC($AE::NameProperty, $name)))
}
function Invoke-Element($el) { $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
function Pause([int]$ms) { Start-Sleep -Milliseconds $ms }
function Capture-Frame {
    $vf = [Win]::VisibleFrame($hwnd)
    $r = New-Object Win+RECT
    [void][Win]::GetWindowRect($hwnd, [ref]$r)
    return [Win]::CaptureScreen(($r.Left + $vf.X), ($r.Top + $vf.Y), $vf.Width, $vf.Height)
}
function Save-Png($bmp, [string]$name) {
    $path = Join-Path $OutDir ($name + '.png')
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    return $path
}
$screenSize = [System.Windows.Forms.SystemInformation]::PrimaryMonitorSize
# SetCursorPos only moves the cursor and produces no input event, so WinUI never sees the drag.
# An absolute mouse_event (MOVE | ABSOLUTE = 0x8001, coordinates 0..65535) goes through the real input path.
function Move-Mouse([double]$x, [double]$y) {
    $nx = [int][math]::Round($x * 65535.0 / ($screenSize.Width - 1))
    $ny = [int][math]::Round($y * 65535.0 / ($screenSize.Height - 1))
    [Win]::mouse_event(0x8001, $nx, $ny, 0, [UIntPtr]::Zero)
}
function Mouse-Down { [Win]::mouse_event(0x0002, 0, 0, 0, [UIntPtr]::Zero) }
function Mouse-Up { [Win]::mouse_event(0x0004, 0, 0, 0, [UIntPtr]::Zero) }
function Right-Click { [Win]::mouse_event(0x0008, 0, 0, 0, [UIntPtr]::Zero); Pause 60; [Win]::mouse_event(0x0010, 0, 0, 0, [UIntPtr]::Zero) }
function Key-Down([byte]$vk) { [Win]::keybd_event($vk, 0, 0, [UIntPtr]::Zero) }
function Key-Up([byte]$vk) { [Win]::keybd_event($vk, 0, 2, [UIntPtr]::Zero) }
function Press-Chord([byte]$modifier, [byte]$key) {
    Key-Down $modifier; Pause 40; Key-Down $key; Pause 40; Key-Up $key; Pause 40; Key-Up $modifier
}

# ---- start the app ----
$proc = Start-Process -FilePath $Exe -ArgumentList ('"' + $Image + '"') -PassThru
try {
    for ($i = 0; $i -lt 80 -and $proc.MainWindowHandle -eq [IntPtr]::Zero; $i++) { Pause 250; $proc.Refresh() }
    if ($proc.MainWindowHandle -eq [IntPtr]::Zero) { throw 'no main window' }
    $hwnd = $proc.MainWindowHandle
    [void][Win]::ShowWindow($hwnd, 9)
    # HWND_TOPMOST = -1, SWP_SHOWWINDOW = 0x40: keep the window above everything so the real mouse hits it
    [void][Win]::SetWindowPos($hwnd, [IntPtr](-1), $Left, $Top, $Width, $Height, 0x40)
    Pause 700
    [void][Win]::SetForegroundWindow($hwnd)
    $win = $AE::FromHandle($hwnd)

    # wait for OCR
    $ocr = Find-ById $win 'OcrStatusText'
    $ready = $false
    for ($i = 0; $i -lt 60; $i++) {
        if ($ocr.Current.Name -eq $OcrReady) { $ready = $true; break }
        Pause 250
    }
    if (-not $ready) { throw 'OCR never became ready' }
    Pause 500

    $rect = New-Object Win+RECT
    [void][Win]::GetWindowRect($hwnd, [ref]$rect)
    $meta = [ordered]@{ scenario = $Scenario; window = @{ left = $rect.Left; top = $rect.Top; width = ($rect.Right - $rect.Left); height = ($rect.Bottom - $rect.Top) }; dpi = [Win]::GetDpiForWindow($hwnd); visibleFrame = @{ x = [Win]::VisibleFrame($hwnd).X; y = [Win]::VisibleFrame($hwnd).Y; w = [Win]::VisibleFrame($hwnd).Width; h = [Win]::VisibleFrame($hwnd).Height } }

    # Locate the displayed image on the dark canvas (calibrates the image->screen mapping for the current layout)
    function Get-ImageRect([bool]$panelAtRight) {
        $shot = [Win]::Capture($hwnd)
        $vf = [Win]::VisibleFrame($hwnd)
        # viewport = visible frame minus title bar (32), toolbar (44), status bar (~30) and the side panel (320) when open
        $viewLeft = $vf.X + 1
        $viewTop = $vf.Y + 32 + 44 + 1
        $viewRight = $vf.X + $vf.Width - 1
        if ($panelAtRight) { $viewRight = $viewRight - 321 }
        $viewBottom = $vf.Y + $vf.Height - 31
        $bgColor = $shot.GetPixel($viewLeft + 14, $viewTop + 10)
        # skip the band where the round previous/next buttons float, so they cannot widen the box
        $midY = [int](($viewTop + $viewBottom) / 2)
        $upper = New-Object System.Drawing.Rectangle($viewLeft, $viewTop, ($viewRight - $viewLeft), ($midY - 36 - $viewTop))
        $lower = New-Object System.Drawing.Rectangle($viewLeft, ($midY + 36), ($viewRight - $viewLeft), ($viewBottom - $midY - 36))
        $a = [Win]::FindContent($shot, $upper, $bgColor, 14)
        $b = [Win]::FindContent($shot, $lower, $bgColor, 14)
        $shot.Dispose()
        if ($a.IsEmpty) { return $b }
        if ($b.IsEmpty) { return $a }
        return [System.Drawing.Rectangle]::Union($a, $b)
    }
    function To-Screen($imgRect, $imgW, $imgH, [double]$ix, [double]$iy) {
        $scale = $imgRect.Width / $imgW
        return @(($rect.Left + $imgRect.X + $ix * $scale), ($rect.Top + $imgRect.Y + $iy * $scale))
    }

    $planObj = $null
    if ($Plan) { $planObj = Get-Content -Raw -Encoding UTF8 $Plan | ConvertFrom-Json }

    function Do-Drag($imgRect, $drag, [bool]$withShift, [bool]$captureFrames) {
        $s = To-Screen $imgRect $planObj.imageWidth $planObj.imageHeight $drag.x0 $drag.y0
        $e = To-Screen $imgRect $planObj.imageWidth $planObj.imageHeight $drag.x1 $drag.y1
        $frames = @()
        Move-Mouse ($s[0] - 40) ($s[1] + 30); Pause 250
        Move-Mouse $s[0] $s[1]; Pause 250
        if ($captureFrames) { $frames += ,@{ n = 'f000'; x = $s[0]; y = $s[1]; shot = (Save-Png (Capture-Frame) 'f000') } }
        if ($withShift) { Key-Down 0x10; Pause 80 }
        Mouse-Down; Pause 120
        $steps = [int]$drag.steps
        for ($k = 1; $k -le $steps; $k++) {
            $t = $k / $steps
            $t = $t * $t * (3 - 2 * $t)            # smoothstep easing
            $x = $s[0] + ($e[0] - $s[0]) * $t
            $y = $s[1] + ($e[1] - $s[1]) * $t
            Move-Mouse $x $y
            Pause 25
            if ($captureFrames) {
                $name = 'f{0:d3}' -f $k
                $frames += ,@{ n = $name; x = $x; y = $y; shot = (Save-Png (Capture-Frame) $name) }
            }
        }
        Pause 150
        Mouse-Up
        if ($withShift) { Key-Up 0x10 }
        Pause 500
        return $frames
    }

    switch ($Scenario) {
        'baseline' {
            Save-Png ([Win]::Capture($hwnd)) 'baseline' | Out-Null
        }
        'selectall' {
            [void][Win]::SetForegroundWindow($hwnd); Pause 300
            Move-Mouse ($rect.Left + 300) ($rect.Top + 400); Pause 200
            Mouse-Down; Pause 60; Mouse-Up; Pause 300      # focus the canvas with a plain click
            Press-Chord 0x11 0x41; Pause 700
            Save-Png ([Win]::Capture($hwnd)) 'selectall' | Out-Null
        }
        'probe' {
            $img = Get-ImageRect $false
            $meta.imageRect = @{ x = $img.X; y = $img.Y; w = $img.Width; h = $img.Height }
            [void][Win]::SetForegroundWindow($hwnd); Pause 200
            $i = 0
            foreach ($d in $planObj.drags) {
                [void](Do-Drag $img $d $false $false)
                Save-Png ([Win]::Capture($hwnd)) ('probe-' + $i + '-' + $d.label) | Out-Null
                Key-Down 0x1B; Pause 60; Key-Up 0x1B; Pause 300
                $i++
            }
        }
        'narrow' {
            # in a narrow window the "extract text" button has moved into the overflow menu
            $btn = Find-ById $win 'ExtractTextButton'
            if ($null -ne $btn -and -not $btn.Current.IsOffscreen) { Invoke-Element $btn }
            else {
                Invoke-Element (Find-ById $win 'OverflowButton'); Pause 900
                $label = -join ([char[]](0x63d0, 0x53d6, 0x6587, 0x5b57))
                $item = $null
                for ($i = 0; $i -lt 20 -and $null -eq $item; $i++) { $item = Find-ByName $win $label; if ($null -eq $item) { Pause 250 } }
                if ($null -eq $item) { throw 'extract entry not found in the overflow menu' }
                Invoke-Element $item
            }
            Pause 1500
            Save-Png ([Win]::Capture($hwnd)) 'narrow' | Out-Null
        }
        'panel' {
            Invoke-Element (Find-ById $win 'ExtractTextButton'); Pause 1200
            Save-Png ([Win]::Capture($hwnd)) 'panel' | Out-Null
        }
        'hero' {
            Invoke-Element (Find-ById $win 'ExtractTextButton'); Pause 1200
            $img = Get-ImageRect $true
            $meta.imageRect = @{ x = $img.X; y = $img.Y; w = $img.Width; h = $img.Height }
            [void](Do-Drag $img $planObj.drag $false $false)
            Pause 400
            Save-Png ([Win]::Capture($hwnd)) 'hero' | Out-Null
        }
        'select' {
            $img = Get-ImageRect $false
            $meta.imageRect = @{ x = $img.X; y = $img.Y; w = $img.Width; h = $img.Height }
            [void][Win]::SetForegroundWindow($hwnd); Pause 200
            $frames = Do-Drag $img $planObj.drag $false $true
            # hold a beat, then Ctrl+C so the status bar confirms the copy
            Save-Png (Capture-Frame) 'sel-hold' | Out-Null
            Press-Chord 0x11 0x43
            Pause 500
            Save-Png (Capture-Frame) 'sel-copied' | Out-Null
            $meta.frames = $frames
        }
        'region' {
            $img = Get-ImageRect $false
            $meta.imageRect = @{ x = $img.X; y = $img.Y; w = $img.Width; h = $img.Height }
            [void][Win]::SetForegroundWindow($hwnd); Pause 200
            [void](Do-Drag $img $planObj.drag $true $false)
            Save-Png ([Win]::Capture($hwnd)) 'region' | Out-Null
        }
        'selection' {
            $img = Get-ImageRect $false
            $meta.imageRect = @{ x = $img.X; y = $img.Y; w = $img.Width; h = $img.Height }
            [void][Win]::SetForegroundWindow($hwnd); Pause 200
            [void](Do-Drag $img $planObj.drag $false $false)
            Save-Png ([Win]::Capture($hwnd)) 'selection' | Out-Null
        }
        'menu' {
            $img = Get-ImageRect $false
            $meta.imageRect = @{ x = $img.X; y = $img.Y; w = $img.Width; h = $img.Height }
            [void][Win]::SetForegroundWindow($hwnd); Pause 200
            # select the word first, then right-click it so the text actions appear in the menu
            [void](Do-Drag $img $planObj.drag $false $false)
            $p = To-Screen $img $planObj.imageWidth $planObj.imageHeight $planObj.click.x $planObj.click.y
            Move-Mouse $p[0] $p[1]; Pause 300
            Right-Click; Pause 900
            $item = $null
            for ($i = 0; $i -lt 20 -and $null -eq $item; $i++) { $item = Find-ByName $win $LangMenu; if ($null -eq $item) { Pause 250 } }
            if ($null -eq $item) { throw 'language menu entry not found' }
            $item.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            Pause 1000
            Move-Mouse ($rect.Left + $Width - 10) ($rect.Top + 80)      # park the cursor out of the way
            Pause 200
            Save-Png ([Win]::CaptureScreen($rect.Left, $rect.Top, $Width, $Height)) 'menu' | Out-Null
        }
        'overflow' {
            Invoke-Element (Find-ById $win 'OverflowButton'); Pause 900
            $item = $null
            for ($i = 0; $i -lt 20 -and $null -eq $item; $i++) { $item = Find-ByName $win $LangMenu; if ($null -eq $item) { Pause 250 } }
            if ($null -eq $item) { throw 'language menu entry not found' }
            $item.GetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
            Pause 1000
            Save-Png ([Win]::CaptureScreen($rect.Left, $rect.Top, $Width, $Height)) 'overflow' | Out-Null
        }
        default { throw "unknown scenario $Scenario" }
    }

    ($meta | ConvertTo-Json -Depth 6) | Set-Content -Encoding UTF8 (Join-Path $OutDir ($Scenario + '.meta.json'))
    'done: ' + $Scenario
}
finally {
    if (-not $proc.HasExited) { [void]$proc.CloseMainWindow(); Start-Sleep -Milliseconds 800 }
    if (-not $proc.HasExited) { Stop-Process -Id $proc.Id -Force }
}
