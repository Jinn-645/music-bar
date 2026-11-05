# MusicBar YouTube Captions Bridge

This unpacked Manifest V3 extension reads the timed caption track that YouTube already exposes for the current video and sends the cue list to MusicBar at `127.0.0.1:19693`.

1. Open `chrome://extensions` or `brave://extensions`.
2. Enable **Developer mode**.
3. Choose **Load unpacked** and select this folder.
4. Open or reload a YouTube video that has captions.

The extension does not transcribe audio. It only reads captions supplied by YouTube. If no caption track exists, MusicBar keeps using its normal lyric sources.
