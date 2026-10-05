@echo off
call "C:\Program Files\Microsoft Visual Studio\2022\Professional\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b 1
pushd "%~dp0"
if not exist native-build mkdir native-build
cl /nologo /O2 /MT /LD /I vendor\minhook\include /Fonative-build\ /FeScreenMateMonitorHooks.dll MonitorHooks.c OriginalRuntime.c vendor\minhook\src\hook.c vendor\minhook\src\buffer.c vendor\minhook\src\trampoline.c vendor\minhook\src\hde\hde32.c /link user32.lib gdi32.lib /OUT:ScreenMateMonitorHooks.dll /IMPLIB:native-build\ScreenMateMonitorHooks.lib
if errorlevel 1 exit /b 1
cl /nologo /O2 /MT /Fonative-build\ /FeScreenMateMonitorHost.exe MonitorHost.c /link user32.lib
if errorlevel 1 exit /b 1
popd
