(() => {
  'use strict';
  const BRIDGE = 'http://127.0.0.1:19693/caption';
  const PREFERRED_LANGUAGE = 'vi';
  let lastVideo = '', lastUrl = '', cues = [], lastSent = 0;

  function playerResponse() {
    if (window.ytInitialPlayerResponse?.videoDetails) return window.ytInitialPlayerResponse;
    const cfg = window.ytplayer?.config?.args?.player_response;
    try { return cfg ? JSON.parse(cfg) : null; } catch (_) { return null; }
  }

  function captionTrack(response) {
    const tracks = response?.captions?.playerCaptionsTracklistRenderer?.captionTracks || [];
    if (!tracks.length) return null;
    return tracks.find(t => t.languageCode === PREFERRED_LANGUAGE)
      || tracks.find(t => t.languageCode === 'en') || tracks[0];
  }

  async function loadCues(response) {
    const track = captionTrack(response);
    if (!track?.baseUrl) return [];
    let url = track.baseUrl + (track.baseUrl.includes('?') ? '&' : '?') + 'fmt=json3';
    if (track.languageCode !== PREFERRED_LANGUAGE) url += '&tlang=' + encodeURIComponent(PREFERRED_LANGUAGE);
    const res = await fetch(url, { credentials: 'include' });
    if (!res.ok) return [];
    const data = await res.json();
    return (data.events || []).filter(e => e.segs && e.tStartMs != null)
      .map(e => ({ start: e.tStartMs / 1000, duration: (e.dDurationMs || 1500) / 1000,
        text: e.segs.map(s => s.utf8 || '').join('').replace(/\s+/g, ' ').trim() }))
      .filter(e => e.text && e.duration > 0);
  }

  async function tick() {
    const video = document.querySelector('video');
    const response = playerResponse();
    const details = response?.videoDetails;
    if (!video || !details?.videoId) return;
    const key = details.videoId + '|' + location.href;
    if (key !== lastVideo) {
      lastVideo = key;
      cues = await loadCues(response).catch(() => []);
    }
    if (!cues.length) return;
    const now = video.currentTime || 0;
    if (Date.now() - lastSent < 250) return;
    lastSent = Date.now();
    fetch(BRIDGE, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ videoId: details.videoId, title: details.title || document.title,
        artist: details.author || '', duration: Number(details.lengthSeconds || video.duration || 0),
        currentTime: now, language: PREFERRED_LANGUAGE, cues })
    }).catch(() => {});
  }

  setInterval(tick, 250);
  document.addEventListener('yt-navigate-finish', () => { lastVideo = ''; cues = []; });
})();
