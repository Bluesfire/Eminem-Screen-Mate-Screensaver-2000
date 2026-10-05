@echo off
setlocal
set "SCR=%WINDIR%\System32\Eminem ScreenMate.scr"

if not exist "%SCR%" (
    echo Eminem ScreenMate is not installed in Windows Screen Saver Settings.
    echo Run "Install Eminem ScreenMate Screensaver.cmd" first.
    pause
    exit /b 1
)

reg add "HKCU\Control Panel\Desktop" /v "SCRNSAVE.EXE" /t REG_SZ /d "%SCR%" /f >nul
reg add "HKCU\Control Panel\Desktop" /v "ScreenSaveActive" /t REG_SZ /d "1" /f >nul

echo Eminem ScreenMate enabled as the current Windows screen saver.
echo Timeout remains whatever Windows is currently configured to use.
exit /b 0
