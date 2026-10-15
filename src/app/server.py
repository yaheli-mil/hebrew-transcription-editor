"""Local server for the transcription editor.

Serves the editor page, takes in recordings, transcribes them one at a time in the
background (transcribe.py), plays them back with seeking, and writes edits to the
.txt files. Listens on 127.0.0.1 only: nothing leaves this computer.

    python server.py [--open] [--port 8770] [--root FOLDER]
"""
import argparse
import json
import os
import re
import subprocess
import sys
import threading
import time
import urllib.parse
import urllib.request
import webbrowser
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

import core

APP_ID = "transcription-editor-class"
PORTS = range(8770, 8780)
MAX_BODY = 20 * 1024 * 1024
MAX_UPLOAD = 6 * 1024 ** 3
CSP = ("default-src 'none'; script-src 'unsafe-inline'; style-src 'unsafe-inline'; "
       "connect-src 'self'; media-src 'self'; img-src 'self' data:; font-src 'self'; base-uri 'none'; form-action 'none'")
FONT_TYPES = {".ttf": "font/ttf", ".otf": "font/otf", ".woff2": "font/woff2"}
DATA_SUFFIXES = (".words.json", ".env.u8", ".sync.json", ".progress.json", ".job.json", ".error.json")
BAD_CHARS = re.compile(r'[<>:"/\\|?*\x00-\x1f]')


# ------------------------------------------------------------------ the transcription queue

class Queue:
    """Transcribes waiting recordings one at a time, each in its own process."""

    def __init__(self):
        self.current = None          # name being transcribed
        self.proc = None
        self.lock = threading.Lock()
        self.wake = threading.Event()

    def waiting(self):
        recs = core.recordings()
        names = [n for n in recs if not core.txt_path(n).exists() and not core.error_path(n).exists()]

        def created(n):
            job = core.read_json(core.job_path(n)) or {}
            return job.get("created") or recs[n].stat().st_mtime
        return sorted(names, key=created)

    def run(self):
        while True:
            try:
                todo = self.waiting()
            except Exception:
                todo = []
            if not todo:
                self.wake.wait(3)
                self.wake.clear()
                continue
            name = todo[0]
            log = open(core.DATA / "transcribe.log", "ab")
            cmd = [sys.executable, "-X", "utf8", str(core.HERE / "transcribe.py"), name, "--root", str(core.ROOT)]
            with self.lock:
                self.current = name
                # No window of its own, and a lower priority so the computer stays usable while it works.
                flags = getattr(subprocess, "BELOW_NORMAL_PRIORITY_CLASS", 0) | getattr(subprocess, "CREATE_NO_WINDOW", 0)
                self.proc = subprocess.Popen(cmd, cwd=str(core.HERE), stdout=log, stderr=subprocess.STDOUT,
                                             creationflags=flags)
            code = self.proc.wait()
            log.close()
            with self.lock:
                stopped = self.current is None           # deleted while it ran
                self.current, self.proc = None, None
            if code and not stopped and not core.error_path(name).exists() and name in core.recordings():
                core.write_json(core.error_path(name), {"message": "התמלול נעצר באמצע."})

    def stop(self, name):
        with self.lock:
            if self.current == name and self.proc:
                self.current = None
                self.proc.kill()
                try:
                    self.proc.wait(10)
                except Exception:
                    pass


QUEUE = Queue()


def list_recordings():
    out = []
    for name, path in core.recordings().items():
        raw, text = core.read_txt(name)
        item = {"name": name, "duration": core.duration_of(name), "playable": path.suffix.lower() in core.PLAYABLE}
        if raw is not None:
            item.update(status="ready", sha=core.sha_of(raw), open=core.count_open_spots(text))
        elif core.error_path(name).exists():
            err = core.read_json(core.error_path(name)) or {}
            item.update(status="error", message=err.get("message") or "התמלול נכשל.")
        elif QUEUE.current == name:
            prog = core.progress_of(name) or {}
            item.update(status="working", stage=prog.get("stage") or "prepare",
                        pct=prog.get("pct") or 0, eta=prog.get("eta"))
        else:
            item.update(status="queued")
        out.append(item)
    return out


