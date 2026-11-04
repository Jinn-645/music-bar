# Review: Thanh taskbar tỉnh giấc

## Creative calls

- **Storyline:** #1, "Thanh taskbar tỉnh giấc". A character film: the taskbar itself is the mascot. The two storylines not built are in `STORYLINES.md`:
  - #2: a mock news bulletin;
  - #3: a taskbar livestream.
- **Two worlds:** a theatre stage for the character beats, and a drawn warm desktop for Windows 11 and the control center. The strip morphs into the full-width taskbar and back, so the eye never loses the carrier.
- **The stage** (added after the user's note that the flat ground was too plain):
  - pleated butter backdrop, coral velvet drapes, scalloped valance with marquee bulbs, plank floor and a spotlight;
  - it sits dark while the taskbar "nằm im";
  - when the icon lands, the bulbs light from the centre out, the drapes swing open and the spot comes on;
  - the bulbs chase on the beat during both dances and under the end card.
- **Real product only:** every MusicBar surface on screen is rendered by MusicBar's own code (`tools/capture`):
  - SeekRenderer: 7 styles;
  - LyricsPresenter: 3 text styles;
  - ControlCenterWindow: open, and with the output list;
  - the app icon.
  The control center shows two demo notifications injected by the capture tool, not the user's own. The output list shows this machine's playback devices.
- **Sound:**
  - Near silence for the first 14 s: clock ticks, a yawn, a sad trombone cut by a record scratch.
  - The bed enters on a bar line exactly as the icon lands. Its own button ending is the last frame.
  - The bed is carved under the voice (strength 0.8).
  - The master has a -1.5 dBTP true-peak ceiling.
- **Captions:** sticker karaoke (the halo version read as weak):
  - 74 px words with a thick charcoal outline and a hard drop shadow;
  - each word springs in, the spoken word swells, and the sweep fills sung words yellow;
  - key words are coral and a size up;
  - the last line has no caption, because the end card types it; "mười một" shows as "11".
- **Length:** 52.4 s. The film ends 2.4 s after the bed's button, which lands on "hát".

## Measured vs looked at

- **`hyperframes check`:** passes with 0 errors.
- **Contrast:** 3 `contrast_aa_failure` warnings remain, on captions over the light backdrop. The checker does not count the outline layer. Kept: the frames read clearly at full size.
- **Rendered file:** `renders/musicbar-promo.mp4`, rendered on sv.30 (GPU 1, beginframe capture, hardware GPU, 1 m 30 s).
  - 1920 x 1080, 30 fps, 52.4 s.
  - Audio: -14.3 LUFS integrated, true peak -1.4 dBTP, no clipping.
  - The bed's quiet ring-out reads as silence from 50.4 s.
- **Lint:** the warnings left are structural (`nested_structure_needs_subcomposition`, file size, dense track). The film is one continuous stage built as one file; splitting it would break the shared character rig.
- **Snapshots I looked at:** the review sheets (beats, seams, phone), and a second pass on every edited moment.
- **Audio:** measured on the rendered file with `hyperframes audio`, not heard (see the delivery note).

## Weakest shot

S6, the control center. It is a static capture of the real panel, carried by camera moves plus one real state change (the device list slides out). With more time, capture the panel opening and the list sliding as a frame sequence from the app itself.

## Kept warnings

The contrast and structure warnings above.
