using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Views;

/// <summary>
/// Lyrics for a vertical taskbar: the current line as a column, one word per row (one
/// character per row for CJK, kana and hangul), centered across the narrow strip. Same
/// timeline rules, karaoke colors and transition effects as <see cref="LyricsPresenter"/>,
/// turned to run along the height. A column taller than the strip scrolls to keep the
/// word being sung in view. Driven every frame by <see cref="Update"/>.
/// </summary>
public sealed class WordStackPresenter : Grid, ILyricsView
{
    sealed class Karaoke
    {
        public required LinearGradientBrush Brush;
        public required GradientStop Sung;
        public required GradientStop SungEdge;
        public required GradientStop UnsungEdge;
    }

    sealed class Word
    {
        public required TextBlock Block;
        public required double Size;    // as configured; Place may draw it smaller to fit
        public double Before;           // space above it in the column
        public bool Optional;           // the next line's preview, shown only while it fits
        public double Units = 1;        // letters: its share of the line's sung time
        public double From;             // letters sung before it starts
        public double Y, Left, Width, Height;  // Y from the top of the column
    }

    sealed class Slot
    {
        public readonly Grid Root = new();
        public readonly TranslateTransform Translate = new();
        public readonly ScaleTransform Scale = new();
        public readonly Canvas Column = new();
        public readonly TranslateTransform Shift = new();
        public readonly List<Word> Words = [];
        public int MainCount;           // words of the current line; the rest preview the next one
        public double TotalUnits = 1;
        public LyricLine? Line;
        public TimeSpan SungFor;
        public Karaoke? Karaoke;        // one gradient, moved to whichever word is being sung
        public int Active = -1;
        public bool IsMessage;
        public StackPanel? DotsPanel;
        public Ellipse[]? Dots;
        public ScaleTransform? DotsScale;
        public TimeSpan GapStart, GapEnd;
        public double Rest;             // top of the column when not scrolled
        public double MainHeight, Overflow, Scroll;
        public bool Laid, Following;
        public DateTime Born = DateTime.Now;
    }

    enum Kind { Line, Gap, Empty }

    readonly record struct Target(string Key, Kind Kind, int Index, TimeSpan GapStart, TimeSpan GapEnd);

    const double Soft = 0.08;
    const double Pad = 4;               // between the column and the strip's ends
    const double MinFit = 0.55;         // the smallest a long word shrinks to, as a share of its size
    const double FocusAt = 0.4;         // where the word being sung sits while the column scrolls
    const double ScrollLag = 0.22;      // seconds for the scroll to close ~63% of the way to its target
    static readonly TimeSpan IntroGap = TimeSpan.FromSeconds(3);
    static readonly TimeSpan BlankGap = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan HoldGrace = TimeSpan.FromSeconds(2.5);
    static readonly TimeSpan HoldGap = TimeSpan.FromSeconds(5);
    static readonly Size Unbounded = new(double.PositiveInfinity, double.PositiveInfinity);

