using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Views;

/// <summary>
/// Renders the current lyric line (or a message such as the track title) and
/// animates line changes with the configured effect. Driven every frame by
/// <see cref="Update"/> with the playback position, so karaoke fill, marquee
/// scrolling and gap dots all stay locked to the music, including on seek
/// and pause.
/// </summary>
public sealed class LyricsPresenter : Grid
{
    sealed class Karaoke
    {
        public required LinearGradientBrush Brush;
        public required GradientStop Sung;
        public required GradientStop SungEdge;
        public required GradientStop UnsungEdge;
    }

    sealed class Slot
    {
        public readonly Grid Root = new();
        public readonly TranslateTransform Translate = new();
        public readonly ScaleTransform Scale = new();
        public LineView? Main;
        public TextBlock? MainText;
        public List<TextBlock>? Words;
        public List<(int Start, int Length)>? WordSpans;
        public int TotalChars = 1;
        public List<Karaoke>? Karaoke;
        public LyricLine? Line;
        public bool IsMessage;
        public Ellipse[]? Dots;
        public ScaleTransform? DotsScale;
        public TimeSpan GapStart, GapEnd;
        public string? TypeText;
        public int[]? Graphemes;
        public int Typed = -1;
        public DateTime Born = DateTime.Now;
    }

    enum Kind { Line, Gap, Empty }

    readonly record struct Target(string Key, Kind Kind, int Index, TimeSpan GapStart, TimeSpan GapEnd);

    const double Soft = 0.08;
    const double TickerDim = 0.36;
    static readonly TimeSpan IntroGap = TimeSpan.FromSeconds(3);
    static readonly TimeSpan BlankGap = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan HoldGrace = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan HoldGap = TimeSpan.FromSeconds(5);

