using System;
using MusicBar.Infrastructure;

namespace MusicBar.Models;

/// <summary>Where the progress layer sits relative to the Windows taskbar.</summary>
public enum LayerMode { Auto, Under, Over }

public enum WidgetPlacement { Auto, Left, Right }

/// <summary>Where lyrics appear when shown: the biggest empty stretch of taskbar, or right beside the cover.</summary>
public enum LyricsPlacement { LargestGap, BesideCover }

/// <summary>
/// The taskbar background MusicBar paints. Auto follows the Windows taskbar's light or dark mode, so the
/// taskbar's own icons (black in light mode) stay readable.
/// </summary>
public enum BarBackground { Dark, Tinted, Transparent, Light, LightTinted, Auto }

public enum AccentSource { AlbumArt, App, SystemAccent, Custom }

/// <summary>How the playback progress is drawn across the taskbar.</summary>
public enum SeekStyle { Classic, Aurora, Liquid, Neon, Spectrum, Waveform, Ribbon, Cinema, Mosaic, Mesh, Silk, Holo, Bokeh, Ink }

/// <summary>
/// Which way the progress runs: from the start of the taskbar (left, or the bottom of a
/// vertical one), from the other end, out from the middle, or in from both ends.
/// </summary>
public enum SeekDirection { FromStart, FromEnd, FromCenter, FromEnds }

/// <summary>Which taskbars MusicBar shows on when there are several screens.</summary>
public enum DisplayTarget { Main, All, One }

/// <summary>Where the player and the lyrics go: with the progress bar, or on screens of their own.</summary>
public enum PlayerTarget { SameAsBar, Main, All, One }

/// <summary>What sits on the taskbar besides the progress bar.</summary>
public enum PlayerMode { Full, LyricsOnly, Off }

/// <summary>Where the face sits on the taskbar.</summary>
public enum FacePlacement { BesidePlayer, Start, BeforeIcons, AfterIcons }

/// <summary>The language of everything MusicBar shows.</summary>
public enum AppLanguage { English, Vietnamese }

/// <summary>How the previous / play / next buttons beside the cover look.</summary>
public enum ControlStyle { Glow, Minimal, Capsule, Ring, Vinyl, Keys, Bubbles }

public enum LyricEffect { SlideUp, Fade, Swipe, Zoom, Words, Typewriter, Ticker, None }

public enum LyricsAlign { Center, Left }

/// <summary>
/// Every user-tweakable option. Bound directly by the settings window and
/// observed by the taskbar overlay, so changes apply live.
/// </summary>
public sealed class AppSettings : ObservableObject
{
    // ---------- Taskbar ----------
    LayerMode _layerMode = LayerMode.Auto;
    public LayerMode LayerMode { get => _layerMode; set => Set(ref _layerMode, value); }

    /// <summary>MusicBar's own see-through taskbar, for machines without TranslucentTB.</summary>
    bool _transparentTaskbar = true;
    public bool TransparentTaskbar { get => _transparentTaskbar; set => Set(ref _transparentTaskbar, value); }

    /// <summary>Fold the tray's volume icon and notification button into MusicBar's control center.</summary>
    bool _controlCenter = true;
    public bool ControlCenter { get => _controlCenter; set => Set(ref _controlCenter, value); }

    WidgetPlacement _widgetPlacement = WidgetPlacement.Right;
    public WidgetPlacement WidgetPlacement
    {
        get => _widgetPlacement;
        // "Auto" from older settings files means the default spot, beside the notification area.
        set => Set(ref _widgetPlacement, value == WidgetPlacement.Auto ? WidgetPlacement.Right : value);
    }

    /// <summary>Widget width with lyrics expanded.</summary>
    double _widgetMaxWidth = 560;
    public double WidgetMaxWidth { get => _widgetMaxWidth; set => Set(ref _widgetMaxWidth, Math.Clamp(Math.Round(value), 240, 1200)); }

    bool _lyricsExpanded;
    public bool LyricsExpanded { get => _lyricsExpanded; set => Set(ref _lyricsExpanded, value); }

    LyricsPlacement _lyricsPlacement = LyricsPlacement.BesideCover;
    public LyricsPlacement LyricsPlacement { get => _lyricsPlacement; set => Set(ref _lyricsPlacement, value); }

    bool _showInfoPopup = true;
    public bool ShowInfoPopup { get => _showInfoPopup; set => Set(ref _showInfoPopup, value); }

    bool _onlineTrackInfo = true;
    public bool OnlineTrackInfo { get => _onlineTrackInfo; set => Set(ref _onlineTrackInfo, value); }

