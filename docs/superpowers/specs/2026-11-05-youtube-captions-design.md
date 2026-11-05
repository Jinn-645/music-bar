# YouTube Captions in MusicBar

## Goal

Use YouTube's own timed caption track when the active media session is a YouTube video, so MusicBar displays the same timestamped captions instead of searching LRCLIB/NetEase.

## Design

Add a small Manifest V3 browser companion under `browser/youtube-captions`. A content script runs only on `youtube.com`, finds the current video element and the player response's `captionTracks`, fetches the selected timed-text track, and sends the active cue plus video metadata to MusicBar through a loopback HTTP endpoint. It reads the track data directly rather than scraping rendered caption DOM, preserving YouTube timestamps and supporting translated tracks when available.

MusicBar hosts a localhost listener bound to `127.0.0.1` on a fixed port with a random per-process bearer token. The browser companion discovers the token from a user-pasted/generated bridge URL, so arbitrary web pages cannot inject cues. MusicBar keeps the latest YouTube cue in memory, expires it after a short heartbeat timeout, and falls back to its existing lyric pipeline when the bridge is absent or stale.

## Behavior

- YouTube cue is preferred only while a YouTube session is active and heartbeat data is fresh.
- The extension sends title, artist/channel, duration, current time, caption language, and cue text/timing.
- If YouTube has no captions, the existing LRCLIB/NetEase behavior remains unchanged.
- The existing manual lyric offset remains applied to YouTube cues.
- No audio, cookies, account data, or video content is transmitted; only caption metadata is sent to localhost.

## Verification

- Unit tests cover timed-text cue selection, malformed cue rejection, expiry, and offset application.
- `dotnet build src/MusicBar/MusicBar.csproj` must pass.
- Extension lint/package checks must pass.
- Manual check with a YouTube video having captions and one without captions.
