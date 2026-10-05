from pathlib import Path
import zipfile


ROOT = Path(__file__).resolve().parent.parent
BUILD = ROOT / "installer" / "build"
RUNTIME = ROOT / "compat" / "otvdm" / "otvdm-v0.9.0"
OUTPUT = BUILD / "payload.zip"


def add_tree(zf: zipfile.ZipFile, source: Path, archive_root: Path) -> None:
    for path in sorted(source.rglob("*")):
        if path.is_file():
            relative = path.relative_to(source)
            zf.write(path, (archive_root / relative).as_posix())


def main() -> None:
    BUILD.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(OUTPUT, "w", zipfile.ZIP_DEFLATED) as zf:
        add_tree(zf, RUNTIME, Path("compat/otvdm/otvdm-v0.9.0"))

        files = {
            ROOT / "ScreenMateMonitorHost.exe": "ScreenMateMonitorHost.exe",
            ROOT / "ScreenMateMonitorHooks.dll": "ScreenMateMonitorHooks.dll",
            ROOT / "wrapper" / "vendor" / "minhook" / "LICENSE.txt": "MinHook-LICENSE.txt",
            ROOT / "Eminem ScreenMate.scr": "Eminem ScreenMate.scr",
            BUILD / "Eminem ScreenMate Control.exe": "Eminem ScreenMate Control.exe",
            BUILD / "Uninstall Eminem ScreenMate.exe": "Uninstall Eminem ScreenMate.exe",
            ROOT / "WINDOWS_10_11_NOTES.txt": "WINDOWS_10_11_NOTES.txt",
        }
        for source, archive_name in files.items():
            if not source.is_file():
                raise FileNotFoundError(source)
            zf.write(source, archive_name)

    print(f"Built {OUTPUT} ({OUTPUT.stat().st_size} bytes)")


if __name__ == "__main__":
    main()
