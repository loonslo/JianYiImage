# Helper for the JianYiImage installer: lists the OCR languages Windows can use right now, or installs OCR language packs.
# The packs are Windows "Features on Demand" with capability names such as Language.OCR~~~en-US~0.0.1.0.
#
#   -Mode List      no elevation needed. Writes the language tags of the OCR engines Windows offers at this moment
#                   (for example zh-Hans-CN), one per line.
#   -Mode Install   needs an elevated session. Installs the packs for -Languages (capability tags, comma separated,
#                   for example zh-CN,en-US) and writes one line per language: tag|status|detail
#                   status is one of: installed, already, unavailable, failed (dryrun with -DryRun).
#                   A pack is downloaded from Windows Update, so it needs a network connection and can take minutes.
#   -DryRun         Install mode without changing the system. Used by the tests together with -CapabilityListFile, a
#                   text file of "name|state" lines that replaces the query to Windows.
#
# ASCII only on purpose: Windows PowerShell 5.1 reads a script without a BOM as the ANSI code page, and the result
# lines must stay simple for the installer to read. Localized system messages are reduced to their error code.
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][ValidateSet('List', 'Install')][string]$Mode,
    [Parameter(Mandatory = $true)][string]$OutFile,
    [string[]]$Languages = @(),
    [switch]$DryRun,
    [string]$CapabilityListFile = ''
)

$ErrorActionPreference = 'Stop'

function Write-Lines([string[]]$Lines) {
    [System.IO.File]::WriteAllLines($OutFile, [string[]]$Lines, [System.Text.Encoding]::ASCII)
}

if ($Mode -eq 'List') {
    try {
        [Windows.Media.Ocr.OcrEngine, Windows.Foundation.UniversalApiContract, ContentType = WindowsRuntime] | Out-Null
        $tags = @([Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages | ForEach-Object { $_.LanguageTag })
    }
    catch {
        $tags = @()
    }
    Write-Lines $tags
    exit 0
}

# ---- Install mode ----
$requested = @($Languages | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
$results = New-Object System.Collections.Generic.List[string]

function Add-Result([string]$Tag, [string]$Status, [string]$Detail) {
    $results.Add(('{0}|{1}|{2}' -f $Tag, $Status, $Detail))
    Write-Lines $results.ToArray()          # written after every language so a killed run still leaves its progress behind
}

function Format-Code($ErrorRecord) {
    return 'error 0x{0:x8}' -f $ErrorRecord.Exception.HResult
}

Write-Lines @()

if (-not $DryRun) {
    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Add-Result '*' 'failed' 'not elevated'
        exit 5
    }
}

try {
    if ($CapabilityListFile) {
        $capabilities = @(Get-Content -LiteralPath $CapabilityListFile | Where-Object { $_ } | ForEach-Object {
            $parts = $_ -split '\|'
            [pscustomobject]@{ Name = $parts[0]; State = $parts[1] }
        })
    }
    else {
        $capabilities = @(Get-WindowsCapability -Online | Where-Object { $_.Name -like 'Language.OCR~~~*' })
    }
}
catch {
    Add-Result '*' 'failed' ('could not query Windows, ' + (Format-Code $_))
    exit 3
}

$failed = $false
foreach ($tag in $requested) {
    if ($tag -notmatch '^[A-Za-z0-9-]{2,20}$') {
        Add-Result $tag 'failed' 'invalid language tag'
        $failed = $true
        continue
    }

    $capability = $capabilities | Where-Object { $_.Name -like ('Language.OCR~~~' + $tag + '~*') } | Select-Object -First 1
    if ($null -eq $capability) {
        Add-Result $tag 'unavailable' 'not offered by this Windows'
        continue
    }

    if ("$($capability.State)" -eq 'Installed') {
        Add-Result $tag 'already' ''
        continue
    }

    if ($DryRun) {
        Add-Result $tag 'dryrun' $capability.Name
        continue
    }

    try {
        $outcome = Add-WindowsCapability -Online -Name $capability.Name
        $detail = ''
        if ($outcome -and $outcome.RestartNeeded) { $detail = 'restart needed' }
        Add-Result $tag 'installed' $detail
    }
    catch {
        $failed = $true
        Add-Result $tag 'failed' (Format-Code $_)
    }
}

if ($failed) { exit 1 }
exit 0
