$projectRoot = Split-Path $PSScriptRoot -Parent
$wrapperPaths = @((Join-Path $projectRoot 'Eminem ScreenMate.exe'), (Join-Path $projectRoot 'Eminem ScreenMate.scr'))
$runtimePath = Join-Path $projectRoot 'compat\otvdm\otvdm-v0.9.0\otvdmw.exe'
$hostPath = Join-Path $projectRoot 'ScreenMateMonitorHost.exe'
$processes = Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and ($wrapperPaths -contains $_.ExecutablePath -or $_.ExecutablePath -eq $runtimePath -or $_.ExecutablePath -eq $hostPath) }
foreach ($process in $processes) { Stop-Process -Id $process.ProcessId -Force -ErrorAction SilentlyContinue }
