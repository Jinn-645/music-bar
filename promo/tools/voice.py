"""Voiceover for the MusicBar promo: Linh (queen-tts "linh-clone"), one line per request.

Writes assets/voice/<id>.wav and assets/voice/lines.json with every line's duration and
word timings (seconds from the line's start), which drive the timeline and the captions.
The API key comes from the QUEEN_TTS_KEY environment variable.
"""
import base64
import json
import os
import sys
import urllib.request
from pathlib import Path

API = "https://queen-tts.truclv.com/v2/multilang/timed"
KEY = os.environ.get("QUEEN_TTS_KEY", "")
OUT = Path(__file__).resolve().parent.parent / "assets" / "voice"

# id, style, text (what Linh says; the captions show the same words)
LINES = [
    ("v01", "storyteller", "Đây là thanh taskbar."),
    ("v02", "tired", "Ba mươi năm nay, việc duy nhất của nó là... nằm im."),
    ("v03", "sarcastic", "Nhạc phát cả ngày, mà nó chỉ biết... hiện mỗi cái đồng hồ."),
    ("v04", "surprised", "Cho đến hôm nay!"),
    ("v05", "excited", "MusicBar tới rồi!"),
    ("v06", "playful", "Thay áo mới nè: Cực quang, Sợi tơ, Ánh kim, Mực loang..."),
    ("v07", "playful", "Mười bốn bộ, có bộ còn nhún nhảy theo nhạc thật luôn."),
    ("v08", "cute", "Nó còn hát karaoke nữa. Đổi phông, đổi màu, tiếng Việt vẫn đủ dấu."),
    ("v09", "advert", "Lên Windows mười một? Taskbar trong suốt, sạch bong như mới lau kính."),
    ("v10", "playful", "Bấm vào cái loa: thông báo, cài đặt nhanh, đổi loa... gom hết một chỗ."),
    ("v11", "gentle", "Taskbar nhà bạn làm việc chăm chỉ cả ngày rồi."),
    ("v12", "cute", "Cho nó vui một chút đi."),
    ("v13", "happy", "MusicBar. Cho thanh taskbar biết hát."),
]


def synth(text: str, style: str) -> dict:
    body = json.dumps({"text": text, "speaker": "linh-clone", "instruct": style, "accent": "north",
                       "stream": False}, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(API, data=body, method="POST", headers={
        "X-API-Key": KEY, "Content-Type": "application/json; charset=utf-8"})
    with urllib.request.urlopen(req, timeout=180) as r:
        return json.load(r)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    only = set(sys.argv[1:])
    manifest_path = OUT / "lines.json"
    manifest = json.loads(manifest_path.read_text(encoding="utf-8")) if manifest_path.exists() else {}
    for line_id, style, text in LINES:
        if only and line_id not in only:
            continue
        data = synth(text, style)
        (OUT / f"{line_id}.wav").write_bytes(base64.b64decode(data["audio_wav_base64"]))
        words = [{"text": w["word"], "start": round(w["start"], 3), "end": round(w["end"], 3)}
                 for w in data.get("words", [])]
        manifest[line_id] = {"style": style, "text": text, "duration": round(data["duration"], 3), "words": words}
        print(f"{line_id} {data['duration']:5.2f}s {len(words):2d} words  {text}")
    manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=1), encoding="utf-8")


if __name__ == "__main__":
    main()
