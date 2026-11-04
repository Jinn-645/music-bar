---
workflow: general-video
flow: automation
storyboard: no
format: 1920x1080, 30 fps, YouTube / Facebook
length: about 55-60 s
---

# MusicBar promo: "Thanh taskbar tỉnh giấc"

## Message

MusicBar biến thanh taskbar nhàm chán thành sân khấu âm nhạc.

## Audience

Người dùng Windows Việt Nam. Video nói tiếng Việt và có phụ đề karaoke, để xem được cả khi tắt tiếng.

## Tone

Thú vị, hấp dẫn, dễ thương và hài hước.

## Feature order

1. Thay áo: 14 kiểu seekbar.
2. Karaoke: lời bài hát, phông chữ, màu chữ.
3. Taskbar Windows 11 trong suốt.
4. Control center: thông báo, cài đặt nhanh, đổi loa.

## Assets

- **Voice:** queen-tts, giọng `linh-clone`. Mỗi câu một kiểu đọc (`tools/voice.py`), đã cân về -16 LUFS (`tools/levels.py`).
- **Music and sound effects:** nhạc nền và 11 hiệu ứng tạo bằng ElevenLabs (`tools/audio_gen.py`).
- **Product:** mọi phần sản phẩm đều do code thật của MusicBar render ra (`tools/capture`):
  - seekbar: SeekRenderer;
  - lời bài hát: LyricsPresenter;
  - control center: ControlCenterWindow;
  - icon app.
- **The world around it:** thế giới xung quanh (taskbar nhân vật, desktop, con trỏ) là hình vẽ. Không quay màn hình thật của người dùng.

## Captions

Phụ đề karaoke theo kiểu lời bài hát của MusicBar.

## End card

Chữ "MusicBar" và câu tagline. Không có link tải.
