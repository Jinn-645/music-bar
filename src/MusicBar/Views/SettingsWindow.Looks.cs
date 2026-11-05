using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>
/// The progress page's looks: the progress styles, the visualizers and the skins as live tiles,
/// and the colors of the look that's on (the song's, a preset, or three picked by hand).
/// </summary>
public partial class SettingsWindow
{
    /// <summary>A tile in one of the galleries: a style (Skin null) or a skin, its live preview and its frame.</summary>
    sealed record Look(SeekStyle Style, SkinInfo? Skin, Border Tile, SeekRenderer Engine, SkinView? Shader)
    {
        public string Key => Skin != null ? StylePalettes.KeyForSkin(Skin.File) : StylePalettes.KeyFor(Style);
    }

    readonly List<Look> _looks = [];
    readonly List<(string Value, Border Chip)> _paletteChips = [];
    Color _songA = ColorUtil.Brand, _songB = ColorUtil.Brand;

    void BuildLooks()
    {
        var (primary, secondary, art) = Overlay?.PreviewPalette() ?? DemoPalette();
        (_songA, _songB) = (primary, secondary);
        foreach (var option in SettingsOptions.SeekStyles) SeekGallery.Children.Add(StyleTile(option, art));
        foreach (var option in SettingsOptions.Visualizers) VisualizerGallery.Children.Add(StyleTile(option, art));
        BuildSkinTiles();
        BuildPaletteChips();
        RefreshLooks();
    }

    Border StyleTile(SeekStyleOption option, BitmapSource? art)
    {
        var engine = new SeekRenderer { Look = option.Value, ClipToBounds = true, Intensity = _s.FillOpacity, Direction = _s.SeekDirection };
        engine.SetEdge(true);
        engine.SetArt(art);
        engine.SetTrack(option.Label);
        var value = option.Value;
        var tile = Tile(engine, option.Label, option.Description, () =>
        {
            _s.SeekStyle = value;
            _s.Skin = "";
        });
        _looks.Add(new Look(value, null, tile, engine, null));
        return tile;
    }

    /// <summary>The skins in the folder, each running live; one that didn't compile says why.</summary>
    void BuildSkinTiles()
    {
        foreach (var look in _looks.Where(l => l.Skin != null).ToList()) _looks.Remove(look);
        SkinGallery.Children.Clear();
        foreach (var skin in SkinLibrary.List())
        {
            // The engine keeps time and makes up music for the preview; the skin paints over it.
            var engine = new SeekRenderer { Look = skin.Visualizer ? SeekStyle.Equalizer : SeekStyle.Classic, Skinned = true, Visibility = Visibility.Hidden };
            engine.SetEdge(true);
            var shader = new SkinView();
            shader.SetSkin(skin.Works ? skin : null);
            var stage = new Grid();
            stage.Children.Add(engine);
            stage.Children.Add(shader);
            var file = skin.File;
            string description = skin.Works
                ? (skin.Description.Length > 0 ? skin.Description : Loc.T(skin.Visualizer ? "Set_Skin_KindVisualizer" : "Set_Skin_KindProgress"))
                  + (skin.Author.Length > 0 ? " · " + skin.Author : "")
                : Loc.T("Set_Skin_Broken", skin.Error ?? "");
            var tile = Tile(stage, skin.Name, description, () =>
            {
                if (skin.Works) _s.Skin = file;
            });
            AddDeleteButton(tile, skin);
            if (!skin.Works) tile.Opacity = 0.75;
            _looks.Add(new Look(SeekStyle.Classic, skin, tile, engine, shader));
            SkinGallery.Children.Add(tile);
        }
        if (SkinGallery.Children.Count == 0)
            SkinGallery.Children.Add(new TextBlock { Text = Loc.T("Set_Skin_None"), FontSize = 12, Opacity = 0.55, Margin = new Thickness(2, 0, 0, 8) });
    }

