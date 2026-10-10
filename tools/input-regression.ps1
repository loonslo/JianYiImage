# Test Delete only on disposable copies of generated fixtures.
# Requires an interactive desktop; verify foreground PID and text focus before SendInput.
param([Parameter(Mandatory=$true)][string]$Exe,
      [Parameter(Mandatory=$true)][string]$Image)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class InputRegressionNative {
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT {
        public ushort vk, scan; public uint flags, time; public UIntPtr extra;
    }
    [StructLayout(LayoutKind.Explicit, Size=32)] public struct INPUTUNION {
        [FieldOffset(0)] public KEYBDINPUT keyboard;
    }
    [StructLayout(LayoutKind.Sequential)] public struct INPUT {
        public uint type; public INPUTUNION data;
    }
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hwnd);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] public static extern uint SendInput(uint count, INPUT[] inputs, int size);
    public static void Key(ushort vk) {
        var inputs = new INPUT[2];
        inputs[0].type = inputs[1].type = 1;
        inputs[0].data.keyboard.vk = inputs[1].data.keyboard.vk = vk;
        inputs[1].data.keyboard.flags = 2;
        if (SendInput(2, inputs, Marshal.SizeOf<INPUT>()) != 2) throw new Exception("SendInput failed");
    }
}
'@
function Find-Id([string]$Id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    return $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Invoke-Id([string]$Id) {
    $element = Find-Id $Id
    if ($null -eq $element -or -not $element.Current.IsEnabled) { throw "Missing/disabled $Id" }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 300
}
function Send-CheckedKey([int]$Key, [bool]$TextFocus) {
    [uint32]$foregroundPid = 0
    [InputRegressionNative]::GetWindowThreadProcessId([InputRegressionNative]::GetForegroundWindow(), [ref]$foregroundPid) | Out-Null
    if ($foregroundPid -ne $script:p.Id) { throw 'Foreground changed; no key sent.' }
    if ($TextFocus -and -not (Find-Id 'ExtractedTextBox').Current.HasKeyboardFocus) { throw 'Text box lost focus; no key sent.' }
    [InputRegressionNative]::Key($Key)
    Start-Sleep -Milliseconds 250
}
if (-not (Test-Path -LiteralPath $Exe -PathType Leaf) -or -not (Test-Path -LiteralPath $Image -PathType Leaf)) { throw 'Exe and Image must exist.' }
$fixtureDir = Join-Path $env:TEMP ('jianyiimage-input-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixtureDir | Out-Null
$fixture = Join-Path $fixtureDir '01-copy.png'
Copy-Item -LiteralPath $Image -Destination $fixture
# Supported extension with invalid contents: fail decoding after OCR/panel use.
[IO.File]::WriteAllText((Join-Path $fixtureDir '02-corrupt.png'), 'not an image')
$p = Start-Process -FilePath $Exe -ArgumentList ('"' + $fixture + '"') -PassThru -WindowStyle Hidden
try {
    for ($i = 0; $i -lt 60; $i++) {
        $p.Refresh()
        if ($p.HasExited) { throw 'Application exited.' }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 250
    }
    $hwnd = $p.MainWindowHandle
    if ($hwnd -eq [IntPtr]::Zero) { throw 'No main window.' }
    [InputRegressionNative]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 1280, 800, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 1500
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    Invoke-Id 'ExtractTextButton'
    Start-Sleep -Milliseconds 1500
    [InputRegressionNative]::SetForegroundWindow($hwnd) | Out-Null
    (Find-Id 'ExtractedTextBox').SetFocus()
    Start-Sleep -Milliseconds 200
    $p.Refresh()
    $titleBefore = $p.MainWindowTitle
    $zoomBefore = (Find-Id 'ActualSizeButton').Current.Name
    $textBefore = (Find-Id 'ExtractedTextBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    foreach ($key in @(0x2E, 0x52, 0x25, 0x27, 0x24, 0x23)) { # Delete, R, Left, Right, Home, End
        Send-CheckedKey $key $true
        $p.Refresh()
        if (-not (Test-Path -LiteralPath $fixture) -or $p.MainWindowTitle -ne $titleBefore -or (Find-Id 'ActualSizeButton').Current.Name -ne $zoomBefore) {
            throw "Text-focus key $key changed/deleted image."
        }
    }
    if ((Find-Id 'ExtractedTextBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $textBefore) { throw 'Read-only text changed.' }
    Write-Output 'PASS real Delete/R/arrows/Home/End with read-only text focus: copy survives, image/title/zoom unchanged'
    Invoke-Id 'CloseTextExtractionButton' # Handler returns focus to the canvas.
    Send-CheckedKey 0x52 $false
    if ((Find-Id 'ActualSizeButton').Current.Name -eq $zoomBefore) { throw 'Canvas R shortcut did not rotate/refit fixture.' }
    Send-CheckedKey 0x52 $false
    Write-Output 'PASS real R with canvas focus rotates and refits (positive keyboard delivery control)'
    Invoke-Id 'ExtractTextButton'
    Start-Sleep -Milliseconds 600
    Invoke-Id 'NextButton'
    Start-Sleep -Milliseconds 1500
    $text = Find-Id 'ExtractedTextBox'
    if ($null -ne $text -and $text.Current.BoundingRectangle.Width -gt 0) { throw 'Failed image left text panel visible.' }
    if ((Find-Id 'ExtractTextButton').Current.IsEnabled -or (Find-Id 'ActualSizeButton').Current.IsEnabled) { throw 'Failed image left image commands enabled.' }
    $ocrStatus = Find-Id 'OcrStatusText'
    if ($null -ne $ocrStatus -and -not [string]::IsNullOrEmpty($ocrStatus.Current.Name)) { throw 'Failed image left OCR status.' }
    if ($null -eq (Find-Id 'EmptyOpenButton')) { throw 'Failed image did not show empty state.' }
    Write-Output 'PASS corrupt next image: panel closed, OCR cleared, image commands disabled, empty state shown'
    Invoke-Id 'PreviousButton'
    Start-Sleep -Milliseconds 1000
    Invoke-Id 'ExtractTextButton'
    Start-Sleep -Milliseconds 600
    if ((Find-Id 'ExtractedTextBox').GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $textBefore) { throw 'Valid image failed to recover OCR.' }
    Write-Output 'PASS recovery to valid image restores OCR'
}
finally {
    $p.Refresh()
    if (-not $p.HasExited) {
        $null = $p.CloseMainWindow()
        if (-not $p.WaitForExit(1500)) { Stop-Process -Id $p.Id -Force }
    }
    $p.Dispose()
    Write-Output "Fixture copies retained for inspection: $fixtureDir"
}
