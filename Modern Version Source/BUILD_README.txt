Building the modern Eminem ScreenMate package

Required on Windows:
- Visual Studio 2022 Professional with the C++ desktop build tools and x86 tools.
- The .NET Framework 4.x compiler (included with Windows 10/11).

From this folder, run:
  powershell -NoProfile -ExecutionPolicy Bypass -File wrapper\build.ps1

Outputs:
  Eminem ScreenMate.scr / Eminem ScreenMate.exe
  ScreenMateMonitorHost.exe / ScreenMateMonitorHooks.dll
  installer\build\Eminem ScreenMate Setup.exe

The seed installer\build\payload.zip contains the preserved original runtime
and uninstaller. The build refreshes the wrapper, control app, adapters, license
and notes in that payload and embeds it in the new installer. The matching
runtime is also provided under compat so the freshly built local launcher works.
Run Eminem ScreenMate Now.cmd after building to launch the local modern version.

wrapper\build-native.cmd currently expects Visual Studio in:
  C:\Program Files\Microsoft Visual Studio\2022\Professional
Adjust that path if using another Visual Studio edition or installation path.

installer contains the control, setup and uninstaller C# sources and manifests.
The provided uninstaller binary is retained in the seed payload. To rebuild it:
  %WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /platform:x64 /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /win32manifest:installer\uninstall.manifest /out:"installer\build\Uninstall Eminem ScreenMate.exe" installer\EminemScreenMateUninstall.cs
Then update the payload with installer\build_payload.py (Python 3) before
running wrapper\build.ps1 again.

Third-party dependency versions and preservation details are documented in
WINDOWS_10_11_NOTES.txt. MinHook's license and source are in wrapper\vendor.
The original Eminem.zip is provided unchanged one folder above this source tree.
