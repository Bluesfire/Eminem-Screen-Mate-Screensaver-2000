@echo off
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0wrapper\stop.ps1"
exit /b 0
