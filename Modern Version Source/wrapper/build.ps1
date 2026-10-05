$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
Push-Location $projectRoot
try {
    & $env:ComSpec /c (Join-Path $PSScriptRoot 'build-native.cmd')
    if ($LASTEXITCODE -ne 0) { throw 'Original-engine monitor adapter compilation failed.' }
    Copy-Item -LiteralPath 'wrapper\ScreenMateMonitorHost.exe' -Destination 'ScreenMateMonitorHost.exe' -Force
    Copy-Item -LiteralPath 'wrapper\ScreenMateMonitorHooks.dll' -Destination 'ScreenMateMonitorHooks.dll' -Force
    & $compiler /nologo /target:winexe /platform:x64 /unsafe /reference:System.Drawing.dll /reference:System.Windows.Forms.dll '/out:Eminem ScreenMate.scr' '.\wrapper\EminemScreenMateWrapper.cs' '.\wrapper\MultiMonitorSaver.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Wrapper compilation failed.' }
    Copy-Item -LiteralPath 'Eminem ScreenMate.scr' -Destination 'Eminem ScreenMate.exe' -Force
    & $compiler /nologo /target:winexe /platform:x64 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll '/out:installer\build\Eminem ScreenMate Control.exe' '.\installer\EminemScreenMateControl.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Control compilation failed.' }
    # Refresh the existing payload, leaving its preserved Win16 runtime intact.
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::Open((Join-Path $projectRoot 'installer\build\payload.zip'), [IO.Compression.ZipArchiveMode]::Update)
    try {
        $updates = @{
            'ScreenMateMonitorHost.exe' = 'ScreenMateMonitorHost.exe'
            'ScreenMateMonitorHooks.dll' = 'ScreenMateMonitorHooks.dll'
            'MinHook-LICENSE.txt' = 'wrapper\vendor\minhook\LICENSE.txt'
            'Eminem ScreenMate.scr' = 'Eminem ScreenMate.scr'
            'Eminem ScreenMate Control.exe' = 'installer\build\Eminem ScreenMate Control.exe'
            'WINDOWS_10_11_NOTES.txt' = 'WINDOWS_10_11_NOTES.txt'
        }
        foreach ($entryName in $updates.Keys) {
            $entry = $archive.GetEntry($entryName)
            if ($entry) { $entry.Delete() }
            [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, (Join-Path $projectRoot $updates[$entryName]), $entryName) | Out-Null
        }
    } finally { $archive.Dispose() }
    & $compiler /nologo /target:winexe /platform:x64 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll '/win32manifest:installer\setup.manifest' '/resource:installer\build\payload.zip,EminemScreenMate.Payload.zip' '/out:installer\build\Eminem ScreenMate Setup.exe' '.\installer\EminemScreenMateSetup.cs'
    if ($LASTEXITCODE -ne 0) { throw 'Setup compilation failed.' }
} finally { Pop-Location }
