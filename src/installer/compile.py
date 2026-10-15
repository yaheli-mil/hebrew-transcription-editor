"""Compiles the installer, the launcher and the uninstaller with the C# compiler that ships with Windows."""
import os
import shutil
import subprocess
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent
CSC = Path(os.environ["WINDIR"]) / "Microsoft.NET" / "Framework64" / "v4.0.30319" / "csc.exe"
PROGRAMS = {"launcher": "Launcher.cs", "uninstall": "Uninstall.cs", "setup": "Setup.cs"}


def compile_all(out):
    out = Path(out)
    out.mkdir(parents=True, exist_ok=True)
    work = HERE / "build"
    work.mkdir(exist_ok=True)
    fonts = HERE.parent / "app" / "fonts"
    for f in ("Rubik-Regular.ttf", "Rubik-Bold.ttf"):
        shutil.copy2(fonts / f, work / f)
    shutil.copy2(HERE / "app.ico", work / "app.ico")
    common = ["/nologo", "/target:winexe", "/platform:anycpu", "/optimize+", "/codepage:65001",
              "/win32icon:" + str(HERE / "app.ico"), "/win32manifest:" + str(HERE / "app.manifest"),
              "/resource:" + str(work / "Rubik-Regular.ttf") + ",Rubik-Regular.ttf",
              "/resource:" + str(work / "Rubik-Bold.ttf") + ",Rubik-Bold.ttf",
              "/resource:" + str(work / "app.ico") + ",app.ico",
              "/r:System.Windows.Forms.dll", "/r:System.Drawing.dll", "/r:System.IO.Compression.dll",
              "/r:Microsoft.CSharp.dll", "/r:System.Core.dll"]
    built = {}
    for name, src in PROGRAMS.items():
        exe = out / f"{name}.exe"
        r = subprocess.run([str(CSC), *common, "/out:" + str(exe), str(HERE / "Ui.cs"), str(HERE / src)],
                           capture_output=True)
        text = r.stdout.decode("cp1255", errors="replace")
        msgs = [l for l in text.splitlines() if ": error" in l or ": warning" in l]
        for m in msgs:
            print("  ", m.replace(str(HERE) + "\\", ""))
        if r.returncode:
            sys.exit(f"{name}: compile failed")
        built[name] = exe
    return built


if __name__ == "__main__":
    for k, v in compile_all(sys.argv[1] if len(sys.argv) > 1 else HERE / "build").items():
        print(k, v.stat().st_size)
