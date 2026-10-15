"""Shared logic for the transcription editor.

Recordings and their transcripts live in the "הקלטות" folder next to the app.
The .txt next to each recording is the source of truth for the text. Everything
under הקלטות/.data is the app's own working data (word timings, loudness envelope,
aligned document, transcription jobs and their progress).
"""
import collections
import difflib
import hashlib
import json
import math
import os
import re
import shutil
import threading
import time
from pathlib import Path

HERE = Path(__file__).resolve().parent
APP_ROOT = HERE.parent                  # where the editor is installed
MODEL_DIR = APP_ROOT / "model"          # ivrit-ai/whisper-large-v3-turbo-ct2, shipped with the app
ROOT = APP_ROOT / "הקלטות"              # the recordings and their .txt files
DATA = ROOT / ".data"
BACKUPS = DATA / "backups"


def configure(root):
    """Work on another folder of recordings (its working data goes in <root>/.data)."""
    global ROOT, DATA, BACKUPS
    ROOT = Path(root).resolve()
    DATA = ROOT / ".data"
    BACKUPS = DATA / "backups"


def ensure_dirs():
    ROOT.mkdir(parents=True, exist_ok=True)
    fresh = not DATA.exists()
    DATA.mkdir(parents=True, exist_ok=True)
    if fresh and os.name == "nt":                # keep the app's working files out of the way
        try:
            import ctypes
            ctypes.windll.kernel32.SetFileAttributesW(str(DATA), 0x2)
        except Exception:
            pass


# Preference order when the same recording exists in several formats. The ones after
# PLAYABLE are converted to .m4a before transcription (browsers cannot play them).
AUDIO_EXTS = [".m4a", ".mp3", ".wav", ".aac", ".flac", ".ogg", ".opus", ".webm",
              ".mp4", ".mov", ".m4v", ".mkv", ".3gp", ".amr", ".wma", ".avi"]
PLAYABLE = {".m4a", ".mp3", ".wav", ".aac", ".flac", ".ogg", ".opus", ".webm"}
MIME = {
    ".m4a": "audio/mp4", ".mp3": "audio/mpeg", ".wav": "audio/wav", ".aac": "audio/aac",
    ".flac": "audio/flac", ".ogg": "audio/ogg", ".opus": "audio/ogg", ".webm": "audio/webm",
    ".mp4": "video/mp4",
}

PROGRESS_FRESH = 45      # seconds without a progress update before a transcription counts as dead
ENV_FPS = 50             # loudness envelope frames per second
BACKUP_EVERY = 600       # seconds between automatic backups while editing
CACHE_VERSION = 2        # bump when the alignment changes, so cached documents are rebuilt

_file_locks = collections.defaultdict(threading.Lock)


# ---------------------------------------------------------------- recordings

def recordings():
    """name -> audio path, for every recording in the interviews folder."""
    best = {}
    for p in ROOT.iterdir():
        ext = p.suffix.lower()
        if p.is_file() and ext in AUDIO_EXTS:
            rank = AUDIO_EXTS.index(ext)
            if p.stem not in best or rank < best[p.stem][0]:
                best[p.stem] = (rank, p)
    return {name: best[name][1] for name in sorted(best)}


def txt_path(name):
    return ROOT / f"{name}.txt"


def words_path(name):
    return DATA / f"{name}.words.json"


def env_path(name):
    return DATA / f"{name}.env.u8"


def sync_path(name):
    return DATA / f"{name}.sync.json"


def progress_path(name):
    return DATA / f"{name}.progress.json"


def job_path(name):
    """How to label the speakers when transcribing: {"interviewer": label, "interviewee": label}."""
    return DATA / f"{name}.job.json"


def error_path(name):
    return DATA / f"{name}.error.json"


def model_ok():
    return (MODEL_DIR / "model.bin").is_file() and (MODEL_DIR / "tokenizer.json").is_file()


def write_atomic(path, data):
    path.parent.mkdir(parents=True, exist_ok=True)
    tmp = path.with_name(path.name + ".tmp")
    with open(tmp, "wb") as f:
        f.write(data)
        f.flush()
        os.fsync(f.fileno())
    os.replace(tmp, path)


def write_json(path, obj):
    write_atomic(path, json.dumps(obj, ensure_ascii=False, separators=(",", ":")).encode("utf-8"))


def read_json(path):
    try:
        with open(path, "r", encoding="utf-8") as f:
            return json.load(f)
    except (OSError, ValueError):
        return None