    static readonly IEasingFunction EaseOut = Frozen(new QuarticEase { EasingMode = EasingMode.EaseOut });
    static readonly IEasingFunction EaseIn = Frozen(new QuadraticEase { EasingMode = EasingMode.EaseIn });
    static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });
    static readonly IEasingFunction Bounce = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 });

    // Text in the user's color: the line being sung at full strength, the next line and the
    // not-yet-sung part fainter. Rebuilt by SetTextStyle.
    Color _textColor = Colors.White;
    double _textOpacity = 0.96;
    Color UnsungColor => ColorUtil.WithAlpha(_textColor, _textOpacity * 0.55);
    Brush MainBrush = TextBrush(Colors.White, 0.96);
    Brush DimBrush = TextBrush(Colors.White, 0.62);
    Brush SubBrush = TextBrush(Colors.White, 0.44);

    static Brush TextBrush(Color c, double alpha) => Frozen(new SolidColorBrush(ColorUtil.WithAlpha(c, alpha)));

    LyricEffect _effect = LyricEffect.SlideUp;
    bool _karaoke = true;
    bool _twoLine;
    FontFamily _font = new("Segoe UI");
    double _mainSize = 15;
    double _subSize = 11;
    double _speed = 1;
    HorizontalAlignment _align = HorizontalAlignment.Center;
    Color _accent = ColorUtil.Brand;

    SyncedLyrics? _lyrics;
    string _msgPrimary = "";
    string _msgSecondary = "";
    Slot? _current;
    readonly List<Slot> _exiting = [];
    string? _currentKey;
    int _lastIndex = -1;
    bool _instant;
    TimeSpan _pos;
    bool _playing;

    Canvas? _tickerHost;
    StackPanel? _tickerPanel;
    readonly TranslateTransform _tickerX = new();
    readonly TranslateTransform _tickerScroll = new();
    readonly List<TextBlock> _tickerItems = [];
    readonly List<int> _tickerLineOfItem = [];
    int[] _tickerItemOfLine = [];
    double[] _tickerLeft = [];
    double[] _tickerWidth = [];
    int _tickerCurrent = -1;
    bool _tickerActive;
    Karaoke? _tickerKaraoke;

    public LyricsPresenter()
    {
        ClipToBounds = true;
        SizeChanged += (_, _) =>
        {
            if (_tickerPanel != null) PlaceTicker(animate: false);
        };
    }

    public bool HasLyrics => _lyrics != null;

    /// <summary>
    /// Where lyric text is drawn right now, in this element's coordinates (the current
    /// line, the next one, and lines still animating out), or null when nothing shows.
    /// </summary>
    public Rect? TextBounds()
    {
        if (ActualWidth < 1 || !IsVisible) return null;
        // The ticker runs lines across the whole strip.
        if (_effect == LyricEffect.Ticker && _tickerHost != null) return new Rect(RenderSize);

        Rect? all = null;
        void Add(Slot slot)
        {
            if (slot.Root.Opacity < 0.05 || !slot.Root.IsVisible) return;
            var drawn = VisualTreeHelper.GetDescendantBounds(slot.Root);
            if (drawn.IsEmpty || drawn.Width < 1) return;
            var r = slot.Root.TransformToAncestor(this).TransformBounds(drawn);
            all = all is { } a ? Rect.Union(a, r) : r;
        }
        if (_current != null) Add(_current);
        foreach (var slot in _exiting) Add(slot);
        if (all is not { } bounds) return null;
        bounds.Intersect(new Rect(RenderSize));
        return bounds.IsEmpty ? null : bounds;
    }

    /// <summary>The span of drawn text on screen (physical pixels), or (0, 0).</summary>
    public (double Left, double Right) TextScreenSpan()
    {
        if (TextBounds() is not { } text) return (0, 0);
        return (PointToScreen(new Point(text.Left, 0)).X, PointToScreen(new Point(text.Right, 0)).X);
    }

    public Color Accent
    {
        get => _accent;
        set
        {
            if (_accent == value) return;
            _accent = value;
            var sung = SungColor;
            if (_current?.Karaoke != null)
                foreach (var k in _current.Karaoke) k.Sung.Color = k.SungEdge.Color = sung;
            if (_tickerKaraoke != null) _tickerKaraoke.Sung.Color = _tickerKaraoke.SungEdge.Color = sung;
        }
    }

    Color SungColor => ColorUtil.WithAlpha(ColorUtil.Lerp(_textColor, _accent, 0.3), _textOpacity);

    // ------------------------------------------------------------------ public API

    /// <summary>The lyric text color and how solid it is; redraws the current line when it changes.</summary>
    public void SetTextStyle(Color color, double opacity)
    {
        if (color == _textColor && Math.Abs(opacity - _textOpacity) < 0.005) return;
        _textColor = color;
        _textOpacity = opacity;
        MainBrush = TextBrush(color, opacity);
        DimBrush = TextBrush(color, opacity * 0.65);
        SubBrush = TextBrush(color, opacity * 0.46);
        Rebuild();
    }

    public void Configure(LyricEffect effect, bool karaoke, bool twoLine, FontFamily font,
        double mainSize, double subSize, HorizontalAlignment align, double speed)
    {
        bool rebuild = effect != _effect || karaoke != _karaoke || twoLine != _twoLine
                       || font.Source != _font.Source || Math.Abs(mainSize - _mainSize) > 0.01
                       || Math.Abs(subSize - _subSize) > 0.01 || align != _align;
        _effect = effect;
        _karaoke = karaoke;
        _twoLine = twoLine;
        _font = font;
        _mainSize = mainSize;
        _subSize = subSize;
        _align = align;
        _speed = Math.Max(0.25, speed);
        if (rebuild) Rebuild();
    }

    public void ShowMessage(string primary, string? secondary)
    {
        secondary ??= "";
        if (_lyrics == null && _current?.IsMessage == true && primary == _msgPrimary && secondary == _msgSecondary) return;
        _msgPrimary = primary;
        _msgSecondary = secondary;
        _lyrics = null;
        FadeOutTicker();
        _lastIndex = -1;
        _currentKey = "M";
        Transition(CreateMessageSlot(), forward: true);
    }

    public void ShowLyrics(SyncedLyrics lyrics)
    {
        _lyrics = lyrics;
        _currentKey = null;
        _lastIndex = -1;
        if (_effect != LyricEffect.Ticker) return;

        if (_current != null)
        {
            var old = _current;
            _current = null;
            _exiting.Add(old);
            Animate(old.Root, OpacityProperty, null, 0, Ms(300), EaseIn, () => Remove(old));
        }
        BuildTicker();
    }

    /// <summary>Replays the entrance of what is on screen (used by "preview" in settings).</summary>
    public void Preview()
    {
        if (_lyrics != null)
        {
            if (_tickerPanel != null)
            {
                BuildTicker();
                return;
            }
            _currentKey = null;
            _lastIndex = -1;
            Update(_pos, _playing);
        }
        else
        {
            Transition(CreateMessageSlot(), forward: true);
        }
    }

    public void Update(TimeSpan position, bool playing)
    {
        _pos = position;
        _playing = playing;
        if (_lyrics != null)
        {
            if (_tickerPanel != null)
            {
                UpdateTicker(position, instant: false);
                return;
            }

            var t = Resolve(position);
            if (t.Key != _currentKey)
            {
                bool forward = _currentKey == null || t.Index >= _lastIndex;
                var slot = t.Kind switch
                {
                    Kind.Line => CreateLineSlot(t.Index),
                    Kind.Gap => CreateGapSlot(t.GapStart, t.GapEnd),
                    _ => NewSlot(),
                };
                Transition(slot, forward);
                _currentKey = t.Key;
                _lastIndex = t.Index;
            }
        }
        if (_current != null) UpdateSlot(_current, position);
    }

    // ------------------------------------------------------------------ timeline resolution

    Target Resolve(TimeSpan pos)
    {
        var lines = _lyrics!.Lines;
        int first = NextNonBlank(0);
        if (first < 0) return new Target("none", Kind.Empty, -1, default, default);

        int i = _lyrics.IndexAt(pos);
        if (i < first)
        {
            var firstStart = lines[first].Start;
            return firstStart >= IntroGap
                ? new Target("intro", Kind.Gap, -1, TimeSpan.Zero, firstStart)
                : new Target($"L{first}", Kind.Line, first, default, default);
        }

        var line = lines[i];
        int ni = NextNonBlank(i + 1);
        var next = ni >= 0 ? lines[ni] : null;

        if (line.IsBlank)
        {
            if (next == null) return new Target("end", Kind.Empty, i, default, default);
            if (next.Start - line.Start >= BlankGap) return new Target($"G{i}", Kind.Gap, i, line.Start, next.Start);
            int pi = PrevNonBlank(i - 1);
            return new Target($"L{pi}", Kind.Line, pi, default, default);
        }

        if (next != null)
        {
            var holdEnd = line.Start + Cap(line) + HoldGrace;
            if (pos > holdEnd && next.Start - holdEnd >= HoldGap)
                return new Target($"H{i}", Kind.Gap, i, holdEnd, next.Start);
        }
        return new Target($"L{i}", Kind.Line, i, default, default);
    }

    int NextNonBlank(int from)
    {
        var lines = _lyrics!.Lines;
        for (int k = Math.Max(0, from); k < lines.Count; k++)
            if (!lines[k].IsBlank) return k;
        return -1;
    }

    int PrevNonBlank(int from)
    {
        var lines = _lyrics!.Lines;
        for (int k = Math.Min(from, lines.Count - 1); k >= 0; k--)
            if (!lines[k].IsBlank) return k;
        return -1;
    }

    /// <summary>How long the line is actually sung: next line start, capped by a length-based estimate.</summary>
    static TimeSpan Cap(LyricLine l)
    {
        double natural = (l.End - l.Start).TotalSeconds * 0.95;
        double estimate = 0.9 + l.Text.Length * 0.17;
        return TimeSpan.FromSeconds(Math.Max(0.4, Math.Min(natural, estimate)));
    }

    static double Progress(LyricLine l, TimeSpan pos)
        => Math.Clamp((pos - l.Start).TotalSeconds / Cap(l).TotalSeconds, 0, 1);

    // ------------------------------------------------------------------ slot construction

    Slot NewSlot()
    {
        var s = new Slot();
        var group = new TransformGroup();
        group.Children.Add(s.Scale);
        group.Children.Add(s.Translate);
        s.Root.RenderTransform = group;
        s.Root.RenderTransformOrigin = _align == HorizontalAlignment.Center ? new Point(0.5, 0.5) : new Point(0, 0.5);
        return s;
    }

    TextBlock MakeText(string text, double size, FontWeight weight) => new()
    {
        Text = text,
        FontFamily = _font,
        FontSize = size,
        FontWeight = weight,
        TextWrapping = TextWrapping.NoWrap,
        VerticalAlignment = VerticalAlignment.Center,
    };

    Slot CreateLineSlot(int index)
    {
        var lines = _lyrics!.Lines;
        var line = lines[index];
        var slot = NewSlot();
        slot.Line = line;

        FrameworkElement content;
        if (_effect == LyricEffect.Words)
        {
            content = BuildWords(line.Text, slot);
        }
        else
        {
            var tb = MakeText(line.Text, _mainSize, FontWeights.SemiBold);
            slot.MainText = tb;
            if (_karaoke) slot.Karaoke = [AttachKaraoke(tb)];
            else tb.Foreground = MainBrush;
            if (_effect == LyricEffect.Typewriter) slot.TypeText = line.Text;
            content = tb;
        }

        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        slot.Main = new LineView(content, _align);
        stack.Children.Add(slot.Main);

        if (_twoLine)
        {
            int ni = NextNonBlank(index + 1);
            if (ni >= 0)
            {
                var sub = MakeText(lines[ni].Text, _subSize, FontWeights.Normal);
                sub.Foreground = SubBrush;
                stack.Children.Add(new LineView(sub, _align) { Margin = new Thickness(0, 1, 0, 0) });
            }
        }
        slot.Root.Children.Add(stack);
        return slot;
    }

    FrameworkElement BuildWords(string text, Slot slot)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        slot.Words = [];
        slot.WordSpans = [];
        if (_karaoke) slot.Karaoke = [];

        int chars = 0;
        double space = _mainSize * 0.28;
        for (int k = 0; k < words.Length; k++)
        {
            var tb = MakeText(words[k], _mainSize, FontWeights.SemiBold);
            tb.RenderTransform = new TranslateTransform();
            if (k < words.Length - 1) tb.Margin = new Thickness(0, 0, space, 0);
            if (slot.Karaoke != null) slot.Karaoke.Add(AttachKaraoke(tb));
            else tb.Foreground = MainBrush;
            slot.Words.Add(tb);
            slot.WordSpans.Add((chars, words[k].Length));
            chars += words[k].Length + 1;
            panel.Children.Add(tb);
        }
        slot.TotalChars = Math.Max(1, chars - 1);
        return panel;
    }

    Karaoke AttachKaraoke(TextBlock tb)
    {
        var sung = SungColor;
        var k = new Karaoke
        {
            Brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) },
            Sung = new GradientStop(sung, 0),
            SungEdge = new GradientStop(sung, -Soft),
            UnsungEdge = new GradientStop(UnsungColor, 0),
        };
        k.Brush.GradientStops.Add(k.Sung);
        k.Brush.GradientStops.Add(k.SungEdge);
        k.Brush.GradientStops.Add(k.UnsungEdge);
        k.Brush.GradientStops.Add(new GradientStop(UnsungColor, 1));
        tb.Foreground = k.Brush;
        return k;
    }

    static void SetKaraoke(Karaoke k, double progress)
    {
        double edge = progress * (1 + Soft);
        k.SungEdge.Offset = edge - Soft;
        k.UnsungEdge.Offset = edge;
    }

    Slot CreateGapSlot(TimeSpan start, TimeSpan end)
    {
        var slot = NewSlot();
        slot.GapStart = start;
        slot.GapEnd = end;
        double d = Math.Max(5, Math.Round(_mainSize * 0.42));
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = _align == HorizontalAlignment.Center ? HorizontalAlignment.Center : HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransformOrigin = new Point(0.5, 0.5),
        };
        slot.DotsScale = new ScaleTransform();
        panel.RenderTransform = slot.DotsScale;
        slot.Dots = new Ellipse[3];
        for (int k = 0; k < 3; k++)
        {
            var dot = new Ellipse
            {
                Width = d,
                Height = d,
                Fill = Brushes.White,
                Opacity = 0.22,
                Margin = new Thickness(k == 0 ? 0 : d * 0.85, 0, 0, 0),
            };
            slot.Dots[k] = dot;
            panel.Children.Add(dot);
        }
        slot.Root.Children.Add(panel);
        return slot;
    }

    Slot CreateMessageSlot()
    {
        var slot = NewSlot();
        slot.IsMessage = true;
        var stack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        if (_twoLine && _msgSecondary.Length > 0)
        {
            var title = MakeText(_msgPrimary, _mainSize, FontWeights.SemiBold);
            title.Foreground = MainBrush;
            var sub = MakeText(_msgSecondary, _subSize, FontWeights.Normal);
            sub.Foreground = DimBrush;
            slot.Main = new LineView(title, _align);
            stack.Children.Add(slot.Main);
            stack.Children.Add(new LineView(sub, _align) { Margin = new Thickness(0, 1, 0, 0) });
        }
        else
        {
            var tb = new TextBlock
            {
                FontFamily = _font,
                FontSize = _mainSize,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.NoWrap,
                VerticalAlignment = VerticalAlignment.Center,
            };
            tb.Inlines.Add(new Run(_msgPrimary) { Foreground = MainBrush });
            if (_msgSecondary.Length > 0)
            {
                tb.Inlines.Add(new Run("   ·   ") { Foreground = SubBrush, FontWeight = FontWeights.Normal });
                tb.Inlines.Add(new Run(_msgSecondary) { Foreground = DimBrush, FontWeight = FontWeights.Normal });
            }
            slot.Main = new LineView(tb, _align);
            stack.Children.Add(slot.Main);
        }
        slot.Root.Children.Add(stack);
        return slot;
    }

    // ------------------------------------------------------------------ transitions

    void Transition(Slot next, bool forward)
    {
        foreach (var s in _exiting) Children.Remove(s.Root);
        _exiting.Clear();

        var old = _current;
        _current = next;
        Children.Add(next.Root);
        if (old != null) _exiting.Add(old);

        var effect = _instant ? LyricEffect.None : _effect;
        if (effect == LyricEffect.Ticker) effect = LyricEffect.Fade;
        if (effect == LyricEffect.Typewriter && next.TypeText == null) effect = LyricEffect.Fade;
        if (effect == LyricEffect.Words && next.Words == null) effect = LyricEffect.SlideUp;

        var d = Ms(460);
        double dir = forward ? 1 : -1;
        next.Born = DateTime.Now;

        switch (effect)
        {
            case LyricEffect.None:
                if (old != null) Remove(old);
                break;

            case LyricEffect.Fade:
                Animate(next.Root, OpacityProperty, 0, 1, d, EaseOut);
                if (old != null) Animate(old.Root, OpacityProperty, null, 0, d * 0.7, EaseIn, () => Remove(old));
                break;

            case LyricEffect.SlideUp:
            {
                double dy = Math.Max(ActualHeight * 0.8, 14) * dir;
                Animate(next.Translate, TranslateTransform.YProperty, dy, 0, d, EaseOut);
                Animate(next.Root, OpacityProperty, 0, 1, d * 0.8, EaseOut);
                if (old != null)
                {
                    Animate(old.Translate, TranslateTransform.YProperty, null, -dy, d, EaseOut);
                    Animate(old.Root, OpacityProperty, null, 0, d * 0.55, EaseOut, () => Remove(old));
                }
                break;
            }

            case LyricEffect.Swipe:
                Swipe(next, old, d * 1.15, forward);
                break;

            case LyricEffect.Zoom:
            {
                Animate(next.Scale, ScaleTransform.ScaleXProperty, 0.78, 1, d, EaseOut);
                Animate(next.Scale, ScaleTransform.ScaleYProperty, 0.78, 1, d, EaseOut);
                var blurIn = new BlurEffect { Radius = 12 };
                next.Root.Effect = blurIn;
                Animate(blurIn, BlurEffect.RadiusProperty, 12, 0, d, EaseOut, () => next.Root.Effect = null);
                Animate(next.Root, OpacityProperty, 0, 1, d * 0.7, EaseOut);
                if (old != null)
                {
                    Animate(old.Scale, ScaleTransform.ScaleXProperty, null, 1.18, d, EaseOut);
                    Animate(old.Scale, ScaleTransform.ScaleYProperty, null, 1.18, d, EaseOut);
                    var blurOut = new BlurEffect { Radius = 0 };
                    old.Root.Effect = blurOut;
                    Animate(blurOut, BlurEffect.RadiusProperty, 0, 10, d, EaseOut);
                    Animate(old.Root, OpacityProperty, null, 0, d * 0.6, EaseOut, () => Remove(old));
                }
                break;
            }

            case LyricEffect.Words:
            {
                for (int k = 0; k < next.Words!.Count; k++)
                {
                    var word = next.Words[k];
                    var shift = (TranslateTransform)word.RenderTransform;
                    var delay = TimeSpan.FromMilliseconds(k * 55 / _speed);
                    word.Opacity = 0;
                    shift.Y = _mainSize * 0.9;
                    Animate(word, OpacityProperty, 0, 1, d * 0.8, EaseOut, delay: delay);
                    Animate(shift, TranslateTransform.YProperty, _mainSize * 0.9, 0, d * 1.1, Bounce, delay: delay);
                }
                if (old != null)
                {
                    Animate(old.Translate, TranslateTransform.YProperty, null, -_mainSize * 0.8 * dir, d * 0.6, EaseIn);
                    Animate(old.Root, OpacityProperty, null, 0, d * 0.45, EaseIn, () => Remove(old));
                }
                break;
            }

            case LyricEffect.Typewriter:
                next.Typed = -1;
                if (next.MainText != null)
                {
                    next.MainText.Inlines.Clear();
                    next.MainText.Inlines.Add(new Run(next.TypeText!) { Foreground = Brushes.Transparent });
                }
                if (old != null)
                {
                    Animate(old.Translate, TranslateTransform.YProperty, null, -6 * dir, d * 0.4, EaseIn);
                    Animate(old.Root, OpacityProperty, null, 0, d * 0.4, EaseIn, () => Remove(old));
                }
                break;
        }
    }

    void Swipe(Slot next, Slot? old, TimeSpan d, bool forward)
    {
        const double soft = 0.18;
        var start = forward ? new Point(0, 0.5) : new Point(1, 0.5);
        var end = forward ? new Point(1, 0.5) : new Point(0, 0.5);
        double dx = forward ? 18 : -18;

        var inA = new GradientStop(Colors.Black, -soft);
        var inB = new GradientStop(Colors.Transparent, 0);
        var inMask = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        inMask.GradientStops.Add(inA);
        inMask.GradientStops.Add(inB);
        next.Root.OpacityMask = inMask;
        Animate(inA, GradientStop.OffsetProperty, -soft, 1, d, EaseInOut);
        Animate(inB, GradientStop.OffsetProperty, 0, 1 + soft, d, EaseInOut, () => next.Root.OpacityMask = null);
        Animate(next.Translate, TranslateTransform.XProperty, dx, 0, d, EaseOut);

        if (old == null) return;
        var outA = new GradientStop(Colors.Transparent, -soft);
        var outB = new GradientStop(Colors.Black, 0);
        var outMask = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        outMask.GradientStops.Add(outA);
        outMask.GradientStops.Add(outB);
        old.Root.OpacityMask = outMask;
        Animate(outA, GradientStop.OffsetProperty, -soft, 1, d, EaseInOut);
        Animate(outB, GradientStop.OffsetProperty, 0, 1 + soft, d, EaseInOut, () => Remove(old));
        Animate(old.Translate, TranslateTransform.XProperty, null, -dx, d, EaseOut);
    }

    void Remove(Slot slot)
    {
        Children.Remove(slot.Root);
        _exiting.Remove(slot);
    }

    void Rebuild()
    {
        DestroyTicker();
        foreach (var s in _exiting) Children.Remove(s.Root);
        _exiting.Clear();
        if (_current != null)
        {
            Children.Remove(_current.Root);
            _current = null;
        }
        _currentKey = null;
        _lastIndex = -1;

        _instant = true;
        try
        {
            if (_lyrics == null)
            {
                _currentKey = "M";
                Transition(CreateMessageSlot(), forward: true);
            }
            else if (_effect == LyricEffect.Ticker)
            {
                BuildTicker();
            }
            else
            {
                Update(_pos, _playing);
            }
        }
        finally
        {
            _instant = false;
        }
    }

    // ------------------------------------------------------------------ per-frame updates

    void UpdateSlot(Slot s, TimeSpan pos)
    {
        if (s.Line != null)
        {
            double p = Progress(s.Line, pos);
            if (s.Karaoke != null)
            {
                if (s.WordSpans != null)
                {
                    double sung = p * s.TotalChars;
                    for (int k = 0; k < s.Karaoke.Count; k++)
                    {
                        var (start, length) = s.WordSpans[k];
                        SetKaraoke(s.Karaoke[k], Math.Clamp((sung - start) / Math.Max(1, length), 0, 1));
                    }
                }
                else
                {
                    SetKaraoke(s.Karaoke[0], p);
                }
            }
            if (s.Main is { Overflow: > 0.5 } main)
                main.Scroll = main.Overflow * Smooth(Math.Clamp((p - 0.1) / 0.75, 0, 1));
            if (s.TypeText != null && s.MainText != null) Type(s);
        }
        else if (s.Dots != null)
        {
            UpdateDots(s, pos);
        }
        else if (s.IsMessage && _playing && s.Main is { Overflow: > 0.5 } m)
        {
            // Ping-pong marquee with a pause at each end.
            const double hold = 2.2;
            double travel = Math.Max(1, m.Overflow / 38.0);
            double cycle = 2 * (travel + hold);
            double t = (DateTime.Now - s.Born).TotalSeconds % cycle;
            double x = t < hold ? 0
                : t < hold + travel ? (t - hold) / travel
                : t < 2 * hold + travel ? 1
                : 1 - (t - 2 * hold - travel) / travel;
            m.Scroll = m.Overflow * Smooth(x);
        }
    }

    void Type(Slot s)
    {
        var text = s.TypeText!;
        var tb = s.MainText!;
        // Advance by grapheme so decomposed Vietnamese diacritics never split from their letter.
        s.Graphemes ??= StringInfo.ParseCombiningCharacters(text);
        int count = s.Graphemes.Length;
        double total = Math.Clamp(count * 0.03, 0.25, 1.0) / _speed;
        int n = (int)Math.Ceiling(count * Math.Clamp((DateTime.Now - s.Born).TotalSeconds / total, 0, 1));
        if (n == s.Typed) return;
        s.Typed = n;
        tb.Inlines.Clear();
        if (n >= count)
        {
            tb.Text = text;
            s.TypeText = null;
            return;
        }
        int cut = s.Graphemes[n];
        tb.Inlines.Add(new Run(text[..cut]));
        tb.Inlines.Add(new Run(text[cut..]) { Foreground = Brushes.Transparent });
    }

    void UpdateDots(Slot s, TimeSpan pos)
    {
        double total = Math.Max(0.1, (s.GapEnd - s.GapStart).TotalSeconds);
        double p = Math.Clamp((pos - s.GapStart).TotalSeconds / total, 0, 1);
        for (int k = 0; k < 3; k++)
            s.Dots![k].Opacity = 0.22 + 0.78 * Math.Clamp(p * 3 - k, 0, 1);

        double scale = 1;
        if (_playing) scale += 0.09 * Math.Sin((DateTime.Now - s.Born).TotalSeconds * Math.PI * 2 / 1.7);
        double remaining = (s.GapEnd - pos).TotalSeconds;
        if (remaining < 0.35) scale *= 1 + 0.3 * (1 - Math.Max(0, remaining) / 0.35);
        s.DotsScale!.ScaleX = s.DotsScale.ScaleY = scale;
    }

    static double Smooth(double t) => t * t * (3 - 2 * t);

    // ------------------------------------------------------------------ ticker

    void BuildTicker()
    {
        DestroyTicker();
        var lines = _lyrics!.Lines;
        _tickerItemOfLine = new int[lines.Count];
        double gap = Math.Round(_mainSize * 2.4);

        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        var group = new TransformGroup();
        group.Children.Add(_tickerScroll);
        group.Children.Add(_tickerX);
        panel.RenderTransform = group;

        int item = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (!lines[i].IsBlank)
            {
                var tb = MakeText(lines[i].Text, _mainSize, FontWeights.SemiBold);
                tb.Foreground = MainBrush;
                tb.Opacity = TickerDim;
                tb.Margin = new Thickness(0, 0, gap, 0);
                panel.Children.Add(tb);
                _tickerItems.Add(tb);
                _tickerLineOfItem.Add(i);
                item = _tickerItems.Count - 1;
            }
            _tickerItemOfLine[i] = item;
        }

        _tickerLeft = new double[_tickerItems.Count];
        _tickerWidth = new double[_tickerItems.Count];
        double x = 0;
        for (int k = 0; k < _tickerItems.Count; k++)
        {
            var tb = _tickerItems[k];
            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            _tickerLeft[k] = x;
            _tickerWidth[k] = tb.DesiredSize.Width - gap;
            x += tb.DesiredSize.Width;
        }

        _tickerHost = new Canvas();
        _tickerHost.Children.Add(panel);
        _tickerPanel = panel;
        Children.Add(_tickerHost);
        _tickerCurrent = -1;
        _tickerActive = false;
        _tickerScroll.X = 0;
        if (!_instant) Animate(_tickerHost, OpacityProperty, 0, 1, Ms(450), EaseOut);
        UpdateTicker(_pos, instant: true);
    }

    void UpdateTicker(TimeSpan pos, bool instant)
    {
        if (_tickerItems.Count == 0 || _lyrics == null) return;
        int line = _lyrics.IndexAt(pos);
        int item = line < 0 ? -1 : _tickerItemOfLine[line];
        int focus = Math.Max(0, item);
        bool active = item >= 0 && !_lyrics.Lines[line].IsBlank;

        if (focus != _tickerCurrent || active != _tickerActive)
        {
            var d = Ms(420);
            if (_tickerCurrent >= 0 && _tickerCurrent < _tickerItems.Count && _tickerCurrent != focus)
            {
                var previous = _tickerItems[_tickerCurrent];
                previous.Foreground = MainBrush;
                Animate(previous, OpacityProperty, null, TickerDim, d, EaseOut);
            }

            var current = _tickerItems[focus];
            if (active)
            {
                _tickerKaraoke = _karaoke ? AttachKaraoke(current) : null;
                Animate(current, OpacityProperty, null, 1, d, EaseOut);
            }
            else
            {
                current.Foreground = MainBrush;
                _tickerKaraoke = null;
                Animate(current, OpacityProperty, null, TickerDim, d, EaseOut);
            }

            _tickerCurrent = focus;
            _tickerActive = active;
            _tickerScroll.X = 0;
            PlaceTicker(animate: !instant && !_instant);
        }

        if (!active) return;
        double p = Progress(_lyrics.Lines[_tickerLineOfItem[focus]], pos);
        if (_tickerKaraoke != null) SetKaraoke(_tickerKaraoke, p);
        double overflow = _tickerWidth[focus] - (ActualWidth - 24);
        _tickerScroll.X = overflow > 0 ? -overflow * Smooth(Math.Clamp((p - 0.1) / 0.75, 0, 1)) : 0;
    }

    void PlaceTicker(bool animate)
    {
        if (_tickerPanel == null || _tickerCurrent < 0 || _tickerCurrent >= _tickerItems.Count) return;
        int k = _tickerCurrent;
        double w = ActualWidth;
        double target = _tickerWidth[k] > w - 24
            ? -_tickerLeft[k] + 12
            : _align == HorizontalAlignment.Center
                ? w / 2 - (_tickerLeft[k] + _tickerWidth[k] / 2)
                : -_tickerLeft[k];

        Canvas.SetTop(_tickerPanel, Math.Round((ActualHeight - _tickerItems[k].DesiredSize.Height) / 2));
        if (animate)
        {
            Animate(_tickerX, TranslateTransform.XProperty, null, target, Ms(650), EaseInOut);
        }
        else
        {
            _tickerX.BeginAnimation(TranslateTransform.XProperty, null);
            _tickerX.X = target;
        }
    }

    void DestroyTicker()
    {
        if (_tickerHost != null) Children.Remove(_tickerHost);
        ResetTickerState();
    }

    void FadeOutTicker()
    {
        if (_tickerHost == null) return;
        var host = _tickerHost;
        ResetTickerState();
        Animate(host, OpacityProperty, null, 0, Ms(250), EaseIn, () => Children.Remove(host));
    }

    void ResetTickerState()
    {
        _tickerHost = null;
        _tickerPanel = null;
        _tickerItems.Clear();
        _tickerLineOfItem.Clear();
        _tickerKaraoke = null;
        _tickerCurrent = -1;
    }

    // ------------------------------------------------------------------ helpers

    TimeSpan Ms(double ms) => TimeSpan.FromMilliseconds(ms / _speed);

    static void Animate(IAnimatable target, DependencyProperty property, double? from, double to,
        TimeSpan duration, IEasingFunction ease, Action? done = null, TimeSpan? delay = null)
    {
        var a = new DoubleAnimation { To = to, Duration = duration, EasingFunction = ease };
        if (from.HasValue) a.From = from.Value;
        if (delay.HasValue) a.BeginTime = delay.Value;
        if (done != null) a.Completed += (_, _) => done();
        target.BeginAnimation(property, a);
    }

    static T Frozen<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