    static readonly IEasingFunction EaseOut = Frozen(new QuarticEase { EasingMode = EasingMode.EaseOut });
    static readonly IEasingFunction EaseIn = Frozen(new QuadraticEase { EasingMode = EasingMode.EaseIn });
    static readonly IEasingFunction EaseInOut = Frozen(new CubicEase { EasingMode = EasingMode.EaseInOut });
    static readonly IEasingFunction Bounce = Frozen(new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.45 });

    // Text in the user's color, as LyricsPresenter draws it. Rebuilt by SetTextStyle (and Accent).
    Color _textColor = Colors.White;
    double _textOpacity = 0.96;
    Color UnsungColor => ColorUtil.WithAlpha(_textColor, _textOpacity * 0.55);
    Color SungColor => ColorUtil.WithAlpha(ColorUtil.Lerp(_textColor, _accent, 0.3), _textOpacity);
    Brush _mainBrush;
    Brush _dimBrush;
    Brush _subBrush;
    Brush _sungBrush;
    Brush _unsungBrush;

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
    DateTime _lastFrame;

    public WordStackPresenter()
    {
        ClipToBounds = true;
        MakeBrushes();
        SizeChanged += (_, _) => Relayout();
    }

    public bool HasLyrics => _lyrics != null;

    /// <summary>
    /// Where lyric text is drawn right now, in this element's coordinates (the current
    /// column, the next line under it, and columns still animating out), or null.
    /// </summary>
    public Rect? TextBounds()
    {
        if (ActualWidth < 1 || !IsVisible) return null;

        Rect? all = null;
        void Add(Slot slot)
        {
            if (slot.Root.Opacity < 0.05 || !ReferenceEquals(VisualTreeHelper.GetParent(slot.Root), this)) return;
            var drawn = Rect.Empty;
            Visual space = slot.Column;
            if (slot.DotsPanel is { } dots)
            {
                drawn = new Rect(dots.RenderSize);
                space = dots;
            }
            else if (slot.Laid)
            {
                foreach (var word in slot.Words)
                    if (word.Block.Visibility == Visibility.Visible)
                        drawn.Union(new Rect(word.Left, slot.Rest + word.Y, word.Width, word.Height));
            }
            if (drawn.IsEmpty || drawn.Height < 1) return;
            var r = space.TransformToAncestor(this).TransformBounds(drawn);
            all = all is { } a ? Rect.Union(a, r) : r;
        }
        if (_current != null) Add(_current);
        foreach (var slot in _exiting) Add(slot);
        if (all is not { } bounds) return null;
        bounds.Intersect(new Rect(RenderSize));
        return bounds.IsEmpty ? null : bounds;
    }

    /// <summary>The vertical span of drawn text on screen (physical pixels), or (0, 0).</summary>
    public (double Start, double End) TextScreenSpan()
    {
        if (TextBounds() is not { } text) return (0, 0);
        return (PointToScreen(new Point(0, text.Top)).Y, PointToScreen(new Point(0, text.Bottom)).Y);
    }

    public Color Accent
    {
        get => _accent;
        set
        {
            if (_accent == value) return;
            _accent = value;
            var sung = SungColor;
            _sungBrush = Frozen(new SolidColorBrush(sung));
            if (_current?.Karaoke is { } k)
            {
                k.Sung.Color = k.SungEdge.Color = sung;
                for (int i = 0; i < Math.Min(_current.Active, _current.MainCount); i++)
                    _current.Words[i].Block.Foreground = _sungBrush;
            }
        }
    }

    // ------------------------------------------------------------------ public API

    /// <summary>The lyric text color and how solid it is; redraws the current column when it changes.</summary>
    public void SetTextStyle(Color color, double opacity)
    {
        if (color == _textColor && Math.Abs(opacity - _textOpacity) < 0.005) return;
        _textColor = color;
        _textOpacity = opacity;
        MakeBrushes();
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
        _lastIndex = -1;
        _currentKey = "M";
        Transition(CreateMessageSlot(), forward: true);
    }

    public void ShowLyrics(SyncedLyrics lyrics)
    {
        _lyrics = lyrics;
        _currentKey = null;
        _lastIndex = -1;
    }

    /// <summary>Replays the entrance of what is on screen (used by "preview" in settings).</summary>
    public void Preview()
    {
        if (_lyrics != null)
        {
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
        var now = DateTime.Now;
        double dt = Math.Clamp((now - _lastFrame).TotalSeconds, 0, 0.25);
        _lastFrame = now;
        _pos = position;
        _playing = playing;
        if (_lyrics != null)
        {
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
        if (_current != null) UpdateSlot(_current, position, dt);
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
            var holdEnd = line.Start + Cap(i) + HoldGrace;
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

    /// <summary>
    /// How long line <paramref name="i"/> is actually sung: until its end (the next line's start
    /// when the end is not usable), at most its syllables' worth.
    /// </summary>
    TimeSpan Cap(int i)
    {
        var lines = _lyrics!.Lines;
        var l = lines[i];
        if (l.End > l.Start) return l.Sung;
        var end = i + 1 < lines.Count ? lines[i + 1].Start : l.Start + TimeSpan.FromSeconds(6);
        double natural = (end - l.Start).TotalSeconds * 0.95;
        return TimeSpan.FromSeconds(Math.Max(0.4, Math.Min(natural, l.Sung.TotalSeconds)));
    }

    // ------------------------------------------------------------------ slot construction

    double RowGap => Math.Max(1, Math.Round(_mainSize * 0.1));
    double PhraseGap => Math.Round(_mainSize * 0.4);
    double LineGap => Math.Round(_mainSize * 0.8);

    Slot NewSlot()
    {
        var s = new Slot();
        var group = new TransformGroup();
        group.Children.Add(s.Scale);
        group.Children.Add(s.Translate);
        s.Root.RenderTransform = group;
        s.Root.RenderTransformOrigin = new Point(0.5, 0.5);
        s.Column.RenderTransform = s.Shift;
        s.Root.Children.Add(s.Column);
        return s;
    }

    /// <summary>Adds a row per word of <paramref name="text"/>, after a line gap when the column already has words.</summary>
    void AddWords(Slot s, string text, double size, FontWeight weight, Brush brush, bool optional)
    {
        bool first = true;
        foreach (var (row, phrase) in Split(text))
        {
            var tb = new TextBlock
            {
                Text = row,
                FontFamily = _font,
                FontSize = size,
                FontWeight = weight,
                TextWrapping = TextWrapping.NoWrap,
                Foreground = brush,
            };
            s.Column.Children.Add(tb);
            s.Words.Add(new Word
            {
                Block = tb,
                Size = size,
                Optional = optional,
                Before = first && s.Words.Count > 0 ? LineGap : RowGap + (phrase ? PhraseGap : 0),
                Units = Letters(row),
            });
            first = false;
        }
    }

    Slot CreateLineSlot(int index)
    {
        var lines = _lyrics!.Lines;
        var line = lines[index];
        var slot = NewSlot();
        slot.Line = line;
        slot.SungFor = Cap(index);
        AddWords(slot, line.Text, _mainSize, FontWeights.SemiBold, _karaoke ? _unsungBrush : _mainBrush, optional: false);
        slot.MainCount = slot.Words.Count;

        // No word timings exist: each word gets a share of the line in proportion to its letters.
        double units = 0;
        for (int k = 0; k < slot.MainCount; k++)
        {
            slot.Words[k].From = units;
            units += slot.Words[k].Units;
        }
        slot.TotalUnits = Math.Max(1, units);
        if (_karaoke && slot.MainCount > 0) slot.Karaoke = CreateKaraoke();

        if (_twoLine)
        {
            int ni = NextNonBlank(index + 1);
            if (ni >= 0) AddWords(slot, lines[ni].Text, _subSize, FontWeights.Normal, _dimBrush, optional: true);
        }
        return slot;
    }

    Karaoke CreateKaraoke()
    {
        var sung = SungColor;
        var unsung = UnsungColor;
        var k = new Karaoke
        {
            Brush = new LinearGradientBrush { StartPoint = new Point(0, 0.5), EndPoint = new Point(1, 0.5) },
            Sung = new GradientStop(sung, 0),
            SungEdge = new GradientStop(sung, -Soft),
            UnsungEdge = new GradientStop(unsung, 0),
        };
        k.Brush.GradientStops.Add(k.Sung);
        k.Brush.GradientStops.Add(k.SungEdge);
        k.Brush.GradientStops.Add(k.UnsungEdge);
        k.Brush.GradientStops.Add(new GradientStop(unsung, 1));
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
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = Along(_align),
            Margin = new Thickness(0, d, 0, d),
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
                Margin = new Thickness(0, k == 0 ? 0 : d * 0.85, 0, 0),
            };
            slot.Dots[k] = dot;
            panel.Children.Add(dot);
        }
        slot.Root.Children.Add(panel);
        slot.DotsPanel = panel;
        slot.Root.RenderTransformOrigin = new Point(0.5, _align switch
        {
            HorizontalAlignment.Left => 0,
            HorizontalAlignment.Right => 1,
            _ => 0.5,
        });
        return slot;
    }

    Slot CreateMessageSlot()
    {
        var slot = NewSlot();
        slot.IsMessage = true;
        AddWords(slot, _msgPrimary, _mainSize, FontWeights.SemiBold, _mainBrush, optional: false);
        AddWords(slot, _msgSecondary, _subSize, FontWeights.Normal, _subBrush, optional: false);
        return slot;
    }

    // ------------------------------------------------------------------ layout

    void Relayout()
    {
        if (_current != null) Place(_current);
        foreach (var s in _exiting) Place(s);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        Relayout();
    }

    /// <summary>
    /// Fits each word to the strip's width, stacks the rows and places the column along the
    /// height by the alignment. Rows of the next line that do not fit are hidden; a column that
    /// still does not fit starts at the top and scrolls.
    /// </summary>
    void Place(Slot s)
    {
        double w = ActualWidth, h = ActualHeight;
        if (s.Words.Count == 0 || w < 1 || h < 1) return;
        double dip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        double room = Math.Max(1, w - 4), limit = h - 2 * Pad;
        double y = 0;
        bool any = false, fits = true;
        for (int k = 0; k < s.Words.Count; k++)
        {
            var word = s.Words[k];
            Fit(word, room, dip);
            double top = any ? y + word.Before : 0;
            if (word.Optional && fits && top + word.Height > limit) fits = false;
            bool show = !word.Optional || fits;
            word.Block.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (!show) continue;
            word.Y = top;
            y = top + word.Height;
            any = true;
            if (k == s.MainCount - 1) s.MainHeight = y;
        }

        s.Overflow = Math.Max(0, y - limit);
        s.Rest = s.Overflow > 0 ? Pad : _align switch
        {
            HorizontalAlignment.Left => Pad,
            HorizontalAlignment.Right => h - Pad - y,
            _ => (h - y) / 2,
        };
        foreach (var word in s.Words)
        {
            word.Left = (w - word.Width) / 2;
            Canvas.SetLeft(word.Block, word.Left);
            Canvas.SetTop(word.Block, s.Rest + word.Y);
        }
        SetScroll(s, Math.Clamp(s.Scroll, 0, s.Overflow));
        // Zoom and the like scale about the middle of the column, not of the strip.
        double mid = s.Overflow > 0 ? h / 2 : s.Rest + y / 2;
        s.Root.RenderTransformOrigin = new Point(0.5, Math.Clamp(mid / h, 0, 1));
        s.Laid = true;
    }

    /// <summary>Shrinks a word wider than the strip until it fits, down to MinFit of its size; past that it clips.</summary>
    void Fit(Word word, double room, double dip)
    {
        var tb = word.Block;
        var face = new Typeface(_font, FontStyles.Normal, tb.FontWeight, FontStretches.Normal);
        double width = new FormattedText(tb.Text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            face, word.Size, Brushes.White, dip).WidthIncludingTrailingWhitespace;
        double size = width > room ? Math.Max(word.Size * MinFit, word.Size * room / width) : word.Size;
        if (Math.Abs(tb.FontSize - size) > 0.01) tb.FontSize = size;
        tb.Measure(Unbounded);
        word.Width = tb.DesiredSize.Width;
        word.Height = tb.DesiredSize.Height;
    }

    static VerticalAlignment Along(HorizontalAlignment align) => align switch
    {
        HorizontalAlignment.Left => VerticalAlignment.Top,
        HorizontalAlignment.Right => VerticalAlignment.Bottom,
        _ => VerticalAlignment.Center,
    };

    // ------------------------------------------------------------------ transitions

    /// <summary>How far a column slides on a line change: a stretch of the strip, not all of it.</summary>
    double Travel => Math.Clamp(ActualHeight * 0.3, 16, Math.Max(16, _mainSize * 6));

    void Transition(Slot next, bool forward)
    {
        foreach (var s in _exiting) Children.Remove(s.Root);
        _exiting.Clear();

        var old = _current;
        _current = next;
        Children.Add(next.Root);
        Place(next);
        if (old != null) _exiting.Add(old);

        // The column runs along the taskbar, so each effect plays along the height. The ticker
        // has no strip to run along here and slides; per-word effects need a lyric line.
        var effect = _instant ? LyricEffect.None : _effect;
        if (effect == LyricEffect.Ticker) effect = LyricEffect.SlideUp;
        if (effect == LyricEffect.Words && next.Line == null) effect = LyricEffect.SlideUp;
        if (effect == LyricEffect.Typewriter && next.Line == null) effect = LyricEffect.Fade;

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
                double dy = Travel * dir;
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
                // Rows rise into place one after another, top to bottom; long columns stagger faster.
                double step = Math.Min(55, 600.0 / Math.Max(1, next.Words.Count)) / _speed;
                for (int k = 0; k < next.Words.Count; k++)
                {
                    var word = next.Words[k].Block;
                    var shift = new TranslateTransform();
                    word.RenderTransform = shift;
                    var delay = TimeSpan.FromMilliseconds(k * step);
                    word.Opacity = 0;
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
            {
                // Rows appear in reading order at the pace the horizontal typewriter types letters;
                // the next line's preview follows once the line is typed.
                double total = Math.Clamp(next.TotalUnits * 0.03, 0.25, 1.0) / _speed;
                for (int k = 0; k < next.Words.Count; k++)
                {
                    var word = next.Words[k];
                    bool main = k < next.MainCount;
                    var at = TimeSpan.FromSeconds(main ? total * word.From / next.TotalUnits : total);
                    word.Block.Opacity = 0;
                    Animate(word.Block, OpacityProperty, 0, 1, main ? Ms(60) : d * 0.6, EaseOut, delay: at);
                }
                if (old != null)
                {
                    Animate(old.Translate, TranslateTransform.YProperty, null, -6 * dir, d * 0.4, EaseIn);
                    Animate(old.Root, OpacityProperty, null, 0, d * 0.4, EaseIn, () => Remove(old));
                }
                break;
            }
        }
    }

    /// <summary>A soft-edged wipe down the column (up it, going back), with a slight drift.</summary>
    void Swipe(Slot next, Slot? old, TimeSpan d, bool forward)
    {
        const double soft = 0.18;
        var start = forward ? new Point(0.5, 0) : new Point(0.5, 1);
        var end = forward ? new Point(0.5, 1) : new Point(0.5, 0);
        double dy = forward ? 18 : -18;

        var inA = new GradientStop(Colors.Black, -soft);
        var inB = new GradientStop(Colors.Transparent, 0);
        var inMask = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        inMask.GradientStops.Add(inA);
        inMask.GradientStops.Add(inB);
        next.Root.OpacityMask = inMask;
        Animate(inA, GradientStop.OffsetProperty, -soft, 1, d, EaseInOut);
        Animate(inB, GradientStop.OffsetProperty, 0, 1 + soft, d, EaseInOut, () => next.Root.OpacityMask = null);
        Animate(next.Translate, TranslateTransform.YProperty, dy, 0, d, EaseOut);

        if (old == null) return;
        var outA = new GradientStop(Colors.Transparent, -soft);
        var outB = new GradientStop(Colors.Black, 0);
        var outMask = new LinearGradientBrush { StartPoint = start, EndPoint = end };
        outMask.GradientStops.Add(outA);
        outMask.GradientStops.Add(outB);
        old.Root.OpacityMask = outMask;
        Animate(outA, GradientStop.OffsetProperty, -soft, 1, d, EaseInOut);
        Animate(outB, GradientStop.OffsetProperty, 0, 1 + soft, d, EaseInOut, () => Remove(old));
        Animate(old.Translate, TranslateTransform.YProperty, null, -dy, d, EaseOut);
    }

    void Remove(Slot slot)
    {
        Children.Remove(slot.Root);
        _exiting.Remove(slot);
    }

    void Rebuild()
    {
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

    void UpdateSlot(Slot s, TimeSpan pos, double dt)
    {
        if (s.Line != null)
        {
            double p = s.Line.WordProgress(pos) ?? Math.Clamp((pos - s.Line.Start).TotalSeconds / s.SungFor.TotalSeconds, 0, 1);
            double focus = s.Karaoke != null ? Sing(s, p) : s.MainHeight * p;
            if (s.Overflow > 0.5) Follow(s, focus, dt);
        }
        else if (s.Dots != null)
        {
            UpdateDots(s, pos);
        }
        else if (s.IsMessage && _playing && s.Overflow > 0.5)
        {
            // Ping-pong down the column and back, with a pause at each end.
            const double hold = 2.2;
            double travel = Math.Max(1, s.Overflow / 38.0);
            double cycle = 2 * (travel + hold);
            double t = (DateTime.Now - s.Born).TotalSeconds % cycle;
            double x = t < hold ? 0
                : t < hold + travel ? (t - hold) / travel
                : t < 2 * hold + travel ? 1
                : 1 - (t - 2 * hold - travel) / travel;
            SetScroll(s, s.Overflow * Smooth(x));
        }
    }

    /// <summary>
    /// Colors the line's words by how far it is sung: sung words solid, the word being sung
    /// filling left to right, the rest unsung. Returns the point being sung, down the column.
    /// </summary>
    double Sing(Slot s, double p)
    {
        double sung = p * s.TotalUnits;
        int active = 0;
        while (active < s.MainCount && sung >= s.Words[active].From + s.Words[active].Units) active++;
        if (active != s.Active)
        {
            s.Active = active;
            for (int k = 0; k < s.MainCount; k++)
                s.Words[k].Block.Foreground = k < active ? _sungBrush : k == active ? s.Karaoke!.Brush : _unsungBrush;
        }
        if (active >= s.MainCount) return s.MainHeight;
        var word = s.Words[active];
        double fill = Math.Clamp((sung - word.From) / word.Units, 0, 1);
        SetKaraoke(s.Karaoke!, fill);
        return word.Y + word.Height * fill;
    }

    /// <summary>Eases the scroll toward holding <paramref name="focus"/> (down the column) at FocusAt of the height.</summary>
    void Follow(Slot s, double focus, double dt)
    {
        double target = Math.Clamp(s.Rest + focus - ActualHeight * FocusAt, 0, s.Overflow);
        double scroll = s.Following ? s.Scroll + (target - s.Scroll) * (1 - Math.Exp(-dt * _speed / ScrollLag)) : target;
        s.Following = true;
        SetScroll(s, scroll);
    }

    static void SetScroll(Slot s, double scroll)
    {
        s.Scroll = scroll;
        if (Math.Abs(s.Shift.Y + scroll) > 0.01) s.Shift.Y = -scroll;
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

    // ------------------------------------------------------------------ splitting

    /// <summary>
    /// The rows a line is drawn in: a row per word, and a row per character for CJK, kana and
    /// hangul written without spaces. Punctuation stays on its word's row. Phrase marks a row
    /// that starts a new space-separated run next to a run cut into characters, so the column
    /// leaves a little more room there.
    /// </summary>
    static List<(string Text, bool Phrase)> Split(string text)
    {
        var rows = new List<(string Text, bool Phrase)>();
        string lead = "";
        bool lastCut = false;
        foreach (var token in text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (!HasLetter(token))
            {
                // A lone dash or ellipsis rides on the word before it (the word after, at the start).
                if (rows.Count > 0) rows[^1] = (rows[^1].Text + token, rows[^1].Phrase);
                else lead += token;
                continue;
            }
            bool cut = HasWide(token);
            int first = rows.Count;
            if (cut) SplitWide(token, rows);
            else rows.Add((token, false));
            rows[first] = (lead + rows[first].Text, first > 0 && (cut || lastCut));
            lead = "";
            lastCut = cut;
        }
        if (lead.Length > 0) rows.Add((lead, false));
        return rows;
    }

    /// <summary>Cuts a CJK run into characters; a word in another script inside it stays whole.</summary>
    static void SplitWide(string token, List<(string Text, bool Phrase)> rows)
    {
        int start = rows.Count;
        var unit = new StringBuilder();
        bool opening = false, latin = false;
        var e = StringInfo.GetTextElementEnumerator(token);
        while (e.MoveNext())
        {
            int i = e.ElementIndex;
            string g = e.GetTextElement();
            bool wide = Rune.TryGetRuneAt(token, i, out var r) && IsWide(r.Value);
            if (wide || char.IsLetterOrDigit(token, i))
            {
                // Opening brackets and quotes wait for the character they open.
                if (unit.Length > 0 && !opening && (wide || !latin)) Flush();
                latin = !wide;
                opening = false;
                unit.Append(g);
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(token, i) is UnicodeCategory.OpenPunctuation or UnicodeCategory.InitialQuotePunctuation)
            {
                if (unit.Length > 0 && !opening) Flush();
                opening = true;
                unit.Append(g);
            }
            else
            {
                unit.Append(g); // closing punctuation stays with what it follows
            }
        }
        if (unit.Length == 0) return;
        if (opening && rows.Count > start) rows[^1] = (rows[^1].Text + unit.ToString(), false);
        else rows.Add((unit.ToString(), false));

        void Flush()
        {
            rows.Add((unit.ToString(), false));
            unit.Clear();
        }
    }

    static bool HasLetter(string s)
    {
        for (int i = 0; i < s.Length; i++)
            if (char.IsLetterOrDigit(s, i)) return true;
        return false;
    }

    static bool HasWide(string s)
    {
        foreach (var r in s.EnumerateRunes())
            if (IsWide(r.Value)) return true;
        return false;
    }

    /// <summary>CJK ideographs, kana, bopomofo and hangul: scripts written without spaces between words.</summary>
    static bool IsWide(int c) => c is >= 0x1100 and <= 0x11FF   // Hangul Jamo
        or >= 0x2E80 and <= 0x2FDF                               // CJK and Kangxi radicals
        or >= 0x3040 and <= 0x312F                               // Hiragana, Katakana, Bopomofo
        or >= 0x3130 and <= 0x318F                               // Hangul compatibility Jamo
        or >= 0x31F0 and <= 0x31FF                               // Katakana extensions
        or >= 0x3400 and <= 0x4DBF                               // CJK extension A
        or >= 0x4E00 and <= 0x9FFF                               // CJK unified ideographs
        or >= 0xA960 and <= 0xA97F                               // Hangul Jamo extended A
        or >= 0xAC00 and <= 0xD7FF                               // Hangul syllables, Jamo extended B
        or >= 0xF900 and <= 0xFAFF                               // CJK compatibility ideographs
        or >= 0xFF66 and <= 0xFFDC                               // halfwidth katakana and hangul
        or >= 0x20000 and <= 0x3FFFF;                            // CJK extensions B onward

    /// <summary>Letters and digits in a row, a character with its marks counted once; at least 1.</summary>
    static int Letters(string s)
    {
        int n = 0;
        var e = StringInfo.GetTextElementEnumerator(s);
        while (e.MoveNext())
            if (char.IsLetterOrDigit(s, e.ElementIndex)) n++;
        return Math.Max(1, n);
    }

    // ------------------------------------------------------------------ helpers

    [MemberNotNull(nameof(_mainBrush), nameof(_dimBrush), nameof(_subBrush), nameof(_sungBrush), nameof(_unsungBrush))]
    void MakeBrushes()
    {
        _mainBrush = TextBrush(_textColor, _textOpacity);
        _dimBrush = TextBrush(_textColor, _textOpacity * 0.65);
        _subBrush = TextBrush(_textColor, _textOpacity * 0.46);
        _sungBrush = Frozen(new SolidColorBrush(SungColor));
        _unsungBrush = Frozen(new SolidColorBrush(UnsungColor));
    }

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