def progress_of(name):
    """The running transcription's progress, or None if none is running."""
    try:
        if time.time() - progress_path(name).stat().st_mtime > PROGRESS_FRESH:
            return None
    except OSError:
        return None
    return read_json(progress_path(name))


# ---------------------------------------------------------------------- text

_LABEL = re.compile(r"^([^:\n]{1,40}):(?:[ \t]+|$)")
_KNOWN_LABEL = re.compile(r"^(מראיי|המראיי|מרואיי|דובר)|\(המראיי")
_NORM = re.compile(r"[^0-9a-zא-ת]")


def sha_of(data):
    return hashlib.sha1(data).hexdigest()


def read_txt(name):
    """-> (bytes, text) or (None, None) when there is no transcript yet."""
    try:
        raw = txt_path(name).read_bytes()
    except OSError:
        return None, None
    return raw, raw.decode("utf-8-sig", errors="replace")


def parse_txt(text):
    """Split a transcript into turns: [{'sp': label, 'tokens': [...]}]."""
    text = text.replace("\r\n", "\n").replace("\r", "\n")
    paras = [" ".join(p.split()) for p in re.split(r"\n[ \t]*\n", text)]
    paras = [p for p in paras if p]
    seen = collections.Counter()
    for p in paras:
        m = _LABEL.match(p)
        if m:
            seen[m.group(1).strip()] += 1
    labels = {l for l, c in seen.items() if c >= 2 or _KNOWN_LABEL.search(l)}
    turns = []
    for p in paras:
        m = _LABEL.match(p)
        if m and m.group(1).strip() in labels:
            turns.append({"sp": m.group(1).strip(), "tokens": p[m.end():].split()})
        else:
            turns.append({"sp": "", "tokens": p.split()})
    return turns


def format_txt(turns, newline="\r\n"):
    paras = []
    for t in turns:
        body = " ".join(t["tokens"])
        if body:
            paras.append(f"{t['sp']}: {body}" if t["sp"] else body)
    return (newline * 2).join(paras) + newline


def count_open_spots(text):
    return text.count("(?)") + text.count("[לא ברור")


def norm(tok):
    return _NORM.sub("", tok.lower())


def token_kinds(tokens):
    """'lex' = a spoken word, 'br' = inside a [bracket note], 'non' = marker/punctuation only."""
    kinds, in_br, run = [], False, 0
    for tok in tokens:
        if in_br or "[" in tok:
            kinds.append("br")
            run += 1
            in_br = "]" not in tok and run < 8
            if not in_br:
                run = 0
        elif norm(tok):
            kinds.append("lex")
        else:
            kinds.append("non")
    return kinds


# --------------------------------------------------------------------- audio

def compute_env(name):
    """Loudness envelope of a recording (uint8 dB scale, ENV_FPS frames/s), cached on disk."""
    path = env_path(name)
    with _file_locks["env:" + name]:
        if path.exists():
            return path.read_bytes()
        import av
        import numpy as np

        audio = recordings()[name]
        hop = 16000 // ENV_FPS
        resampler = av.AudioResampler(format="s16", layout="mono", rate=16000)
        frames, tail = [], np.zeros(0, dtype=np.float32)

        def push(chunk):
            nonlocal tail
            x = np.concatenate([tail, chunk.astype(np.float32)])
            n = len(x) // hop
            if n:
                frames.append(np.sqrt((x[:n * hop].reshape(n, hop) ** 2).mean(axis=1)))
            tail = x[n * hop:]

        with open(audio, "rb") as fh, av.open(fh, mode="r", metadata_errors="ignore") as container:
            for frame in container.decode(audio=0):
                for out in resampler.resample(frame):
                    push(out.to_ndarray().reshape(-1))
            for out in resampler.resample(None):
                push(out.to_ndarray().reshape(-1))
        rms = np.concatenate(frames) if frames else np.zeros(1, dtype=np.float32)
        db = 20 * np.log10(rms / 32768 + 1e-6)
        data = np.clip((db + 80) * (255 / 80), 0, 255).astype(np.uint8).tobytes()
        write_atomic(path, data)
        return data


def peaks(name, n=1200):
    """Waveform overview: n values 0..100."""
    import numpy as np

    env = np.frombuffer(compute_env(name), dtype=np.uint8).astype(np.float32)
    amp = 10 ** ((env * (80 / 255) - 80) / 20)
    n = max(1, min(n, len(amp)))
    edges = np.linspace(0, len(amp), n + 1).astype(int)
    out = np.array([amp[a:max(b, a + 1)].max() for a, b in zip(edges[:-1], edges[1:])])
    ref = np.percentile(out, 99) or 1.0
    out = np.clip(out / ref, 0, 1) ** 0.6
    return {"duration": len(env) / ENV_FPS, "peaks": [int(round(v * 100)) for v in out]}


