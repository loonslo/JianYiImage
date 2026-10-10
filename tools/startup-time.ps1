param([string]$Exe)
$ErrorActionPreference = 'Stop'
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class StartupNative {
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hwnd);
}
'@
for ($run = 1; $run -le 3; $run++) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $p = Start-Process -FilePath $Exe -PassThru -WindowStyle Hidden
    try {
        while ($watch.ElapsedMilliseconds -lt 15000) {
            $p.Refresh()
            if ($p.HasExited) { throw 'App exited during startup.' }
            if ($p.MainWindowHandle -ne [IntPtr]::Zero -and [StartupNative]::IsWindowVisible($p.MainWindowHandle)) { break }
            Start-Sleep -Milliseconds 10
        }
        $watch.Stop()
        if ($p.MainWindowHandle -eq [IntPtr]::Zero -or -not [StartupNative]::IsWindowVisible($p.MainWindowHandle)) { throw 'No visible window.' }
        Write-Output ('Run ' + $run + ': ' + $watch.ElapsedMilliseconds + ' ms to visible window (new process, OS cache retained)')
    }
    finally {
        $p.Refresh()
        if (-not $p.HasExited) {
            $null = $p.CloseMainWindow()
            if (-not $p.WaitForExit(1500)) { Stop-Process -Id $p.Id -Force }
        }
    }
}
