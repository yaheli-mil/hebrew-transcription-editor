"""Transcribe one recording: Hebrew speech to text, with who said what, fully offline.

    python transcribe.py NAME [--root FOLDER]

Writes <NAME>.txt next to the recording (one paragraph per turn, "label: text"),
plus the word timings the editor uses to follow the audio. Progress goes to
.data/<NAME>.progress.json so the editor can show it; a failure leaves
.data/<NAME>.error.json with a message for the user.
"""
import argparse
import os
import sys
import time
import traceback

os.environ.setdefault("HF_HUB_OFFLINE", "1")          # the model ships with the app: never touch the network
os.environ.setdefault("HF_HUB_DISABLE_TELEMETRY", "1")

import core
import diarize

MODEL_NAME = "ivrit-ai/whisper-large-v3-turbo-ct2"
UNSURE = 0.45            # words Whisper is less sure of than this get a "(?)" to check
SR = 16000


class Problem(Exception):
    """Something the user can understand and act on."""


def progress(name, stage, pct=0, eta=None):
    core.write_json(core.progress_path(name), {"stage": stage, "pct": pct, "eta": eta, "t": time.time()})


def keep_awake(on=True):
    """Stop the computer from going to sleep in the middle of a long transcription."""
    if os.name == "nt":
        import ctypes
        ES_CONTINUOUS, ES_SYSTEM_REQUIRED = 0x80000000, 0x00000001
        ctypes.windll.kernel32.SetThreadExecutionState(ES_CONTINUOUS | (ES_SYSTEM_REQUIRED if on else 0))


def to_m4a(src):
    """A copy of the recording's sound that every browser can play (video files, .amr, .wma...)."""
    import av

    dst = src.with_suffix(".m4a")
    tmp = core.DATA / (src.stem + ".converting.m4a")        # out of the recordings list until it is done
    try:
        with av.open(str(src), metadata_errors="ignore") as inp:
            stream = next((s for s in inp.streams if s.type == "audio"), None)
            if stream is None:
                raise Problem("בקובץ הזה אין שמע.")
            with av.open(str(tmp), "w", format="ipod") as out:
                if stream.codec_context.name == "aac":             # phone videos: just take the sound out
                    copy = out.add_stream_from_template(stream)
                    for packet in inp.demux(stream):
                        if packet.dts is None:
                            continue
                        packet.stream = copy
                        out.mux(packet)
                else:
                    enc = out.add_stream("aac", rate=48000)
                    enc.layout = "mono"
                    enc.bit_rate = 96000
                    resampler = av.AudioResampler(format="fltp", layout="mono", rate=48000)
                    fifo = av.AudioFifo()

                    def drain(final=False):
                        while fifo.samples >= 1024 or (final and fifo.samples):
                            frame = fifo.read(1024) if fifo.samples >= 1024 else fifo.read()
                            for packet in enc.encode(frame):
                                out.mux(packet)

                    for frame in inp.decode(stream):
                        for f in resampler.resample(frame):
                            f.pts = None
                            fifo.write(f)
                        drain()
                    for f in resampler.resample(None):
                        f.pts = None
                        fifo.write(f)
                    drain(final=True)
                    for packet in enc.encode(None):
                        out.mux(packet)
        os.replace(tmp, dst)
    finally:
        if tmp.exists():
            tmp.unlink()
    src.unlink()
    return dst


def build_turns(words, labels, names):
    """Consecutive words of the same speaker make one turn; uncertain words get "(?)" after them."""
    turns = []
    for (s, e, text, p), who in zip(words, labels):
        if not turns or turns[-1]["who"] != who:
            turns.append({"who": who, "sp": names[who], "tokens": []})
        turns[-1]["tokens"].append(text)
        if p < UNSURE and core.norm(text):
            turns[-1]["tokens"].append("(?)")
    return turns


