using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// A skin's shader with its inputs, in the registers SkinLibrary.Prelude names. The audio comes in
/// as a small texture: the spectrum across its first row, the waveform across its second.
/// </summary>
public sealed partial class SkinEffect : ShaderEffect
{
    public static readonly DependencyProperty InputProperty = RegisterPixelShaderSamplerProperty("Input", typeof(SkinEffect), 0);
    public static readonly DependencyProperty AudioProperty = RegisterPixelShaderSamplerProperty("Audio", typeof(SkinEffect), 1, SamplingMode.Bilinear);
    public static readonly DependencyProperty TimeProperty = Constant("Time", 0.0, 0);
    public static readonly DependencyProperty ProgressProperty = Constant("Progress", 0.0, 1);
    public static readonly DependencyProperty LevelProperty = Constant("Level", 0.0, 2);
    public static readonly DependencyProperty PlayingProperty = Constant("Playing", 0.0, 3);
    public static readonly DependencyProperty SizeProperty = Constant("Size", new Point(1, 1), 4);
    public static readonly DependencyProperty ColorAProperty = Constant("ColorA", Colors.White, 5);
    public static readonly DependencyProperty ColorBProperty = Constant("ColorB", Colors.White, 6);
    public static readonly DependencyProperty ColorCProperty = Constant("ColorC", Colors.White, 7);
    public static readonly DependencyProperty BeatProperty = Constant("Beat", 0.0, 8);
    public static readonly DependencyProperty BottomEdgeProperty = Constant("BottomEdge", 1.0, 9);

    static DependencyProperty Constant(string name, object initial, int register)
        => DependencyProperty.Register(name, initial.GetType(), typeof(SkinEffect), new UIPropertyMetadata(initial, PixelShaderConstantCallback(register)));

    public SkinEffect(byte[] bytecode)
    {
        var shader = new PixelShader();
        shader.SetStreamSource(new MemoryStream(bytecode));
        PixelShader = shader;
        foreach (var p in new[] { InputProperty, AudioProperty, TimeProperty, ProgressProperty, LevelProperty, PlayingProperty, SizeProperty,
                     ColorAProperty, ColorBProperty, ColorCProperty, BeatProperty, BottomEdgeProperty })
            UpdateShaderValue(p);
    }
}

/// <summary>
/// Shows a skin: a rectangle whose pixels the skin's shader paints, fed each frame from a
/// <see cref="SeekRenderer"/> that keeps the clock, the audio and the colors (it runs hidden,
/// doing everything but drawing).
/// </summary>
public sealed class SkinView : Border
{
    readonly Rectangle _canvas = new() { Fill = new SolidColorBrush(Color.FromArgb(1, 0, 0, 0)) };
    readonly WriteableBitmap _audio = new(256, 2, 96, 96, PixelFormats.Bgra32, null);
    readonly byte[] _pixels = new byte[256 * 2 * 4];
    readonly ImageBrush _audioBrush;
    SkinEffect? _effect;

    public SkinView()
    {
        Child = _canvas;
        _audioBrush = new ImageBrush(_audio) { Stretch = Stretch.Fill };
        IsHitTestVisible = false;
    }

    public SkinInfo? Skin { get; private set; }

    /// <summary>Puts a skin on (or takes it off with null, or one that didn't compile).</summary>
    public void SetSkin(SkinInfo? skin)
    {
        if (ReferenceEquals(skin, Skin)) return;
        Skin = skin;
        _effect = skin?.Bytecode is { } code ? new SkinEffect(code) { Audio = _audioBrush } : null;
        _canvas.Effect = _effect;
    }

    /// <summary>One frame: the time, the song and the sound, as the engine has them now.</summary>
    public void Update(SeekRenderer engine)
    {
        if (_effect is not { } e) return;
        var (a, b, c) = engine.ColorsNow();
        e.Time = engine.StyleTime;
        e.Progress = engine.ProgressNow;
        e.Level = engine.LevelNow;
        e.Beat = engine.BeatNow;
        e.Playing = engine.PlayingNow ? 1 : 0;
        e.Size = new Point(Math.Max(1, ActualWidth), Math.Max(1, ActualHeight));
        e.ColorA = a;
        e.ColorB = b;
        e.ColorC = c;
        e.BottomEdge = engine.EdgeAtTop ? 1 : 0;

        // Row one, the spectrum (low notes on the left); row two, the waveform around the middle grey.
        var wave = engine.WaveNow;
        for (int x = 0; x < 256; x++)
        {
            byte level = (byte)Math.Round(Math.Clamp(engine.Band(x / 255.0), 0, 1) * 255);
            byte sample = (byte)Math.Round(Math.Clamp(wave[x * wave.Length / 256] * 0.5 + 0.5, 0, 1) * 255);
            Put(x, level);
            Put(256 + x, sample);
        }
        _audio.WritePixels(new Int32Rect(0, 0, 256, 2), _pixels, 256 * 4, 0);
    }

    void Put(int i, byte value)
    {
        int o = i * 4;
        _pixels[o] = _pixels[o + 1] = _pixels[o + 2] = value;
        _pixels[o + 3] = 255;
    }
}

public partial class SkinEffect
{
    public double Time { get => (double)GetValue(TimeProperty); set => SetValue(TimeProperty, value); }
    public double Progress { get => (double)GetValue(ProgressProperty); set => SetValue(ProgressProperty, value); }
    public double Level { get => (double)GetValue(LevelProperty); set => SetValue(LevelProperty, value); }
    public double Playing { get => (double)GetValue(PlayingProperty); set => SetValue(PlayingProperty, value); }
    public Point Size { get => (Point)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public Color ColorA { get => (Color)GetValue(ColorAProperty); set => SetValue(ColorAProperty, value); }
    public Color ColorB { get => (Color)GetValue(ColorBProperty); set => SetValue(ColorBProperty, value); }
    public Color ColorC { get => (Color)GetValue(ColorCProperty); set => SetValue(ColorCProperty, value); }
    public double Beat { get => (double)GetValue(BeatProperty); set => SetValue(BeatProperty, value); }
    public double BottomEdge { get => (double)GetValue(BottomEdgeProperty); set => SetValue(BottomEdgeProperty, value); }
    public Brush Audio { get => (Brush)GetValue(AudioProperty); set => SetValue(AudioProperty, value); }
}
