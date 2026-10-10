param([string]$Exe, [string]$Image)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class FinalUiaNative {
    [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
}
'@
[FinalUiaNative]::SetProcessDPIAware() | Out-Null
function Find-Id([string]$Id) {
    $condition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, $Id)
    return $script:root.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Invoke-Id([string]$Id) {
    $element = Find-Id $Id
    if ($null -eq $element -or -not $element.Current.IsEnabled) { throw ('Missing/disabled: ' + $Id) }
    $element.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    [FinalUiaNative]::SetWindowPos($script:hwnd, [IntPtr]::Zero, 0, 0, $script:checkWidth, $script:checkHeight, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 150
}
function Assert-Panel([bool]$Visible) {
    $text = Find-Id 'ExtractedTextBox'
    $actual = $null -ne $text -and $text.Current.BoundingRectangle.Width -gt 0 -and $text.Current.BoundingRectangle.Height -gt 0
    if ($actual -ne $Visible) { throw ('Panel visibility expected ' + $Visible + ', got ' + $actual) }
}
function Menu-Items {
    $menuCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::MenuItem)
    $pidCondition = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ProcessIdProperty, $script:p.Id)
    $condition = New-Object System.Windows.Automation.AndCondition($menuCondition, $pidCondition)
    return [System.Windows.Automation.AutomationElement]::RootElement.FindAll([System.Windows.Automation.TreeScope]::Descendants, $condition)
}
function Escaped([string]$Value) {
    return -join ($Value.ToCharArray() | ForEach-Object { if ([int]$_ -gt 127) { '\u{0:x4}' -f [int]$_ } else { [string]$_ } })
}
$p = Start-Process -FilePath $Exe -ArgumentList ('"' + $Image + '"') -PassThru -WindowStyle Hidden
try {
    for ($i = 0; $i -lt 60; $i++) {
        $p.Refresh()
        if ($p.HasExited) { throw 'Application exited.' }
        if ($p.MainWindowHandle -ne [IntPtr]::Zero) { break }
        Start-Sleep -Milliseconds 250
    }
    $hwnd = $p.MainWindowHandle
    $checkWidth = 1280
    $checkHeight = 800
    if ($hwnd -eq [IntPtr]::Zero) { throw 'No main window.' }
    [FinalUiaNative]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 1280, 800, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 2000
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    Assert-Panel $false
    Invoke-Id 'ExtractTextButton'
    Start-Sleep -Milliseconds 600
    Assert-Panel $true
    $textbox = Find-Id 'ExtractedTextBox'
    $value = $textbox.GetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern).Current.Value
    if ([string]::IsNullOrWhiteSpace($value)) { throw 'OCR panel has no text.' }
    if ([Math]::Abs($textbox.Current.BoundingRectangle.Width - 287) -gt 1) { throw 'Incorrect panel text area width.' }
    Write-Output ('PASS panel open: text length=' + $value.Length + ', text area width=287, dock width=320')
    if ([string]::IsNullOrWhiteSpace($textbox.Current.Name)) { throw 'OCR text has no accessible name.' }
    if (-not (Find-Id 'ActualSizeButton').Current.Name.Contains('%')) { throw 'Zoom name omits percentage.' }
    $p.Refresh()
    if (-not $p.MainWindowTitle.Contains([IO.Path]::GetFileName($Image)) -or -not $p.MainWindowTitle.Contains(' / ')) { throw 'Native title omits filename or playlist position.' }
    Write-Output 'PASS OCR accessible name, zoom percentage, native filename/position title'
    foreach ($width in @(780, 600, 420)) {
        [FinalUiaNative]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, $width, 560, 0x0004) | Out-Null
        Start-Sleep -Milliseconds 500
        Assert-Panel $true
        $textBounds = (Find-Id 'ExtractedTextBox').Current.BoundingRectangle
        $previousBounds = (Find-Id 'PreviousArrowButton').Current.BoundingRectangle
        $nextBounds = (Find-Id 'NextArrowButton').Current.BoundingRectangle
        if ($textBounds.Width -lt $width - 65) { throw "Bottom text panel does not span width $width." }
        if ($textBounds.Top -le $nextBounds.Top + $nextBounds.Height) { throw 'Panel still occupies image width.' }
        if ($previousBounds.Right -ge $nextBounds.Left) { throw 'Navigation buttons overlap.' }
        Write-Output "PASS narrow panel $width`: text width=$($textBounds.Width), arrows separated"
    }
    [FinalUiaNative]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 1280, 800, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 400
    if ([Math]::Abs((Find-Id 'ExtractedTextBox').Current.BoundingRectangle.Width - 287) -gt 1) { throw 'Right dock not restored.' }
    Invoke-Id 'ExtractTextButton'
    Assert-Panel $false
    Invoke-Id 'ExtractTextButton'
    Assert-Panel $true
    Invoke-Id 'CloseTextExtractionButton'
    Assert-Panel $false
    Start-Sleep -Milliseconds 1500
    Assert-Panel $false
    Write-Output 'PASS panel open/close/open, close button, completion keeps closed'
    Invoke-Id 'ExtractTextButton'
    Assert-Panel $true
    Invoke-Id 'NextButton'
    Start-Sleep -Milliseconds 1500
    Assert-Panel $false
    Invoke-Id 'PreviousButton'
    Start-Sleep -Milliseconds 1500
    Assert-Panel $false
    Write-Output 'PASS navigation resets panel'
    foreach ($id in @('ZoomInButton', 'ActualSizeButton', 'ZoomOutButton', 'FitButton', 'RotateClockwiseButton', 'RotateCounterClockwiseButton')) {
        Invoke-Id $id
        $p.Refresh()
        if ($p.HasExited) { throw ('Application exited after ' + $id) }
    }
    Write-Output 'PASS zoom/actual size/fit/rotation commands invoked without errors'
    $checkWidth = 250
    $checkHeight = 560
    [FinalUiaNative]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 250, 560, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 600
    Invoke-Id 'OverflowButton'
    $menus = Menu-Items
    $actual = $null
    $required = @('ZoomOutButton','ActualSizeButton','ZoomInButton')
    $labels = @([regex]::Unescape('\u7f29\u5c0f'), 'actual', [regex]::Unescape('\u653e\u5927'))
    $found = @($false, $false, $false)
    foreach ($menu in $menus) {
        $name = $menu.Current.Name
        Write-Output ('MENU ' + (Escaped $name))
        if ($name -eq $labels[0]) { $found[0] = $true }
        if ($name.Contains('100%')) { $found[1] = $true; $actual = $menu }
        if ($name -eq $labels[2]) { $found[2] = $true }
    }
    if ($found -contains $false) { throw 'Zoom group was not fully flattened into the overflow menu.' }
    $actual.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 200
    Write-Output 'PASS hidden zoom group flattened and actual-size menu command invoked'
    $checkWidth = 1280
    $checkHeight = 800
    [FinalUiaNative]::SetWindowPos($hwnd, [IntPtr]::Zero, 0, 0, 1280, 800, 0x0004) | Out-Null
    Start-Sleep -Milliseconds 500
    $windowPattern = $root.GetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern)
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Maximized)
    Start-Sleep -Milliseconds 300
    if ($windowPattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Maximized) { throw 'Maximize failed.' }
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    Start-Sleep -Milliseconds 300
    if ($windowPattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Normal) { throw 'Restore failed.' }
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Minimized)
    Start-Sleep -Milliseconds 300
    if ($windowPattern.Current.WindowVisualState -ne [System.Windows.Automation.WindowVisualState]::Minimized) { throw 'Minimize failed.' }
    $windowPattern.SetWindowVisualState([System.Windows.Automation.WindowVisualState]::Normal)
    Write-Output 'PASS maximize/restore/minimize/restore'
}
finally {
    $p.Refresh()
    if (-not $p.HasExited) {
        $null = $p.CloseMainWindow()
        if (-not $p.WaitForExit(1500)) { Stop-Process -Id $p.Id -Force }
    }
    Write-Output ('Closed owned PID=' + $p.Id)
}
