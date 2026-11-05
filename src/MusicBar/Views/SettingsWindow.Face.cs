using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MusicBar.Infrastructure;
using MusicBar.Services;

namespace MusicBar.Views;

/// <summary>The face of pictures: a tile for each mood, with its picture playing, to choose or remove.</summary>
public partial class SettingsWindow
{
    void BuildFaceStates()
    {
        FaceStates.Children.Clear();
        _facePlaying.Clear();
        var pack = new FacePack();
        foreach (var state in FacePack.States)
        {
            var file = FacePack.FileFor(state);
            var clip = file != null ? pack.For(state) : null;
            var picture = new Image { Width = 64, Height = 64, Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(picture, BitmapScalingMode.HighQuality);
            if (clip != null) Animate(picture, clip);
            var empty = new TextBlock
            {
                Text = Loc.T("Face_Pic_None"), FontSize = 11, Opacity = 0.45,
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                Visibility = clip == null ? Visibility.Visible : Visibility.Collapsed,
            };
            var stage = new Grid { Height = 72, Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x0B, 0x0F)), Margin = new Thickness(8, 8, 8, 0) };
            stage.Children.Add(picture);
            stage.Children.Add(empty);

            var name = new TextBlock { Text = Loc.T("Face_State_" + state), FontSize = 13, FontWeight = FontWeights.SemiBold };
            var about = new TextBlock { Text = Loc.T("Face_State_" + state + "_Desc"), FontSize = 11, Opacity = 0.6, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 6) };
            var choose = new Button { Content = Loc.T("Face_Pic_Choose"), Padding = new Thickness(10, 3, 10, 3), Margin = new Thickness(0, 0, 6, 0) };
            var remove = new Button { Content = Loc.T("Face_Pic_Remove"), Padding = new Thickness(10, 3, 10, 3), IsEnabled = file != null };
            var key = state;
            choose.Click += (_, _) => ChooseFacePicture(key);
            remove.Click += (_, _) =>
            {
                FacePack.Remove(key);
                FacePicturesChanged();
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            buttons.Children.Add(choose);
            buttons.Children.Add(remove);
            var text = new StackPanel { Margin = new Thickness(12, 8, 12, 10) };
            text.Children.Add(name);
            text.Children.Add(about);
            text.Children.Add(buttons);
            var body = new StackPanel();
            body.Children.Add(stage);
            body.Children.Add(text);
            FaceStates.Children.Add(new Border
            {
                Width = 186,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(9),
                BorderThickness = new Thickness(1.5),
                BorderBrush = new SolidColorBrush(Color.FromArgb(state == "idle" && file == null ? (byte)0x80 : (byte)0x14, 255, 255, 255)),
                Background = new SolidColorBrush(Color.FromArgb(0x0D, 255, 255, 255)),
                Child = body,
            });
        }
    }

    // The animated pictures in the mood tiles, played by the preview clock while the face page shows.
    readonly System.Collections.Generic.List<(Image Target, FacePack.Clip Clip)> _facePlaying = [];

    void Animate(Image target, FacePack.Clip clip)
    {
        target.Source = clip.Frames[0];
        if (clip.Frames.Length > 1) _facePlaying.Add((target, clip));
    }

    void TickFacePictures()
    {
        double t = _previewClock.Elapsed.TotalSeconds;
        foreach (var (target, clip) in _facePlaying)
        {
            var frame = clip.At(t);
            if (!ReferenceEquals(target.Source, frame)) target.Source = frame;
        }
    }

    void ChooseFacePicture(string state)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = Loc.T("Face_Pic_Filter") + " (*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp)|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.bmp",
        };
        if (dialog.ShowDialog(this) != true) return;
        string? problem;
        try
        {
            problem = FacePack.Import(state, dialog.FileName);
        }
        catch (Exception ex)
        {
            Log.Error("Taking a face picture failed", ex);
            problem = ex.Message;
        }
        FacePicStatus.Text = problem ?? "";
        FacePicStatus.Visibility = problem != null ? Visibility.Visible : Visibility.Collapsed;
        if (problem == null)
        {
            // A picture for the idle face is a face of pictures: worn at once.
            if (state == "idle") _s.FaceStyle = Face.FaceStyle.Custom;
            FacePicturesChanged();
        }
    }

    /// <summary>The pictures changed: the tiles, the gallery's preview and the face on the taskbar read them again.</summary>
    void FacePicturesChanged()
    {
        BuildFaceStates();
        foreach (var (value, _, preview) in _faceTiles.Where(t => t.Value == Face.FaceStyle.Custom)) preview.Pack = new FacePack();
        _s.FacePackStamp = DateTime.Now.Ticks;
    }

    void FaceFolder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(FacePack.Folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{FacePack.Folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Opening the face pictures folder failed", ex);
        }
    }

    void ShowFacePictures() => FacePictures.Visibility = _s.FaceStyle == Face.FaceStyle.Custom ? Visibility.Visible : Visibility.Collapsed;
}