def duration_of(name):
    try:
        return env_path(name).stat().st_size / ENV_FPS
    except OSError:
        w = read_json(words_path(name))
        return w.get("duration") if w else None


def refine_words(words, env_bytes):
    """Pull word edges out of silence.

    Whisper often stretches a word across the pause next to it, which makes the
    highlight jump ahead while nobody is speaking. Where a word's stated start
    (or end) falls in silence and the speech inside it starts later (or ends
    earlier), move the edge to the speech.
    """
    import numpy as np

    db = np.frombuffer(env_bytes, dtype=np.uint8).astype(np.float32)
    if len(db) < ENV_FPS:
        return words
    floor, top = np.percentile(db, 10), np.percentile(db, 95)
    if top - floor < 20:          # no clear speech/silence contrast: leave timings alone
        return words
    loud = db > floor + 0.35 * (top - floor)
    out = []
    for s, e, w in words:
        a, b = int(s * ENV_FPS), min(len(loud), int(math.ceil(e * ENV_FPS)))
        if b - a >= 5:
            idx = np.flatnonzero(loud[a:b])
            if idx.size >= 3:
                first, last = int(idx[0]), int(idx[-1])
                if first >= 2:
                    s = round(a / ENV_FPS + first / ENV_FPS - 0.02, 3)
                if (b - a - 1) - last >= 2:
                    e = round(a / ENV_FPS + (last + 1) / ENV_FPS + 0.02, 3)
                e = max(e, s + 0.04)
        out.append((s, e, w))
    return out


# ----------------------------------------------------------------- alignment

def align_tokens(tokens, words, duration):
    """Give every transcript token a (start, end) using the ASR word timings.

    tokens: the transcript words in order. words: [(start, end, text)] from Whisper.
    Tokens that Whisper also heard take its timing; the rest are spread over the
    time between their matched neighbours.
    """
    n = len(tokens)
    kinds = token_kinds(tokens)
    a_idx = [i for i, k in enumerate(kinds) if k == "lex"]
    b_idx = [j for j, w in enumerate(words) if norm(w[2])]
    sm = difflib.SequenceMatcher(None, [norm(tokens[i]) for i in a_idx],
                                 [norm(words[j][2]) for j in b_idx], autojunk=False)
    match = {}
    for i, j, size in sm.get_matching_blocks():
        for k in range(size):
            match[a_idx[i + k]] = b_idx[j + k]

    out = [None] * n
    for i, j in match.items():
        out[i] = (words[j][0], words[j][1])
    end_of_audio = max(duration or 0.0, words[-1][1] if words else 0.0)

    i = 0
    while i < n:
        if out[i] is not None:
            i += 1
            continue
        p = i
        while i < n and i not in match:
            i += 1
        q = i                                   # unmatched run is tokens[p:q]
        prev_j = match.get(p - 1) if p else None
        next_j = match.get(q) if q < n else None
        lo = words[prev_j][1] if prev_j is not None else 0.0
        hi = words[next_j][0] if next_j is not None else end_of_audio
        between = words[(prev_j + 1 if prev_j is not None else 0):
                        (next_j if next_j is not None else len(words))]
        between = [w for w in between if norm(w[2])]
        spoken = [k for k in range(p, q) if kinds[k] != "non"]

        if spoken and len(between) == len(spoken):
            # Same number of words on both sides (a correction, a spelling variant): map 1:1.
            for k, w in zip(spoken, between):
                out[k] = (w[0], w[1])
        elif spoken:
            if between and 0.5 <= len(spoken) / len(between) <= 2:
                lo, hi = max(lo, between[0][0]), max(lo, between[-1][1])
            hi = max(hi, lo)
            total = sum(len(tokens[k]) + 1 for k in spoken)
            t = lo
            for k in spoken:
                d = (hi - lo) * (len(tokens[k]) + 1) / total
                out[k] = (t, t + d)
                t += d
        # Markers such as "(?)" take no time: they sit at the end of the word before them.
        last_end = lo
        for k in range(p, q):
            if out[k] is None:
                out[k] = (last_end, last_end)
            else:
                last_end = out[k][1]

    prev = 0.0
    fixed = []
    for s, e in out:
        s = max(s, prev)
        e = max(e, s)
        fixed.append((round(s, 2), round(e, 2)))
        prev = s
    matched = len(match)
    return fixed, {"words": len(a_idx), "matched": matched}