# ------------------------------------------------------------------ files

def clean_name(raw):
    name = " ".join(BAD_CHARS.sub(" ", raw or "").split()).strip(" .")[:80]
    return name or "ריאיון"


def unique_name(name):
    taken = {n.lower() for n in core.recordings()}
    if name.lower() not in taken:
        return name
    n = 2
    while f"{name} ({n})".lower() in taken:
        n += 1
    return f"{name} ({n})"


def files_of(name):
    paths = [p for p in core.ROOT.iterdir() if p.is_file() and p.stem == name
             and p.suffix.lower() in core.AUDIO_EXTS + [".txt"]]
    paths += [core.DATA / (name + s) for s in DATA_SUFFIXES if (core.DATA / (name + s)).exists()]
    if (core.BACKUPS / name).is_dir():
        paths.append(core.BACKUPS / name)
    return paths


def to_recycle_bin(paths):
    """Move files to the Recycle Bin, so a deletion can still be undone from there."""
    import ctypes
    from ctypes import wintypes

    class SHFILEOPSTRUCTW(ctypes.Structure):
        _fields_ = [("hwnd", wintypes.HWND), ("wFunc", wintypes.UINT), ("pFrom", wintypes.LPCWSTR),
                    ("pTo", wintypes.LPCWSTR), ("fFlags", ctypes.c_uint16), ("fAnyOperationsAborted", wintypes.BOOL),
                    ("hNameMappings", ctypes.c_void_p), ("lpszProgressTitle", wintypes.LPCWSTR)]

    FO_DELETE, FOF_SILENT, FOF_NOCONFIRMATION, FOF_ALLOWUNDO, FOF_NOERRORUI = 3, 0x4, 0x10, 0x40, 0x400
    op = SHFILEOPSTRUCTW(wFunc=FO_DELETE, pFrom="\0".join(str(p) for p in paths) + "\0\0",
                         fFlags=FOF_SILENT | FOF_NOCONFIRMATION | FOF_ALLOWUNDO | FOF_NOERRORUI)
    return ctypes.windll.shell32.SHFileOperationW(ctypes.byref(op)) == 0


def delete_recording(name):
    QUEUE.stop(name)
    paths = files_of(name)
    if not paths:
        return
    if os.name == "nt" and to_recycle_bin(paths):
        return
    import shutil
    for p in paths:
        shutil.rmtree(p, ignore_errors=True) if p.is_dir() else p.unlink(missing_ok=True)


# ------------------------------------------------------------------ http

