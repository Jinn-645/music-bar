"""Evens out the voiceover: every line from assets/voice/ to -16 LUFS with a -1.5 dBTP ceiling,
written to assets/vo/ (the files the film plays), so no line jumps out or drops away.
"""
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
SRC, OUT = ROOT / "assets" / "voice", ROOT / "assets" / "vo"
TARGET = -16.0


def loudness(path: Path) -> float:
    log = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", str(path), "-af", "ebur128", "-f", "null", "-"],
                         capture_output=True, text=True, encoding="utf-8", errors="replace").stderr
    return float(re.findall(r"I:\s+(-?[\d.]+) LUFS", log)[-1])


def main() -> None:
    OUT.mkdir(exist_ok=True)
    for wav in sorted(SRC.glob("v*.wav")):
        before = loudness(wav)
        gain = TARGET - before
        out = OUT / wav.name
        subprocess.run(["ffmpeg", "-y", "-loglevel", "error", "-i", str(wav), "-af",
                        f"volume={gain:.2f}dB,alimiter=limit=0.84:attack=2:release=60:level=disabled",
                        "-ar", "48000", str(out)], check=True)
        print(f"{wav.name}  {before:6.1f} -> {loudness(out):6.1f} LUFS  ({gain:+.1f} dB)")


if __name__ == "__main__":
    main()