# ----------------------------------------------------------------- documents

def words_sig(name):
    try:
        return words_path(name).stat().st_mtime_ns
    except OSError:
        return None


def load_doc(name):
    """The document the editor shows: turns of [token, start, end]."""
    raw, text = read_txt(name)
    if raw is None:
        return None
    sha, sig = sha_of(raw), words_sig(name)
    cached = read_json(sync_path(name))
    if (cached and cached.get("v") == CACHE_VERSION and cached.get("sha") == sha
            and cached.get("words_sig") == sig):
        return cached

    turns = parse_txt(text)
    doc = {"v": CACHE_VERSION, "name": name, "sha": sha, "words_sig": sig, "timed": False,
           "duration": duration_of(name)}
    wdata = read_json(words_path(name)) if sig is not None else None
    if wdata:
        words = [tuple(w) for w in wdata["words"]]
        try:
            words = refine_words(words, compute_env(name))
        except Exception:
            pass                                 # no envelope: keep Whisper's raw timings
        flat = [tok for t in turns for tok in t["tokens"]]
        times, stats = align_tokens(flat, words, wdata.get("duration"))
        k = 0
        for t in turns:
            t["w"] = [[tok, *times[k + i]] for i, tok in enumerate(t["tokens"])]
            k += len(t["tokens"])
        doc.update(timed=True, stats=stats, duration=wdata.get("duration") or doc["duration"])
    else:
        for t in turns:
            t["w"] = [[tok, None, None] for tok in t["tokens"]]
    doc["turns"] = [{"sp": t["sp"], "w": t["w"]} for t in turns]
    with _file_locks["txt:" + name]:
        write_json(sync_path(name), doc)
    return doc


class Conflict(Exception):
    """The transcript on disk changed since the editor loaded it."""


_last_written = {}   # name -> sha this process last wrote
_last_backup = {}    # name -> time of the last backup


def backup(name, raw, sha):
    stamp = time.strftime("%Y-%m-%d %H.%M.%S")
    dest, n = BACKUPS / name / f"{name} {stamp}.txt", 1
    dest.parent.mkdir(parents=True, exist_ok=True)
    while dest.exists() and dest.read_bytes() != raw:        # two different versions in the same second
        n += 1
        dest = BACKUPS / name / f"{name} {stamp} ({n}).txt"
    dest.write_bytes(raw)
    _last_backup[name] = time.time()


def clean_turns(turns):
    """Normalise turns that came from a browser: tidy labels, one token per word, no empty turns."""
    clean = []
    for t in turns:
        sp = " ".join(str(t.get("sp") or "").replace(":", " ").split())[:40]
        ws = []
        for item in t.get("w") or []:
            tok, s, e = item[0], item[1], item[2]
            for part in str(tok).split():
                ws.append([part,
                           round(float(s), 2) if isinstance(s, (int, float)) else None,
                           round(float(e), 2) if isinstance(e, (int, float)) else None])
        if ws:
            clean.append({"sp": sp, "w": ws})
    return clean


def save_doc(name, turns, base_sha, force=False):
    """Write the edited transcript back to its .txt (and refresh the timing cache)."""
    clean = clean_turns(turns)
    with _file_locks["txt:" + name]:
        raw, _ = read_txt(name)
        cur_sha = sha_of(raw) if raw is not None else None
        if raw is not None and cur_sha != base_sha and not force:
            raise Conflict(cur_sha)
        crlf = raw is None or b"\r\n" in raw
        bom = raw is None or raw.startswith(b"\xef\xbb\xbf")
        text = format_txt([{"sp": t["sp"], "tokens": [w[0] for w in t["w"]]} for t in clean],
                          "\r\n" if crlf else "\n")
        new = (b"\xef\xbb\xbf" if bom else b"") + text.encode("utf-8")
        new_sha = sha_of(new)
        if raw is not None and new != raw:
            stale = time.time() - _last_backup.get(name, 0) > BACKUP_EVERY
            if stale or _last_written.get(name) != cur_sha:
                backup(name, raw, cur_sha)
        if new != raw:
            write_atomic(txt_path(name), new)
        _last_written[name] = new_sha
        sig = words_sig(name)
        timed = sig is not None and any(w[1] is not None for t in clean for w in t["w"])
        write_json(sync_path(name), {
            # An untimed save must not look current once the word timings arrive.
            "v": CACHE_VERSION, "name": name, "sha": new_sha,
            "words_sig": sig if timed else None, "timed": timed,
            "duration": duration_of(name), "turns": clean,
        })
        return new_sha
