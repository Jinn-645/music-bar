# YouTube Captions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a YouTube timed-caption bridge that MusicBar can prefer while YouTube is playing.

**Architecture:** A Manifest V3 content script extracts YouTube's timed-text track and posts current cues to a localhost bridge. MusicBar owns the bridge and exposes a small in-memory `YouTubeCaptionService`; the existing lyric service remains the fallback.

**Tech Stack:** C#/.NET 10 WPF, `HttpListener`, System.Text.Json, Manifest V3 JavaScript.

## Global Constraints

- Bind only to `127.0.0.1`, never a public interface.
- Do not transmit cookies, audio, account data, or video bytes.
- Keep existing LRCLIB/NetEase behavior unchanged when no fresh YouTube cue exists.
- Apply MusicBar's existing lyric offset semantics to YouTube cue timestamps.

### Task 1: Add tested timed-caption core

**Files:**
- Create: `src/MusicBar/Services/YouTubeCaptions.cs`
- Create: `tests/YouTubeCaptions.Tests/YouTubeCaptions.Tests.csproj`
- Create: `tests/YouTubeCaptions.Tests/CueTests.cs`

- [ ] Write tests for selecting the cue at a timestamp, rejecting empty cues, and expiring stale heartbeats.
- [ ] Run the tests and verify they fail because the service is missing.
- [ ] Implement minimal immutable cue state and expiry logic.
- [ ] Run tests and verify they pass.

### Task 2: Host the localhost bridge

**Files:**
- Modify: `src/MusicBar/Services/YouTubeCaptions.cs`
- Modify: `src/MusicBar/App.xaml.cs`

- [ ] Add a loopback `HttpListener` accepting authenticated JSON POST payloads.
- [ ] Start it after app initialization and stop it during app exit.
- [ ] Expose the latest cue to the overlay/media update path without blocking the UI thread.
- [ ] Build the project.

### Task 3: Prefer YouTube cues in lyric presentation

**Files:**
- Modify: `src/MusicBar/Services/LyricsService.cs`
- Modify: `src/MusicBar/Views/LyricsPresenter.cs`
- Modify: `src/MusicBar/Views/TaskbarOverlay.cs`

- [ ] Add a YouTube-origin result path that is used only for a fresh YouTube bridge payload.
- [ ] Keep local files, LRCLIB, and NetEase as fallback paths.
- [ ] Apply the existing per-song offset to cue timestamps.
- [ ] Add source/status text for `YouTube captions`.
- [ ] Build and run the test suite.

### Task 4: Add the browser companion

**Files:**
- Create: `browser/youtube-captions/manifest.json`
- Create: `browser/youtube-captions/content.js`
- Create: `browser/youtube-captions/README.md`

- [ ] Read `captionTracks` from the YouTube player response and select the requested/default language.
- [ ] Fetch timed-text JSON/XML and normalize cue start/duration/text.
- [ ] Post heartbeats and current cue to the bridge using the configured token.
- [ ] Handle SPA navigation, pauses, video changes, and missing captions.
- [ ] Document loading the unpacked extension and configuring the bridge URL.

### Task 5: Verify and package

- [ ] Run all tests.
- [ ] Run `dotnet build src/MusicBar/MusicBar.csproj`.
- [ ] Validate the extension manifest and JavaScript syntax.
- [ ] Perform a manual YouTube-caption and no-caption fallback check.
- [ ] Commit the implementation.