def run(name):
    rec = core.recordings().get(name)
    if rec is None:
        raise Problem("ההקלטה לא נמצאה בתיקייה.")
    job = core.read_json(core.job_path(name)) or {}
    names = [job.get("interviewer") or "המראיין", job.get("interviewee") or "מרואיין"]

    progress(name, "prepare")
    if rec.suffix.lower() not in core.PLAYABLE:
        rec = to_m4a(rec)
    from faster_whisper import WhisperModel, decode_audio
    try:
        audio = decode_audio(str(rec), sampling_rate=SR)
    except Exception:
        raise Problem("לא הצלחתי לקרוא את הקובץ. ייתכן שהוא פגום או שזה לא קובץ שמע.")
    duration = len(audio) / SR
    if duration < 1:
        raise Problem("ההקלטה ריקה.")
    try:
        core.compute_env(name)                    # the waveform; cheap, and the editor shows it right away
    except Exception:
        pass

    progress(name, "load")
    if not core.model_ok():
        raise Problem("חסרה תיקיית המודל (model). צריך להתקין את העורך מחדש.")
    threads = max(2, min(6, (os.cpu_count() or 4) // 2))
    model = WhisperModel(str(core.MODEL_DIR), device="cpu", compute_type="int8", cpu_threads=threads)

    progress(name, "transcribe", 0)
    segments, info = model.transcribe(audio, language="he", beam_size=5, vad_filter=True,
                                      word_timestamps=True, condition_on_previous_text=False)
    words, t0, last = [], time.time(), 0.0
    for seg in segments:
        for w in seg.words or []:
            text = w.word.strip()
            if not text:
                continue
            if words and (text[0] in "'׳’" or not core.norm(text)):
                # Whisper splits "חבר'ה" into "חבר" + "'ה", and sometimes sends punctuation alone: rejoin
                s, _, prev, p = words[-1]
                words[-1] = (s, round(w.end, 3), prev + text, min(p, w.probability))
                continue
            words.append((round(w.start, 3), round(w.end, 3), text, w.probability))
        if time.time() - last > 1:
            last = time.time()
            done = min(seg.end / duration, 0.999)
            eta = (time.time() - t0) / done * (1 - done) if done > 0.03 else None
            progress(name, "transcribe", int(done * 100), round(eta) if eta else None)
    if not words:
        raise Problem("לא שמעתי דיבור בהקלטה הזו.")

    progress(name, "speakers", 100)
    labels = diarize.speakers(audio, [(s, e, t) for s, e, t, _ in words])
    turns = build_turns(words, labels, names)

    core.write_json(core.words_path(name), {
        "model": MODEL_NAME, "duration": round(duration, 3), "words": [[s, e, t] for s, e, t, _ in words]})
    text = core.format_txt(turns, "\r\n")
    core.write_atomic(core.txt_path(name), b"\xef\xbb\xbf" + text.encode("utf-8"))
    return len(words), duration


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser()
    ap.add_argument("name")
    ap.add_argument("--root")
    a = ap.parse_args()
    if a.root:
        core.configure(a.root)
    core.ensure_dirs()
    name = a.name
    core.error_path(name).unlink(missing_ok=True)
    keep_awake(True)
    t0 = time.time()
    try:
        import warnings
        warnings.filterwarnings("ignore")
        n, duration = run(name)
        print(f"{name}: {n} words, {duration / 60:.1f} min of audio in {(time.time() - t0) / 60:.1f} min", flush=True)
    except Problem as p:
        core.write_json(core.error_path(name), {"message": str(p)})
        sys.exit(1)
    except MemoryError:
        core.write_json(core.error_path(name), {"message": "אין מספיק זיכרון במחשב. סגרו תוכנות אחרות ונסו שוב."})
        sys.exit(1)
    except Exception:
        traceback.print_exc()
        core.write_json(core.error_path(name), {"message": "התמלול נכשל בגלל תקלה לא צפויה.",
                                                "detail": traceback.format_exc()[-2000:]})
        sys.exit(1)
    finally:
        core.progress_path(name).unlink(missing_ok=True)
        keep_awake(False)


if __name__ == "__main__":
    main()
