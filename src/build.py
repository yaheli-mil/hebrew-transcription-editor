"""Builds the transcription editor: one installer that sets the editor up on any Windows 10/11 PC
with nothing else installed.

    python build.py                  -> dist/התקנת עורך תמלול.exe  (and dist/package/, the folder it installs)
    python build.py --no-installer   just the folder, for quick testing

The installed folder holds:
    עורך תמלול.exe           what the shortcut opens: runs the editor in the background, icon by the clock
    uninstall.exe            what Settings > Apps runs to remove it
    app/                     the editor (server, transcription, page, fonts)
    python/                  a private copy of Python 3.12 with only the libraries the app uses
    model/                   ivrit-ai/whisper-large-v3-turbo-ct2, the Hebrew speech model
    הקלטות/                  created on first run: the user's recordings and transcripts
"""
import importlib.metadata as md
import os
import shutil
import subprocess
import sys
import time
import zipfile
from pathlib import Path

SRC = Path(__file__).resolve().parent
sys.path.insert(0, str(SRC / "installer"))
import compile as programs                      # noqa: E402  (installer/compile.py)
OUT = SRC.parent / "dist"
NAME = "עורך תמלול"
DEST = OUT / "package"                          # the folder the installer carries (staging, not for sharing)
PY = Path(sys.base_prefix)                      # the Python 3.12 this script runs with
SITE = PY / "Lib" / "site-packages"
SYS32 = Path(os.environ["WINDIR"]) / "System32"

# What a real transcription + the server load (traced), plus the .libs folders their DLLs live in.
PACKAGES = ["av", "av.libs", "certifi", "charset_normalizer", "colorama", "ctranslate2", "faster_whisper",
            "filelock", "fsspec", "huggingface_hub", "idna", "more_itertools", "numpy", "numpy.libs",
            "onnxruntime", "packaging", "pkg_resources", "requests", "setuptools", "_distutils_hack",
            "tokenizers", "tqdm", "typing_extensions.py", "urllib3", "yaml", "_yaml"]
# Parts of packages the app never uses (developer tools); also keeps paths short.
PACKAGE_SKIP = {"onnxruntime": ("tools", "transformers", "quantization", "backend", "datasets")}
STDLIB_SKIP = {"site-packages", "test", "idlelib", "tkinter", "turtledemo", "ensurepip", "lib2to3", "__pycache__"}
DLLS_SKIP = {"_tkinter.pyd", "tcl86t.dll", "tk86t.dll"}
# The C++ runtime ctranslate2 and onnxruntime need; not every PC has the Visual C++ package that installs it.
RUNTIME = ["msvcp140.dll", "msvcp140_1.dll"]
MODEL_FILES = ["config.json", "model.bin", "preprocessor_config.json", "tokenizer.json", "vocabulary.json"]
APP_FILES = ["server.py", "core.py", "transcribe.py", "diarize.py", "index.html"]

def copy_tree(src, dst, skip=()):
    shutil.copytree(src, dst, ignore=shutil.ignore_patterns("__pycache__", *skip), dirs_exist_ok=True)


def dist_infos():
    """The .dist-info folders of every distribution that owns one of PACKAGES (some libraries read their own metadata)."""
    owners = md.packages_distributions()
    names = {d for p in PACKAGES for d in owners.get(p.removesuffix(".py").removesuffix(".libs"), [])}
    out = []
    for dist in names:
        path = Path(md.distribution(dist)._path)
        if path.parent == SITE and path.is_dir():
            out.append(path)
    return out


