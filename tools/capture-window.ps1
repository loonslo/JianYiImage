param(
    [string]$Exe = '',
    [string]$Image = '',
    [string]$OutDir = (Join-Path $env:TEMP 'imageviewer-ui-refresh'),
    [string]$Name = 'shot',
    [int]$Width = 0,
    [int]$Height = 0,
    [switch]$Extract,
    [switch]$Attach,
    [string]$TitleContains = '',
    [switch]$DumpUia
)

$ErrorActionPreference = 'Stop'
if (($Width -lt 0) -or ($Height -lt 0) -or (($Width -eq 0) -ne ($Height -eq 0))) {
    throw 'Width and Height must both be positive or both be zero.'
}
if ([string]::IsNullOrWhiteSpace($Name) -or $Name -eq '.' -or $Name -eq '..' -or
    $Name.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {
    throw 'Name must be a single valid filename stem.'
}
$tempRoot = [IO.Path]::GetFullPath($env:TEMP).TrimEnd('\', '/')
$outputRoot = [IO.Path]::GetFullPath($OutDir).TrimEnd('\', '/')
if (-not $outputRoot.Equals($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
    -not $outputRoot.StartsWith($tempRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutDir must be inside TEMP.'
}
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$resolvedOutput = (Resolve-Path -LiteralPath $outputRoot).ProviderPath.TrimEnd('\', '/')
if (-not $resolvedOutput.Equals($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
    -not $resolvedOutput.StartsWith($tempRoot + '\', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Resolved OutDir must be inside TEMP.'
}

Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class ImageViewerCaptureNative {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr hwnd);
}
'@
[ImageViewerCaptureNative]::SetProcessDPIAware() | Out-Null

function ConvertTo-Ascii([string]$Value) {
    return -join ($Value.ToCharArray() | ForEach-Object {
        if ([int]$_ -gt 127) { '\u{0:x4}' -f [int]$_ } else { [string]$_ }
    })
}

function Save-Window([IntPtr]$Handle, [string]$Path) {
    $rect = New-Object ImageViewerCaptureNative+RECT
    if (-not [ImageViewerCaptureNative]::GetWindowRect($Handle, [ref]$rect)) {
        throw 'GetWindowRect failed.'
    }
    $captureWidth = $rect.Right - $rect.Left
    $captureHeight = $rect.Bottom - $rect.Top
    if ($captureWidth -le 0 -or $captureHeight -le 0) { throw 'Window has no drawable area.' }
    $bitmap = New-Object System.Drawing.Bitmap $captureWidth, $captureHeight
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try {
            $hdc = $graphics.GetHdc()
            try { $ok = [ImageViewerCaptureNative]::PrintWindow($Handle, $hdc, 2) }
            finally { $graphics.ReleaseHdc($hdc) }
            if (-not $ok) { throw 'PrintWindow failed.' }
        }
        finally { $graphics.Dispose() }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
    Write-Output ('saved ' + $Path + ' (' + $captureWidth + 'x' + $captureHeight + ')')
}

function Write-Buttons([System.Windows.Automation.AutomationElement]$Root) {
    $condition = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::Button)
    $buttons = $Root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
    $lines = @('AutomationId | Name (ASCII escaped) | IsEnabled | BoundingRectangle')
    foreach ($button in $buttons) {
        $current = $button.Current
        $bounds = $current.BoundingRectangle
        $lines += '{0} | {1} | {2} | {3},{4},{5},{6}' -f
            (ConvertTo-Ascii $current.AutomationId), (ConvertTo-Ascii $current.Name),
            $current.IsEnabled, $bounds.X, $bounds.Y, $bounds.Width, $bounds.Height
    }
    $lines | Set-Content -LiteralPath (Join-Path $outputRoot ($Name + '-uia.txt')) -Encoding ASCII
    $lines | Write-Output
}

$ownedProcess = $false
$process = $null
try {
    if ($Attach) {
        if (-not $TitleContains) {
            $TitleContains = [regex]::Unescape('\u7b80\u58f9\u56fe\u7247\u6d4f\u89c8\u5668')
        }
        $matches = @(Get-Process -Name ImageViewer -ErrorAction SilentlyContinue | Where-Object {
            $_.MainWindowHandle -ne [IntPtr]::Zero -and $_.MainWindowTitle.Contains($TitleContains)
        })
        if ($matches.Count -ne 1) { throw ('Attach requires one matching ImageViewer window; found ' + $matches.Count) }
        $process = $matches[0]
        Write-Output ('attached PID ' + $process.Id)
    }
    else {
        if (-not $Exe -or -not (Test-Path -LiteralPath $Exe -PathType Leaf)) {
            throw 'Exe must name an existing application executable.'
        }
        if ($Image) {
            if (-not (Test-Path -LiteralPath $Image -PathType Leaf)) { throw 'Image does not exist.' }
            $process = Start-Process -FilePath $Exe -ArgumentList ('"' + $Image + '"') -PassThru -WindowStyle Hidden
        }
        else { $process = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden }
        $ownedProcess = $true
        Write-Output ('started PID ' + $process.Id)
    }
    for ($i = 0; $i -lt 60; $i++) {
        $process.Refresh()
        if ($process.HasExited) { throw 'Application exited before showing its window.' }
        if ($process.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 250
    }
    $hwnd = $process.MainWindowHandle
    if ($hwnd -eq [IntPtr]::Zero) { throw 'No main window after 15 seconds.' }
    Write-Output ('DPI=' + [ImageViewerCaptureNative]::GetDpiForWindow($hwnd))
    if ($Width -gt 0 -and -not [ImageViewerCaptureNative]::SetWindowPos(
        $hwnd, [IntPtr]::Zero, 0, 0, $Width, $Height, 0x0004)) { throw 'SetWindowPos failed.' }
    if (-not $Attach -or $Width -gt 0) { Start-Sleep -Milliseconds 2500 }
    Save-Window $hwnd (Join-Path $outputRoot ($Name + '.png'))
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    if ($DumpUia) { Write-Buttons $root }
    if ($Extract) {
        # Open extraction at full width before restoring a narrow target size.
        if ($Width -gt 0 -and $Width -lt 800) {
            [ImageViewerCaptureNative]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 1280, $Height, 0x0004) | Out-Null
            Start-Sleep -Milliseconds 300
        }
        $condition = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'ExtractTextButton')
        $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -eq $button) {
            $label = [regex]::Unescape('\u63d0\u53d6\u6587\u5b57')
            $condition = New-Object System.Windows.Automation.PropertyCondition(
                [System.Windows.Automation.AutomationElement]::NameProperty, $label)
            $button = $root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
        }
        if ($null -eq $button -or -not $button.Current.IsEnabled) { throw 'ExtractTextButton is missing or disabled.' }
        $button.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
        Start-Sleep -Milliseconds 3000
        if ($Width -gt 0 -and $Width -lt 800) {
            [ImageViewerCaptureNative]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, $Width, $Height, 0x0004) | Out-Null
            Start-Sleep -Milliseconds 500
        }
        Save-Window $hwnd (Join-Path $outputRoot ($Name + '-panel.png'))
        if ($DumpUia) { Write-Buttons $root }
    }
}
finally {
    if ($ownedProcess -and $null -ne $process) {
        $process.Refresh()
        if (-not $process.HasExited) {
            $null = $process.CloseMainWindow()
            if (-not $process.WaitForExit(1500)) { Stop-Process -Id $process.Id -Force }
        }
        Write-Output ('closed owned PID ' + $process.Id)
    }
    if ($null -ne $process) { $process.Dispose() }
}
