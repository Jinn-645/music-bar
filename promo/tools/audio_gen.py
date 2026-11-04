"""Music bed and sound effects for the MusicBar promo, generated with ElevenLabs.

  python audio_gen.py music        -> assets/music/bed.mp3
  python audio_gen.py sfx [names]  -> assets/sfx/<name>.mp3
The API key is read from the user's .env and never printed.
"""
import json
import sys
import urllib.request
from pathlib import Path

ENV = Path(r"D:\TEST_PROJECTS\platform-make-game-tutoring\.env")
ROOT = Path(__file__).resolve().parent.parent


def key() -> str:
    for line in ENV.read_text(encoding="utf-8").splitlines():
        if line.strip().startswith("ELEVENLABS_API_KEY="):
            return line.split("=", 1)[1].strip().strip('"').strip("'")
    raise KeyError("ELEVENLABS_API_KEY")


def post(url: str, body: dict, out: Path) -> None:
    req = urllib.request.Request(url, data=json.dumps(body).encode("utf-8"), method="POST",
                                 headers={"xi-api-key": key(), "Content-Type": "application/json"})
    with urllib.request.urlopen(req, timeout=600) as r:
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_bytes(r.read())
    print(f"{out.relative_to(ROOT)}  {out.stat().st_size / 1024:.0f} KB")


MUSIC_PROMPT = (
    "Cute, playful and quirky instrumental for a funny product video. Ukulele, glockenspiel, "
    "pizzicato strings, finger snaps, light kick and handclaps, bright and bouncy, 116 BPM, "
    "major key, cheerful cartoon-commercial energy. No vocals. Steady groove from the first "
    "bar, a small lift in the middle, and a clean button ending."
)

# name: (prompt, seconds)
SFX = {
    "boing": ("cartoon spring boing, a toy bouncing once, short and cute", 1.0),
    "scratch": ("vinyl record scratch, comedic sudden stop of music", 1.0),
    "pop-1": ("soft bubbly cartoon pop, small and cute", 0.5),
    "pop-2": ("light plucky pop, like a cork, cute and short", 0.5),
    "click": ("one crisp computer mouse click, then silence", 0.5),
    "squeak": ("squeegee wiping a glass window, rubbery squeak, short", 1.3),
    "whoosh": ("fast airy whoosh, short swipe", 0.6),
    "tick": ("old wall clock, two soft ticks", 1.2),
    "yawn": ("cute cartoon creature yawning, short and soft", 1.6),
    "trombone": ("sad trombone wah wah wah, comedic fail", 2.2),
    "tada": ("magical sparkle shimmer ending in a cute ta-da hit", 1.8),
}


def main() -> None:
    what = sys.argv[1]
    if what == "music":
        post("https://api.elevenlabs.io/v1/music?output_format=mp3_44100_192",
             {"prompt": MUSIC_PROMPT, "music_length_ms": 64000}, ROOT / "assets" / "music" / "bed.mp3")
    elif what == "sfx":
        names = sys.argv[2:] or list(SFX)
        for name in names:
            prompt, seconds = SFX[name]
            post("https://api.elevenlabs.io/v1/sound-generation?output_format=mp3_44100_128",
                 {"text": prompt, "duration_seconds": seconds, "prompt_influence": 0.6},
                 ROOT / "assets" / "sfx" / f"{name}.mp3")


if __name__ == "__main__":
    main()