class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "TranscriptionEditor"

    def log_message(self, *args):
        pass

    def _send(self, code, body=b"", ctype="application/json; charset=utf-8", headers=None):
        headers = dict(headers or {})
        self.send_response(code)
        self.send_header("Content-Type", ctype)
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", headers.pop("Cache-Control", "no-store"))
        self.send_header("X-Content-Type-Options", "nosniff")
        for k, v in headers.items():
            self.send_header(k, v)
        self.end_headers()
        if self.command != "HEAD":
            self.wfile.write(body)

    def _json(self, obj, code=200):
        self._send(code, json.dumps(obj, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))

    def _local(self):
        """Only this machine's own browser may talk to the editor."""
        port = self.server.server_address[1]
        hosts = {f"127.0.0.1:{port}", f"localhost:{port}"}
        if self.headers.get("Host", "") not in hosts:
            return False
        origin = self.headers.get("Origin")
        return origin is None or origin in {f"http://{h}" for h in hosts}

    def do_GET(self):
        if not self._local():
            return self._send(403)
        url = urllib.parse.urlsplit(self.path)
        query = urllib.parse.parse_qs(url.query)
        name = (query.get("name") or [""])[0]
        try:
            if url.path == "/":
                body = (core.HERE / "index.html").read_bytes()
                self._send(200, body, "text/html; charset=utf-8", {"Content-Security-Policy": CSP})
            elif url.path.startswith("/fonts/"):
                font = core.HERE / "fonts" / urllib.parse.unquote(url.path[len("/fonts/"):])
                if font.parent != core.HERE / "fonts" or font.suffix.lower() not in FONT_TYPES or not font.is_file():
                    return self._send(404)
                self._send(200, font.read_bytes(), FONT_TYPES[font.suffix.lower()], {"Cache-Control": "public, max-age=604800"})
            elif url.path == "/api/ping":
                self._json({"app": APP_ID})
            elif url.path == "/api/status":
                self._json({"model": core.model_ok(), "folder": str(core.ROOT)})
            elif url.path == "/api/list":
                self._json({"recordings": list_recordings()})
            elif name not in core.recordings():
                self._send(404)
            elif url.path == "/api/doc":
                doc = core.load_doc(name)
                self._json(doc) if doc else self._json({"error": "no_txt"}, 404)
            elif url.path == "/api/peaks":
                self._json(core.peaks(name, int((query.get("n") or ["1200"])[0])))
            elif url.path == "/audio":
                self._audio(core.recordings()[name])
            else:
                self._send(404)
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass
        except Exception as exc:
            self._json({"error": str(exc)}, 500)

    do_HEAD = do_GET

    def _audio(self, path):
        size = path.stat().st_size
        start, end, code = 0, size - 1, 200
        m = re.match(r"bytes=(\d*)-(\d*)$", (self.headers.get("Range") or "").strip())
        if m and (m.group(1) or m.group(2)):
            if m.group(1):
                start = int(m.group(1))
                end = min(int(m.group(2)), size - 1) if m.group(2) else size - 1
            else:
                start = max(0, size - int(m.group(2)))
            if start > end:
                return self._send(416, headers={"Content-Range": f"bytes */{size}"})
            code = 206
        self.send_response(code)
        self.send_header("Content-Type", core.MIME.get(path.suffix.lower(), "application/octet-stream"))
        self.send_header("Accept-Ranges", "bytes")
        self.send_header("Content-Length", str(end - start + 1))
        self.send_header("Cache-Control", "private, max-age=600")
        if code == 206:
            self.send_header("Content-Range", f"bytes {start}-{end}/{size}")
        self.end_headers()
        if self.command == "HEAD":
            return
        with open(path, "rb") as f:
            f.seek(start)
            left = end - start + 1
            while left > 0:
                chunk = f.read(min(262144, left))
                if not chunk:
                    break
                self.wfile.write(chunk)
                left -= len(chunk)

    def _upload(self, query):
        """The body is the recording itself; name, extension and speaker labels come in the query."""
        length = int(self.headers.get("Content-Length") or 0)
        ext = "." + re.sub(r"[^a-z0-9]", "", ((query.get("ext") or [""])[0]).lower())
        if not 0 < length <= MAX_UPLOAD:
            return self._json({"error": "הקובץ גדול מדי."}, 413)
        if ext not in core.AUDIO_EXTS:
            return self._json({"error": "סוג הקובץ הזה לא נתמך. אפשר להעלות הקלטת שמע או וידאו (m4a, mp3, wav, mp4...)."}, 415)
        name = unique_name(clean_name((query.get("name") or [""])[0]))
        tmp = core.DATA / f"upload-{time.time_ns()}.part"
        left = length
        try:
            with open(tmp, "wb") as f:
                while left > 0:
                    chunk = self.rfile.read(min(1 << 20, left))
                    if not chunk:
                        raise ConnectionError("upload cut short")
                    f.write(chunk)
                    left -= len(chunk)

            def label(key, default):
                return " ".join(((query.get(key) or [""])[0]).replace(":", " ").split())[:40] or default
            core.write_json(core.job_path(name), {
                "interviewer": label("interviewer", "המראיין"),
                "interviewee": label("interviewee", "מרואיין"), "created": time.time()})
            os.replace(tmp, core.ROOT / f"{name}{ext}")
        finally:
            tmp.unlink(missing_ok=True)
        QUEUE.wake.set()
        self._json({"name": name})

    def do_POST(self):
        if not self._local():
            return self._send(403)
        url = urllib.parse.urlsplit(self.path)
        try:
            if url.path == "/api/upload":
                return self._upload(urllib.parse.parse_qs(url.query))
            if "application/json" not in self.headers.get("Content-Type", ""):
                return self._send(403)
            length = int(self.headers.get("Content-Length") or 0)
            if not 0 < length <= MAX_BODY:
                return self._send(413)
            req = json.loads(self.rfile.read(length).decode("utf-8"))
            if url.path == "/api/open-folder":
                os.startfile(str(core.ROOT))
                return self._json({"ok": True})
            name = req.get("name")
            if name not in core.recordings():
                return self._send(404)
            if url.path == "/api/delete":
                delete_recording(name)
                return self._json({"ok": True})
            if url.path == "/api/retry":
                core.error_path(name).unlink(missing_ok=True)
                QUEUE.wake.set()
                return self._json({"ok": True})
            if url.path != "/api/save":
                return self._send(404)
            try:
                sha = core.save_doc(name, req.get("turns") or [], req.get("base"), bool(req.get("force")))
            except core.Conflict as c:
                return self._json({"error": "conflict", "sha": str(c)}, 409)
            self._json({"sha": sha, "saved_at": time.strftime("%H:%M")})
        except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
            pass
        except Exception as exc:
            self._json({"error": str(exc)}, 500)


