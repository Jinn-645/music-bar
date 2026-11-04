"""Karaoke captions for the promo, built from the voiceover's word timings.

Sticker style: heavy words with a thick charcoal outline and a hard drop shadow. Each word springs
in, the word being spoken swells, and the karaoke sweep fills sung words yellow. Key words are
coral and a size up.

  python captions.py  -> compositions/captions.html

START holds each line's place in the film (seconds); index.html places the same audio at the same times.
"""
import json
import re
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
FILM = 52.4
START = {"v01": 0.30, "v02": 2.75, "v03": 7.10, "v04": 12.40, "v05": 13.60, "v06": 15.85, "v07": 20.65,
         "v08": 24.05, "v09": 29.55, "v10": 35.20, "v11": 41.20, "v12": 44.90, "v13": 47.20}
# The end card types the last line itself, so it gets no caption.
SKIP = {"v13"}
# Spoken form -> what the caption shows.
MERGE = {("mười", "một?"): "11?"}
# Words that carry the pitch get the accent.
EMPHASIS = {"musicbar", "karaoke", "11", "nằm", "im", "đồng", "hồ", "mười", "bốn", "nhún", "nhảy", "trong",
            "suốt", "thông", "báo", "cài", "đặt", "nhanh", "loa", "vui", "phông", "màu", "dấu"}
MAX_WORDS, PAUSE = 5, 0.6


def words_of(line_id: str, line: dict) -> list[dict]:
    tokens = line["text"].split()
    timed = line["words"]
    assert len(tokens) == len(timed), f"{line_id}: {len(tokens)} tokens vs {len(timed)} timed words"
    t0 = START[line_id]
    out = [{"text": tok, "start": t0 + w["start"], "end": t0 + w["end"]} for tok, w in zip(tokens, timed)]
    merged, i = [], 0
    while i < len(out):
        pair = (out[i]["text"].lower(), out[i + 1]["text"].lower()) if i + 1 < len(out) else None
        if pair in MERGE:
            merged.append({"text": MERGE[pair], "start": out[i]["start"], "end": out[i + 1]["end"]})
            i += 2
        else:
            merged.append(out[i])
            i += 1
    return merged


