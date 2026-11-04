# SPEC: Thanh taskbar tỉnh giấc

```text
GOAL:    the viewer smiles at a sleepy taskbar waking up and remembers "MusicBar: cho thanh taskbar biết hát"
OUTPUT:  HyperFrames, 1920 x 1080, 30 fps
TIME:    53.67 s x 30 = 1610 frames. Bed 140 BPM = 12.857 frames a beat. It enters on a bar at
         14.00 s (media 24.465 s) and its button ending lands on the last frame. Beat k = 14.00 + 0.428571 k.
LOOK:    world A (home stage): butter yellow #FFE9A8 flat, 2.5 % fibre grain, 6 % warm vignette.
         world B (desktop): a drawn warm landscape wallpaper (peach sky #FFD7A3, coral sun #FF8A6B,
         hills #F2A271 / #E07A5F / #B95A4C) under a Windows 11 style taskbar 1920 x 72.
         ink #1D1F27; one accent, coral #FF6B6B (the "14", sung caption words, the end-card verb "hát").
         display: Be Vietnam Pro 700, 104 px, -2 % tracking; captions: Be Vietnam Pro 700, 60 px.
         hero material: a glossy charcoal strip (#2B2E39 to #15171E, 1 px top highlight at 18 %),
         contact shadow 0.30 at 22 px blur.
OBJECTS: the taskbar character: the strip itself. It has two white eyes with pupils and charcoal lids,
         and a small mouth. Centred app icons, then tray icons (wifi, loa, pin) and the clock "10:24".
         MusicBar's real renders: the seekbar clips, lyric clips, control-center PNGs and the app icon.
         Props: a microphone, a squeegee, a big arrow cursor, a cobweb, music notes.
MOTION:  arrivals expo.out 0.35-0.6 s; camera power2.inOut 0.5 s; exits power3.in, 40 % shorter.
         Character springs: back.out(1.7) on squash and scale, never on opacity.
         Squash on landing: scaleY 0.82 / scaleX 1.12, then a settle.
         Lids and pupils offset by 2 frames left to right. Idle breath: scaleY +1.5 % over 2.4 s.
SHOTS:
  0.00-11.3   S1 hook: the macro strip (1500 x 150) half asleep. A cobweb appears on "ba mươi năm".
              It yawns after "nằm im". Notes float past ignored on "nhạc phát cả ngày".
              Punch into the clock on "đồng hồ", then the clock ticks 10:24 -> 10:25.
  11.3-12.4   gag: a sad trombone cut by a record scratch; the frame freezes and the eyes pop open.
  12.4-15.6   S2 reveal: the MusicBar icon drops (boing) on the bar line at 14.00.
              The real Aurora seekbar floods the strip and the music starts.
  15.6-24.0   S3 thay áo: a spin and swap per style name.
              Cực quang, Sợi tơ, Ánh kim and Mực loang each get a tag.
              "14" slams in with a montage, then it lands on Phổ âm and the strip bounces on every beat.
  24.0-29.3   S4 karaoke: a mic pops in, the icons make room, and the real lyrics engine sings:
              Be Vietnam Pro, then Dancing Script ("đổi phông"), then Playfair ("đổi màu").
  29.3-35.1   S5 Windows 11: the strip morphs into a full-width taskbar on a desktop.
              A squeegee wipes it clear on "trong suốt", then a glint and sparkles on "sạch bong".
  35.1-40.7   S6 control center: the cursor clicks the speaker and the real panel springs up.
              The camera visits notifications, then quick settings. A click on the output row slides
              the device list out to the left, then the camera pulls back.
  40.7-47.0   S7 claim: the taskbar morphs back to the stage, tired (sweat drop), then dances to the beat.
  47.0-53.67  S8 end card: the icon pops out of the strip and rises.
              The "MusicBar" wordmark wipes in, then the tagline "Cho thanh taskbar biết hát" word by word.
              It holds 3 s and the music buttons out.
BANNED:  the spec.md defaults; also a fake Windows screenshot, a redrawn MusicBar UI,
         text that moves while it is read, and click rings.
QA:      0.5 (thumbnail reads: a sleepy taskbar), 10.7 (clock punch), 14.3 (icon landed, aurora flood),
         17.4 / 19.8 (outfit tags), 21.0 ("14"), 26.0 / 28.0 (lyric fonts), 32.0 (mid-wipe),
         36.8 (notifications), 38.9 (device list out), 45.5 (dance), 50.5 / 53.5 (end card)
```

## Cues (film seconds)

| line | start | anchor words |
| --- | --- | --- |
| v01 | 0.30 | taskbar 1.49 |
| v02 | 2.75 | nằm im 5.89 |
| v03 | 7.10 | đồng hồ 10.49 |
| v04 | 12.40 | hôm nay 12.87 |
| v05 | 13.60 | MusicBar 14.05 |
| v06 | 15.85 | Cực quang 17.11 · Sợi tơ 17.97 · Ánh kim 18.78 · Mực loang 19.65 |
| v07 | 20.65 | mười bốn 20.77 · nhún nhảy 22.16 |
| v08 | 24.05 | karaoke 24.87 · đổi phông 25.98 · đổi màu 26.83 · đủ dấu 28.35 |
| v09 | 29.55 | Windows 30.10 · trong suốt 32.34 · sạch bong 33.25 |
| v10 | 35.20 | loa 36.10 · thông báo 36.67 · cài đặt nhanh 37.51 · đổi loa 38.55 · gom hết 39.63 |
| v11 | 41.20 | chăm chỉ 43.25 |
| v12 | 44.90 | vui 45.55 |
| v13 | 47.20 | MusicBar 47.38 · biết hát 50.00 |
