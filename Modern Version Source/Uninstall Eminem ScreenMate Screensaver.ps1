$ErrorActionPreference = 'Stop'

$systemScr = Join-Path $env:WINDIR 'System32\Eminem ScreenMate.scr'
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

$desktopKey = 'HKCU:\Control Panel\Desktop'
$current = (Get-ItemProperty -Path $desktopKey -Name 'SCRNSAVE.EXE' -ErrorAction SilentlyContinue).'SCRNSAVE.EXE'
if ($current -and ([IO.Path]::GetFullPath($current) -ieq [IO.Path]::GetFullPath($systemScr))) {
    Remove-ItemProperty -Path $desktopKey -Name 'SCRNSAVE.EXE' -ErrorAction SilentlyContinue
}

Remove-Item -LiteralPath $systemScr -Force -ErrorAction SilentlyContinue
Remove-Item -Path 'HKCU:\Software\EminemScreenMate' -Recurse -Force -ErrorAction SilentlyContinue

Write-Host 'Eminem ScreenMate Windows integration removed.'
Read-Host 'Press Enter to close'