    /// <summary>A gallery tile: a dark strip with the live preview, a name and a line about it.</summary>
    Border Tile(UIElement preview, string name, string description, Action choose)
    {
        var screen = new Border
        {
            Height = 34,
            Margin = new Thickness(8, 8, 8, 0),
            Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F)),
            Child = preview,
            ClipToBounds = true,
        };
        screen.SizeChanged += (_, _) => screen.Clip = new RectangleGeometry(new Rect(screen.RenderSize), 6, 6);
        var text = new StackPanel { Margin = new Thickness(12, 8, 12, 10) };
        text.Children.Add(new TextBlock { Text = name, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock { Text = description, FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
        var body = new StackPanel();
        body.Children.Add(screen);
        body.Children.Add(text);
        var tile = new Border
        {
            Width = 284,
            Margin = new Thickness(0, 0, 10, 10),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255)),
            Cursor = Cursors.Hand,
            Child = body,
        };
        tile.MouseLeftButtonUp += (_, _) => choose();
        tile.MouseEnter += (_, _) => tile.Background = new SolidColorBrush(Color.FromArgb(0x18, 255, 255, 255));
        tile.MouseLeave += (_, _) => tile.Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255));
        return tile;
    }

    void AddDeleteButton(Border tile, SkinInfo skin)
    {
        var remove = new Button
        {
            Content = "",
            FontFamily = (FontFamily)FindResource("IconFont"),
            FontSize = 11,
            Padding = new Thickness(6, 3, 6, 3),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 12, 12, 0),
            ToolTip = Loc.T("Set_Skin_Delete"),
            Opacity = 0.85,
        };
        remove.Click += (_, e) =>
        {
            e.Handled = true;
            var answer = MessageBox.Show(this, Loc.T("Set_Skin_DeleteAsk", skin.Name), Loc.T("Set_Skin_Delete"), MessageBoxButton.OKCancel, MessageBoxImage.Question);
            if (answer != MessageBoxResult.OK) return;
            if (_s.Skin == skin.File) _s.Skin = "";
            StylePalettes.Set(_s, StylePalettes.KeyForSkin(skin.File), "");
            SkinLibrary.Delete(skin.File);
            BuildSkinTiles();
            RefreshLooks();
        };
        var body = (StackPanel)tile.Child;
        tile.Child = null;
        var layered = new Grid();
        layered.Children.Add(body);
        layered.Children.Add(remove);
        tile.Child = layered;
    }

    /// <summary>The frame round the look that's on, each preview in its own colors, and the color picker for the look that's on.</summary>
    void RefreshLooks()
    {
        var accent = (Brush)FindResource("Accent");
        var idle = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        foreach (var look in _looks)
        {
            bool on = look.Skin != null ? _s.Skin == look.Skin.File : _s.Skin.Length == 0 && _s.SeekStyle == look.Style;
            look.Tile.BorderBrush = on ? accent : idle;
            if (StylePalettes.For(_s, look.Key) is { } own) look.Engine.SetColors(own.A, own.B, animate: false, own.C);
            else look.Engine.SetColors(_songA, _songB, animate: false);
        }
        RefreshPalettePicker();
    }

    /// <summary>Keeps the previews moving while the page is on screen, with made-up music.</summary>
    void TickLooks(double progress)
    {
        foreach (var look in _looks)
        {
            look.Engine.SetState(progress, timeline: true, playing: true);
            look.Engine.Tick();
            look.Shader?.Update(look.Engine);
        }
    }

    // ------------------------------------------------------------------ colors of the look that's on

    void BuildPaletteChips()
    {
        PaletteChips.Children.Clear();
        _paletteChips.Clear();
        AddChip("", Loc.T("Pal_Song"), null);
        foreach (var preset in StylePalettes.Presets) AddChip("preset:" + preset.Id, Loc.T(preset.NameKey), preset.Colors);
        AddChip("custom", Loc.T("Pal_Custom"), null);
    }

    void AddChip(string value, string name, Palette3? colors)
    {
        var swatch = new Border { Width = 46, Height = 16, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 4) };
        if (colors is { } c) swatch.Background = new LinearGradientBrush(new GradientStopCollection { new(c.B, 0), new(c.A, 0.5), new(c.C, 1) }, 0);
        else if (value == "custom") swatch.Background = new LinearGradientBrush(new GradientStopCollection
        {
            new(Color.FromRgb(0xFF, 0x5F, 0x6D), 0), new(Color.FromRgb(0xFF, 0xD1, 0x66), 0.33),
            new(Color.FromRgb(0x4C, 0xFF, 0xB0), 0.66), new(Color.FromRgb(0x47, 0xA9, 0xFF), 1),
        }, 0);
        var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
        body.Children.Add(swatch);
        body.Children.Add(new TextBlock { Text = name, FontSize = 11, Opacity = 0.75, HorizontalAlignment = HorizontalAlignment.Center });
        var chip = new Border
        {
            MinWidth = 66,
            Padding = new Thickness(8, 7, 8, 6),
            Margin = new Thickness(0, 0, 8, 8),
            CornerRadius = new CornerRadius(9),
            BorderThickness = new Thickness(1.5),
            Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255)),
            Cursor = Cursors.Hand,
            Child = body,
            Tag = swatch,
        };
        chip.MouseLeftButtonUp += (_, _) => ChoosePalette(value);
        _paletteChips.Add((value, chip));
        PaletteChips.Children.Add(chip);
    }

    void ChoosePalette(string value)
    {
        var key = StylePalettes.KeyFor(_s);
        if (value == "custom")
        {
            // Starts from what the look shows now, so the three boxes begin somewhere sensible.
            var now = StylePalettes.For(_s, key) ?? new Palette3(_songA, _songB, ColorUtil.ShiftHue(ColorUtil.Lerp(_songA, _songB, 0.5), 48, 0.04));
            StylePalettes.Set(_s, key, StylePalettes.Custom(now.A, now.B, now.C));
            return;
        }
        StylePalettes.Set(_s, key, value);
    }

    void RefreshPalettePicker()
    {
        string lookName = _s.Skin.Length > 0
            ? SkinLibrary.Load(_s.Skin)?.Name ?? _s.Skin
            : (SettingsOptions.SeekStyles.Concat(SettingsOptions.Visualizers).FirstOrDefault(o => o.Value == _s.SeekStyle)?.Label ?? _s.SeekStyle.ToString());
        PaletteFor.Text = Loc.T("Set_Palette_For", lookName);

        var saved = StylePalettes.Saved(_s, StylePalettes.KeyFor(_s));
        bool custom = saved.Length > 0 && !saved.StartsWith("preset:", StringComparison.Ordinal);
        var accent = (Brush)FindResource("Accent");
        var idle = new SolidColorBrush(Color.FromArgb(0x14, 255, 255, 255));
        foreach (var (value, chip) in _paletteChips)
        {
            bool on = custom ? value == "custom" : value == saved;
            chip.BorderBrush = on ? accent : idle;
            // The song's chip shows the song's colors as they are now.
            if (value.Length == 0 && chip.Tag is Border swatch)
                swatch.Background = new LinearGradientBrush(new GradientStopCollection { new(_songB, 0), new(_songA, 1) }, 0);
        }
        CustomPalette.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (custom && StylePalettes.Parse(saved) is { } p)
        {
            PaletteA.Text = ColorUtil.ToHex(p.A);
            PaletteB.Text = ColorUtil.ToHex(p.B);
            PaletteC.Text = ColorUtil.ToHex(p.C);
            PaletteA.Background = Swatch(p.A);
            PaletteB.Background = Swatch(p.B);
            PaletteC.Background = Swatch(p.C);
        }
    }

    /// <summary>A text box tinted with its own color, faintly, so the three read as a palette.</summary>
    static Brush Swatch(Color c) => new LinearGradientBrush(ColorUtil.WithAlpha(c, 0.55), ColorUtil.WithAlpha(c, 0.12), 0);

    void PaletteBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) SaveCustomPalette();
    }

    void PaletteBox_LostFocus(object sender, RoutedEventArgs e) => SaveCustomPalette();

    void SaveCustomPalette()
    {
        if (!ColorUtil.TryParse(PaletteA.Text, out var a) || !ColorUtil.TryParse(PaletteB.Text, out var b) || !ColorUtil.TryParse(PaletteC.Text, out var c))
        {
            RefreshPalettePicker();
            return;
        }
        StylePalettes.Set(_s, StylePalettes.KeyFor(_s), StylePalettes.Custom(a, b, c));
    }

    // ------------------------------------------------------------------ skin buttons

    void SkinImport_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = Loc.T("Set_Skin_Filter") + " (*.hlsl)|*.hlsl|*.*|*.*", Multiselect = true };
        if (dialog.ShowDialog(this) != true) return;
        var lines = new List<string>();
        SkinInfo? last = null;
        foreach (var path in dialog.FileNames)
        {
            try
            {
                var skin = SkinLibrary.Import(path);
                if (skin.Works) last = skin;
                lines.Add(skin.Works ? Loc.T("Set_Skin_Imported", skin.Name) : Loc.T("Set_Skin_ImportFailed", Path.GetFileName(path), skin.Error ?? ""));
            }
            catch (Exception ex)
            {
                Log.Error("Importing a skin failed", ex);
                lines.Add(Loc.T("Set_Skin_ImportFailed", Path.GetFileName(path), ex.Message));
            }
        }
        BuildSkinTiles();
        // One skin brought in and working: it goes on, which is what bringing it in was for.
        if (last != null && dialog.FileNames.Length == 1) _s.Skin = last.File;
        RefreshLooks();
        ShowSkinStatus(string.Join("\n", lines));
    }

    void SkinSamples_Click(object sender, RoutedEventArgs e)
    {
        int added = SkinLibrary.InstallSamples();
        BuildSkinTiles();
        RefreshLooks();
        ShowSkinStatus(Loc.T(added > 0 ? "Set_Skin_SamplesAdded" : "Set_Skin_SamplesThere", added));
    }

    void SkinFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(SkinLibrary.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{SkinLibrary.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Opening the skins folder failed", ex);
        }
    }

    void SkinGuide_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://github.com/lvt1693/music-bar/blob/main/docs/skins.md") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Opening the skins guide failed", ex);
        }
    }

    void ShowSkinStatus(string text)
    {
        SkinStatus.Text = text;
        SkinStatus.Visibility = text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
