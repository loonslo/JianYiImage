# UI Automation helper for record.py. It only READS the app's UI (rectangles, OCR status); it never sends input.
# ASCII only on purpose: Windows PowerShell 5.1 reads BOM-less UTF-8 scripts as GBK, so Chinese names arrive as \uXXXX.
param(
    [Parameter(Mandatory)][int]$ProcessId,
    [Parameter(Mandatory)][ValidateSet('wait-ocr', 'rects')][string]$Mode,
    [string]$Ids = '',
    [string]$Names = '',
    [int]$TimeoutMs = 20000
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$AE = [System.Windows.Automation.AutomationElement]
$TS = [System.Windows.Automation.TreeScope]
$PC = [System.Windows.Automation.PropertyCondition]

$proc = Get-Process -Id $ProcessId
$deadline = [DateTime]::UtcNow.AddMilliseconds($TimeoutMs)
while ($proc.MainWindowHandle -eq [IntPtr]::Zero) {
    if ([DateTime]::UtcNow -gt $deadline) { throw 'no main window' }
    Start-Sleep -Milliseconds 200
    $proc.Refresh()
}
$win = $AE::FromHandle($proc.MainWindowHandle)

function Find-Named([string]$name) {
    $cond = New-Object $PC($AE::NameProperty, $name)
    $el = $win.FindFirst($TS::Descendants, $cond)
    if ($null -ne $el) { return $el }
    # popups (menus) can be separate top-level windows of the same process
    $mine = New-Object $PC($AE::ProcessIdProperty, $ProcessId)
    foreach ($top in $AE::RootElement.FindAll($TS::Children, $mine)) {
        $el = $top.FindFirst($TS::Descendants, $cond)
        if ($null -ne $el) { return $el }
    }
    return $null
}

switch ($Mode) {
    'wait-ocr' {
        $ready = [regex]::Unescape('\u6587\u5b57\u5df2\u8bc6\u522b')       # the status text once OCR has finished
        $el = $null
        while ($true) {
            if ($null -eq $el) { $el = $win.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, 'OcrStatusText'))) }
            if ($null -ne $el -and $el.Current.Name -eq $ready) { 'ready'; break }
            if ([DateTime]::UtcNow -gt $deadline) { throw 'OCR never became ready' }
            Start-Sleep -Milliseconds 150
        }
    }
    'rects' {
        $out = @()
        foreach ($id in ($Ids.Split(',') | Where-Object { $_ })) {
            $el = $win.FindFirst($TS::Descendants, (New-Object $PC($AE::AutomationIdProperty, $id)))
            if ($null -ne $el) {
                $r = $el.Current.BoundingRectangle
                $out += [pscustomobject]@{ key = $id; x = $r.X; y = $r.Y; w = $r.Width; h = $r.Height }
            }
        }
        foreach ($escaped in ($Names.Split(',') | Where-Object { $_ })) {
            $el = Find-Named ([regex]::Unescape($escaped))
            if ($null -ne $el) {
                $r = $el.Current.BoundingRectangle
                $out += [pscustomobject]@{ key = $escaped; x = $r.X; y = $r.Y; w = $r.Width; h = $r.Height }
            }
        }
        ConvertTo-Json -InputObject @($out) -Compress
    }
}