class Server(ThreadingHTTPServer):
    allow_reuse_address = False      # on Windows, reuse would let two servers share the port
    daemon_threads = True

    def handle_error(self, request, client_address):
        # Browsers drop idle and aborted connections all the time (every seek does it).
        if isinstance(sys.exc_info()[1], (ConnectionError, TimeoutError)):
            return
        super().handle_error(request, client_address)


def running_at():
    for port in PORTS:
        try:
            with urllib.request.urlopen(f"http://127.0.0.1:{port}/api/ping", timeout=0.5) as r:
                if json.load(r).get("app") == APP_ID:
                    return port
        except Exception:
            pass
    return None


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int)
    ap.add_argument("--open", action="store_true", help="open the editor in the browser")
    ap.add_argument("--root", help="folder of recordings (default: הקלטות next to the app)")
    ap.add_argument("--port-file", help="write the port here once listening (the app's launcher reads it)")
    a = ap.parse_args()
    if a.root:
        core.configure(a.root)
    core.ensure_dirs()

    port = None if os.environ.get("TE_IGNORE_RUNNING") else running_at()     # TE_IGNORE_RUNNING: for automated tests
    if port:
        print(f"The editor is already running: http://127.0.0.1:{port}/")
        if a.port_file:                                    # let the launcher use the copy that is already running
            core.write_atomic(core.Path(a.port_file), str(port).encode())
        if a.open:
            webbrowser.open(f"http://127.0.0.1:{port}/")
        return
    httpd = None
    for port in ([a.port] if a.port else PORTS):
        try:
            httpd = Server(("127.0.0.1", port), Handler)
            break
        except OSError:
            continue
    if httpd is None:
        sys.exit("Could not start: the ports 8770-8779 are all busy.")
    url = f"http://127.0.0.1:{httpd.server_address[1]}/"
    threading.Thread(target=QUEUE.run, daemon=True).start()
    if a.port_file:
        core.write_atomic(core.Path(a.port_file), str(httpd.server_address[1]).encode())
    print("Transcription editor is running:", url)
    print("Keep this window open while you work. Close it to stop the editor.")
    if not core.model_ok():
        print("WARNING: the 'model' folder is missing, so recordings cannot be transcribed.")
    if a.open and not os.environ.get("TE_NO_BROWSER"):          # TE_NO_BROWSER: for automated tests
        threading.Timer(0.6, webbrowser.open, args=(url,)).start()
    try:
        httpd.serve_forever()
    except KeyboardInterrupt:
        pass


if __name__ == "__main__":
    main()
