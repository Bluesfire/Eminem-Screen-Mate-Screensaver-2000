@echo off
setlocal
set "RUNTIME=%~dp0compat\otvdm\otvdm-v0.9.0"

if not exist "%RUNTIME%\otvdmw.exe" (
    echo Eminem ScreenMate compatibility runtime is missing:
    echo "%RUNTIME%\otvdmw.exe"
    pause
    exit /b 1
)

if not exist "%RUNTIME%\WINDOWS\eminem.SCR" (
    echo Eminem ScreenMate compatibility files are missing.
    pause
    exit /b 1
)

start "" "%~dp0Eminem ScreenMate.exe" /s
exit /b 0