    bool _hideWhenIdle = true;
    public bool HideWhenIdle { get => _hideWhenIdle; set => Set(ref _hideWhenIdle, value); }

    bool _hideInFullscreen = true;
    public bool HideInFullscreen { get => _hideInFullscreen; set => Set(ref _hideInFullscreen, value); }

    bool _startWithWindows;
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }

    string _pinnedAppId = "";
    public string PinnedAppId { get => _pinnedAppId; set => Set(ref _pinnedAppId, value ?? ""); }

    // ---------- Appearance ----------
    BarBackground _background = BarBackground.Auto;
    public BarBackground Background { get => _background; set => Set(ref _background, value); }

    double _backgroundOpacity = 0.86;
    public double BackgroundOpacity { get => _backgroundOpacity; set => Set(ref _backgroundOpacity, Math.Clamp(value, 0, 1)); }

    AccentSource _accentSource = AccentSource.AlbumArt;
    public AccentSource AccentSource { get => _accentSource; set => Set(ref _accentSource, value); }

    string _customColor = "#FF6B6B";
    public string CustomColor { get => _customColor; set => SetColor(ref _customColor, value); }

    /// <summary>Stores a valid color as #RRGGBB; anything else puts the old value back in the box.</summary>
    void SetColor(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string? name = null)
    {
        if (ColorUtil.TryParse(value, out var c)) Set(ref field, ColorUtil.ToHex(c), name);
        else OnPropertyChanged(name);
    }

    SeekStyle _seekStyle = SeekStyle.Aurora;
    public SeekStyle SeekStyle { get => _seekStyle; set => Set(ref _seekStyle, value); }

    SeekDirection _seekDirection = SeekDirection.FromStart;
    public SeekDirection SeekDirection { get => _seekDirection; set => Set(ref _seekDirection, value); }

    /// <summary>Let the seekbar follow the music (reads the system audio mix locally).</summary>
    bool _audioReactive = true;
    public bool AudioReactive { get => _audioReactive; set => Set(ref _audioReactive, value); }

    double _fillOpacity = 0.34;
    public double FillOpacity { get => _fillOpacity; set => Set(ref _fillOpacity, Math.Clamp(value, 0.05, 1)); }

    bool _showAlbumArt = true;
    public bool ShowAlbumArt { get => _showAlbumArt; set => Set(ref _showAlbumArt, value); }

    bool _showAppIcon = true;
    public bool ShowAppIcon { get => _showAppIcon; set => Set(ref _showAppIcon, value); }

    bool _showControls = true;
    public bool ShowControls { get => _showControls; set => Set(ref _showControls, value); }

    /// <summary>The player (cover and buttons) on the taskbar, only its lyrics, or neither (just the progress bar).</summary>
    PlayerMode _playerMode = PlayerMode.Full;
    public PlayerMode PlayerMode { get => _playerMode; set => Set(ref _playerMode, value); }

    DisplayTarget _displayTarget = DisplayTarget.Main;
    public DisplayTarget DisplayTarget { get => _displayTarget; set => Set(ref _displayTarget, value); }

    // The screen for DisplayTarget.One, by its device name (\\.\DISPLAY2).
    string _displayMonitor = "";
    public string DisplayMonitor { get => _displayMonitor; set => Set(ref _displayMonitor, value ?? ""); }

    PlayerTarget _playerTarget = PlayerTarget.SameAsBar;
    public PlayerTarget PlayerTarget { get => _playerTarget; set => Set(ref _playerTarget, value); }

    // The screen for PlayerTarget.One.
    string _playerMonitor = "";
    public string PlayerMonitor { get => _playerMonitor; set => Set(ref _playerMonitor, value ?? ""); }

    AppLanguage _language = AppLanguage.English;
    public AppLanguage Language { get => _language; set => Set(ref _language, value); }

    ControlStyle _controlStyle = ControlStyle.Glow;
    public ControlStyle ControlStyle { get => _controlStyle; set => Set(ref _controlStyle, value); }

    string _fontFamily = "Segoe UI Variable Text";
    public string FontFamily { get => _fontFamily; set => Set(ref _fontFamily, string.IsNullOrWhiteSpace(value) ? "Segoe UI" : value); }

    // ---------- Face ----------
    /// <summary>The little face that lives on the taskbar, as in the promo film.</summary>
    bool _faceEnabled = true;
    public bool FaceEnabled { get => _faceEnabled; set => Set(ref _faceEnabled, value); }

    Views.Face.FaceStyle _faceStyle = Views.Face.FaceStyle.Classic;
    public Views.Face.FaceStyle FaceStyle { get => _faceStyle; set => Set(ref _faceStyle, value); }

    FacePlacement _facePlacement = FacePlacement.BesidePlayer;
    public FacePlacement FacePlacement { get => _facePlacement; set => Set(ref _facePlacement, value); }

    /// <summary>The face's size against the taskbar's thickness.</summary>
    double _faceScale = 1.0;
    public double FaceScale { get => _faceScale; set => Set(ref _faceScale, Math.Clamp(value, 0.6, 1.0)); }

    /// <summary>Sleepy at night, waking up in the morning, a yawn after lunch.</summary>
    bool _faceFollowsClock = true;
    public bool FaceFollowsClock { get => _faceFollowsClock; set => Set(ref _faceFollowsClock, value); }

    bool _faceReactsToMusic = true;
    public bool FaceReactsToMusic { get => _faceReactsToMusic; set => Set(ref _faceReactsToMusic, value); }

    bool _faceWatchesPointer = true;
    public bool FaceWatchesPointer { get => _faceWatchesPointer; set => Set(ref _faceWatchesPointer, value); }

    /// <summary>With nothing playing, MusicBar steps aside but the face stays on the taskbar (and naps).</summary>
    bool _faceStaysWhenIdle = true;
    public bool FaceStaysWhenIdle { get => _faceStaysWhenIdle; set => Set(ref _faceStaysWhenIdle, value); }

    // ---------- Lyrics ----------
    bool _lyricsEnabled = true;
    public bool LyricsEnabled { get => _lyricsEnabled; set => Set(ref _lyricsEnabled, value); }

    LyricEffect _lyricEffect = LyricEffect.SlideUp;
    public LyricEffect LyricEffect { get => _lyricEffect; set => Set(ref _lyricEffect, value); }

    double _animationSpeed = 1.0;
    public double AnimationSpeed { get => _animationSpeed; set => Set(ref _animationSpeed, Math.Clamp(value, 0.4, 2.5)); }

    bool _karaoke = true;
    public bool Karaoke { get => _karaoke; set => Set(ref _karaoke, value); }

    bool _showNextLine = true;
    public bool ShowNextLine { get => _showNextLine; set => Set(ref _showNextLine, value); }

    double _lyricScale = 1.0;
    public double LyricScale { get => _lyricScale; set => Set(ref _lyricScale, Math.Clamp(value, 0.6, 2.0)); }

    string _lyricColor = "#FFFFFF";
    public string LyricColor { get => _lyricColor; set => SetColor(ref _lyricColor, value); }

    /// <summary>How solid the lyric text is; the next line and the unsung part follow it, fainter.</summary>
    double _lyricOpacity = 0.96;
    public double LyricOpacity { get => _lyricOpacity; set => Set(ref _lyricOpacity, Math.Clamp(value, 0.2, 1)); }

    /// <summary>A halo around the lyric text, so it stands off whatever runs underneath.</summary>
    bool _lyricStroke = true;
    public bool LyricStroke { get => _lyricStroke; set => Set(ref _lyricStroke, value); }

    string _lyricStrokeColor = "#000000";
    public string LyricStrokeColor { get => _lyricStrokeColor; set => SetColor(ref _lyricStrokeColor, value); }

    double _lyricStrokeOpacity = 0.75;
    public double LyricStrokeOpacity { get => _lyricStrokeOpacity; set => Set(ref _lyricStrokeOpacity, Math.Clamp(value, 0, 1)); }

    /// <summary>Halo size in pixels: small is a tight outline, large a soft glow.</summary>
    double _lyricStrokeRadius = 8;
    public double LyricStrokeRadius { get => _lyricStrokeRadius; set => Set(ref _lyricStrokeRadius, Math.Clamp(value, 1, 24)); }

    LyricsAlign _lyricsAlign = LyricsAlign.Left;
    public LyricsAlign LyricsAlign { get => _lyricsAlign; set => Set(ref _lyricsAlign, value); }

    int _lyricsOffsetMs;
    public int LyricsOffsetMs { get => _lyricsOffsetMs; set => Set(ref _lyricsOffsetMs, Math.Clamp(value, -5000, 5000)); }

    bool _fetchOnline = true;
    public bool FetchOnline { get => _fetchOnline; set => Set(ref _fetchOnline, value); }

    string _proxyUrl = "";
    public string ProxyUrl { get => _proxyUrl; set => Set(ref _proxyUrl, (value ?? "").Trim()); }

    string _lyricsFolder = "";
    public string LyricsFolder { get => _lyricsFolder; set => Set(ref _lyricsFolder, (value ?? "").Trim()); }
}
