"""Writes each <audio>'s real length (ffprobe) into its data-duration in index.html, so clips that
share a track don't read as overlapping. Clips that already carry a data-duration (a trim) keep it.
"""
import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
INDEX = ROOT / "index.html"


def length(src: str) -> float:
    out = subprocess.run(["ffprobe", "-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0",
                          str(ROOT / src)], capture_output=True, text=True, check=True).stdout
    return float(out)


def main() -> None:
    html = INDEX.read_text(encoding="utf-8")

    def fix(m: re.Match) -> str:
        tag = m.group(0)
        if "data-duration=" in tag:
            return tag
        src = re.search(r'src="([^"]+)"', tag).group(1)
        return tag.replace(' data-track-index=', f' data-duration="{int(length(src) * 100) / 100:.2f}" data-track-index=', 1)

    html, n = re.subn(r"<audio [^>]*>", fix, html)
    INDEX.write_text(html, encoding="utf-8")
    print(f"checked {n} audio elements")


if __name__ == "__main__":
    main()
