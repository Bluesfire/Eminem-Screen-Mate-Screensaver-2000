@echo off
setlocal
for /f "tokens=2,*" %%A in ('reg query "HKCU\Control Panel\Desktop" /v "SCRNSAVE.EXE" 2^>nul ^| find /I "SCRNSAVE.EXE"') do set "CURRENT=%%B"

if /I "%CURRENT%"=="%WINDIR%\System32\Eminem ScreenMate.scr" (
    reg delete "HKCU\Control Panel\Desktop" /v "SCRNSAVE.EXE" /f >nul 2>nul
)
reg add "HKCU\Control Panel\Desktop" /v "ScreenSaveActive" /t REG_SZ /d "0" /f >nul

echo Windows screen saver disabled.
exit /b 0
