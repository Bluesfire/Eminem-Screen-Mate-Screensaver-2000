> **AI disclosure:** AI was used to write and debug the modern Windows compatibility code. The original screensaver, animations, and sounds are preserved unchanged.

# Eminem ScreenMate

The original Eminem ScreenMate from 2000, running on modern Windows.

He walks onto your desktop, says his lines, shows the album cover, then starts shooting, spitting, pissing, and generally wrecking the place.
Same old screensaver.

The original program still draws the animations and plays the sounds through [WineVDM](https://github.com/otya128/winevdm).
All original files stay intact.

## Where it came from

We found the original `eminem.zip` through the Wayback Machine, in [an archived download from eminemfans.com](https://web.archive.org/web/20070307034311/http://www.eminemfans.com/main/extras/download/eminem.zip).
That archive is included here.

## Run it

Install [Eminem ScreenMate Setup.exe](Eminem%20ScreenMate%20Setup.exe), then choose **Eminem ScreenMate** in Windows Screen Saver Settings.
The Start Menu app has a **Run now** button too.

Built for 64-bit Windows 10 and 11; tested on Windows 11.

## Multiple monitors

Your screens are detected automatically.
Each gets its own desktop screenshot, so portrait displays keep their proper shape.

The intro plays on the main screen, then destruction happens randomly across all connected screens at the original overall pace.
Sound comes from one instance.
That includes effects from the other monitors, with the background song playing only once.

## What's here

- **Eminem.zip**: the unchanged original archive.
- **Modern Version Source/**: compatibility code, dependencies, and [build instructions](Modern%20Version%20Source/BUILD_README.txt).
- **Eminem ScreenMate Setup.exe**: the current installer, version 1.3.0.
- **SHA256SUMS.txt**: file hashes for checking the package.

The current build passed a two-minute visible test across three monitors, including a portrait screen, with no crashes or audio errors.

## Building

You'll need the Visual Studio C++ tools and the .NET Framework compiler.
From `Modern Version Source`, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File wrapper\build.ps1
```

Compatibility details are in [WINDOWS_10_11_NOTES.txt](Modern%20Version%20Source/WINDOWS_10_11_NOTES.txt).
The monitor adapters also use [MinHook](https://github.com/TsudaKageyu/minhook); its source and license are included.
