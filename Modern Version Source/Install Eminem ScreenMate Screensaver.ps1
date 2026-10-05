$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$wrapper = Join-Path $root 'Eminem ScreenMate.scr'
$systemScr = Join-Path $env:WINDIR 'System32\Eminem ScreenMate.scr'

if (-not (Test-Path -LiteralPath $wrapper)) {
    throw "Wrapper not found: $wrapper"
}

$runtime = Join-Path $root 'compat\otvdm\otvdm-v0.9.0\otvdmw.exe'
$payload = Join-Path $root 'compat\otvdm\otvdm-v0.9.0\WINDOWS\eminem.SCR'
if (-not (Test-Path -LiteralPath $runtime) -or -not (Test-Path -LiteralPath $payload)) {
    throw 'WineVDM runtime or original Eminem ScreenMate payload is missing.'
}

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator
)

if (-not $isAdmin) {
    $args = @(
        '-NoProfile',
        '-ExecutionPolicy', 'Bypass',
        '-File', ('"{0}"' -f $MyInvocation.MyCommand.Path)
    )
    Start-Process powershell.exe -Verb RunAs -ArgumentList $args
    exit
}

New-Item -Path 'HKCU:\Software\EminemScreenMate' -Force | Out-Null
Set-ItemProperty -Path 'HKCU:\Software\EminemScreenMate' -Name 'InstallDir' -Value $root -Type String

Copy-Item -LiteralPath $wrapper -Destination $systemScr -Force

Write-Host ''
Write-Host 'Installed Eminem ScreenMate wrapper to:'
Write-Host "  $systemScr"
Write-Host ''
Write-Host 'It should now appear as "Eminem ScreenMate" in Windows Screen Saver Settings.'
Write-Host 'Use "Enable Eminem ScreenMate.cmd" to select it immediately.'
Write-Host ''
Read-Host 'Press Enter to close'