def build_python():
    py = DEST / "python"
    py.mkdir(parents=True)
    for f in ["python.exe", "pythonw.exe", "python3.dll", "python312.dll", "vcruntime140.dll", "vcruntime140_1.dll", "LICENSE.txt"]:
        shutil.copy2(PY / f, py / f)
    for f in RUNTIME:
        shutil.copy2(SYS32 / f, py / f)
    (py / "DLLs").mkdir()
    for f in (PY / "DLLs").iterdir():
        if f.name not in DLLS_SKIP:
            shutil.copy2(f, py / "DLLs" / f.name)
    lib = py / "Lib"
    lib.mkdir()
    for item in (PY / "Lib").iterdir():
        if item.name in STDLIB_SKIP:
            continue
        if item.is_dir():
            copy_tree(item, lib / item.name)
        else:
            shutil.copy2(item, lib / item.name)
    site = lib / "site-packages"
    site.mkdir()
    for name in PACKAGES:
        src = SITE / name
        if not src.exists():
            continue
        if src.is_dir():
            copy_tree(src, site / name, skip=("tests", "testing", *PACKAGE_SKIP.get(name, ())))
        else:
            shutil.copy2(src, site / name)
    for info in dist_infos():
        copy_tree(info, site / info.name)
    # Only these paths, whatever Python the user may have installed (PYTHONPATH, registry, user site...).
    (py / "python312._pth").write_text("Lib\nDLLs\nLib\\site-packages\n..\\app\nimport site\n", encoding="ascii")


def build():
    t0 = time.time()
    if DEST.exists():
        shutil.rmtree(DEST)
    DEST.mkdir(parents=True)
    print("python ...", flush=True)
    build_python()
    print("app ...", flush=True)
    app = DEST / "app"
    app.mkdir()
    for f in APP_FILES:
        shutil.copy2(SRC / "app" / f, app / f)
    copy_tree(SRC / "app" / "fonts", app / "fonts")
    print("model ...", flush=True)
    (DEST / "model").mkdir()
    for f in MODEL_FILES:
        shutil.copyfile(SRC / "model" / f, DEST / "model" / f)
    print("programs ...", flush=True)
    exes = programs.compile_all(SRC / "installer" / "build")
    shutil.copy2(exes["launcher"], DEST / "עורך תמלול.exe")
    shutil.copy2(exes["uninstall"], DEST / "uninstall.exe")
    print("compiling ...", flush=True)
    subprocess.run([str(DEST / "python" / "python.exe"), "-m", "compileall", "-q", "-j", "0",
                    str(DEST / "python" / "Lib"), str(app)], check=False)
    print(f"folder ready in {time.time() - t0:.0f}s: {size_of(DEST) / 1e9:.2f} GB", flush=True)


def size_of(path):
    return sum(p.stat().st_size for p in path.rglob("*") if p.is_file())


def make_installer():
    """setup.exe with the folder appended as a zip, then "EDITZIP1" and the zip's length (8 bytes)."""
    t0 = time.time()
    payload = OUT / "payload.zip.part"
    with zipfile.ZipFile(payload, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as z:
        for p in sorted(DEST.rglob("*")):
            if p.is_dir():
                continue
            stored = p.name == "model.bin"                # weights barely compress: store them as they are
            z.write(p, p.relative_to(DEST).as_posix(), compress_type=zipfile.ZIP_STORED if stored else zipfile.ZIP_DEFLATED)
    target = OUT / f"התקנת {NAME}.exe"
    tmp = target.with_suffix(".exe.part")
    size = payload.stat().st_size
    with open(tmp, "wb") as out:
        out.write((SRC / "installer" / "build" / "setup.exe").read_bytes())
        with open(payload, "rb") as f:
            shutil.copyfileobj(f, out, 1 << 22)
        out.write(b"EDITZIP1" + size.to_bytes(8, "little"))
    payload.unlink()
    os.replace(tmp, target)
    old = OUT / f"{NAME}.zip"                              # the earlier, zip-only way of sharing it
    if old.exists():
        old.unlink()
    print(f"installer ready in {time.time() - t0:.0f}s: {target.stat().st_size / 1e9:.2f} GB", flush=True)


if __name__ == "__main__":
    build()
    if "--no-installer" not in sys.argv:
        make_installer()