def groups_of(words: list[dict]) -> list[list[dict]]:
    """Clauses first (punctuation or a long pause), then each long clause split into even groups,
    so no word is left alone on a line."""
    clauses, cur = [], []
    for i, w in enumerate(words):
        cur.append(w)
        nxt = words[i + 1] if i + 1 < len(words) else None
        ends_clause = re.search(r"[,.:?!…]$", w["text"]) is not None
        if nxt is None or ends_clause or nxt["start"] - w["end"] >= PAUSE:
            clauses.append(cur)
            cur = []
    groups = []
    for c in clauses:
        parts = -(-len(c) // MAX_WORDS)
        size = -(-len(c) // parts)
        groups += [c[i:i + size] for i in range(0, len(c), size)]
    return groups


def display(text: str, last: bool) -> str:
    text = text.replace("...", "…")
    if last and text.endswith(".") and not text.endswith("…"):
        text = text[:-1]
    return text


def emphasised(text: str) -> bool:
    return re.sub(r"[,.:?!…]", "", text).lower() in EMPHASIS


def main() -> None:
    lines = json.loads((ROOT / "assets" / "voice" / "lines.json").read_text(encoding="utf-8"))
    groups = []
    for line_id in START:
        if line_id in SKIP:
            continue
        for g in groups_of(words_of(line_id, lines[line_id])):
            groups.append(g)
    data = []
    for gi, g in enumerate(groups):
        show = g[0]["start"] - 0.1
        nxt = groups[gi + 1][0]["start"] - 0.12 if gi + 1 < len(groups) else FILM
        hide = min(g[-1]["end"] + 0.45, nxt)
        data.append({"in": round(show, 3), "out": round(hide, 3),
                     "w": [[display(w["text"], k == len(g) - 1), round(w["start"], 3), round(w["end"], 3),
                            1 if emphasised(w["text"]) else 0]
                           for k, w in enumerate(g)]})
    html = TEMPLATE.replace("__DATA__", json.dumps(data, ensure_ascii=False)).replace("__FILM__", str(FILM))
    out = ROOT / "compositions" / "captions.html"
    out.parent.mkdir(exist_ok=True)
    out.write_text(html, encoding="utf-8")
    print(f"{len(data)} caption groups -> {out.relative_to(ROOT)}")


TEMPLATE = """<!doctype html>
<html>
  <head>
    <meta charset="UTF-8" />
  </head>
  <body>
    <template>
      <style>
        @font-face {
          font-family: "Be Vietnam Pro";
          src: url("../assets/fonts/BeVietnamPro-700.ttf") format("truetype");
          font-weight: 700;
        }
        #root {
          position: absolute;
          inset: 0;
          pointer-events: none;
        }
        #captions-stack {
          position: absolute;
          left: 0;
          right: 0;
          bottom: 126px;
          height: 130px;
        }
        .cap-group {
          position: absolute;
          left: 0;
          right: 0;
          bottom: 0;
          text-align: center;
          font-family: "Be Vietnam Pro", sans-serif;
          font-weight: 700;
          font-size: 74px;
          line-height: 1.15;
          letter-spacing: -0.01em;
          visibility: hidden;
          filter: drop-shadow(0 7px 0 rgba(29, 31, 39, 0.5));
        }
        .cap-word {
          display: inline-block;
          margin: 0 0.2em;
          transform-origin: 50% 100%;
        }
        .cap-word.emph {
          font-size: 1.14em;
        }
        .cap-inner {
          position: relative;
          display: inline-block;
          transform-origin: 50% 80%;
        }
        .cap-stroke,
        .cap-fill {
          position: absolute;
          left: 0;
          top: 0;
          white-space: nowrap;
        }
        .cap-stroke {
          color: transparent;
          -webkit-text-stroke: 15px #1d1f27;
        }
        .cap-base {
          position: relative;
          color: #ffffff;
          white-space: nowrap;
        }
        .cap-word.emph .cap-base {
          color: #ff6b6b;
        }
        .cap-fill {
          color: #ffd84d;
          clip-path: inset(0 100% 0 0);
        }
      </style>
      <div id="root" data-composition-id="captions" data-width="1920" data-height="1080">
        <div id="captions-stack"></div>
      </div>
      <script>
        (function () {
          const groups = __DATA__;
          const stack = document.getElementById("captions-stack");
          const tl = gsap.timeline({ paused: true });
          const span = (cls, text) => {
            const s = document.createElement("span");
            s.className = cls;
            if (text !== undefined) s.textContent = text;
            return s;
          };
          groups.forEach((g, gi) => {
            const el = span("cap-group");
            el.id = "cap-" + gi;
            const words = [];
            g.w.forEach(([text, start, end, emph]) => {
              const word = span("cap-word" + (emph ? " emph" : ""));
              const inner = span("cap-inner");
              const fill = span("cap-fill", text);
              inner.append(span("cap-stroke", text), span("cap-base", text), fill);
              word.appendChild(inner);
              el.appendChild(word);
              words.push(word);
              // Karaoke sweep, and the spoken word swells while it is said.
              tl.fromTo(fill, { clipPath: "inset(0 100% 0 0)" },
                { clipPath: "inset(0 0% 0 0)", duration: Math.max(0.08, end - start), ease: "none" }, start);
              tl.to(inner, { scale: 1.16, y: -8, duration: 0.1, ease: "power2.out" }, start);
              tl.to(inner, { scale: 1, y: 0, duration: 0.2, ease: "power2.out" }, Math.max(start + 0.1, end));
            });
            stack.appendChild(el);
            tl.set(el, { visibility: "visible", rotation: gi % 2 ? 1.5 : -1.5 }, g.in);
            words.forEach((w, i) => {
              const t = g.in + i * 0.045;
              const tilt = (((gi * 7 + i * 13) % 5) - 2) * 4;
              tl.fromTo(w, { y: 34, scale: 0.4, rotation: tilt },
                { y: 0, scale: 1, rotation: 0, duration: 0.34, ease: "back.out(2.4)", immediateRender: false }, t);
              tl.fromTo(w, { opacity: 0 }, { opacity: 1, duration: 0.1, ease: "power1.out", immediateRender: false }, t);
              tl.to(w, { y: 22, scale: 0.85, opacity: 0, duration: 0.14, ease: "power2.in" }, g.out - 0.2 + i * 0.012);
            });
            tl.set(el, { visibility: "hidden" }, g.out);
          });
          tl.set({}, {}, __FILM__);
          window.__timelines["captions"] = tl;
        })();
      </script>
    </template>
  </body>
</html>
"""

if __name__ == "__main__":
    main()
