using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Models;
using MusicBar.Services;
using MusicBar.Views.Face;

namespace MusicBar.Views;

/// <summary>
/// "Your year in music is ready", in the corner above the tray between Christmas Eve and the 7th of
/// January, without taking focus. Show me opens the recap; Later asks again another day; ticking
/// the box with Later stays quiet until next year.
/// </summary>
public partial class RecapBannerWindow : Window
{
    readonly AppSettings _s;
    readonly int _year;
    readonly DispatcherTimer _clock = new(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(33) };

    public RecapBannerWindow(YearRecap recap, AppSettings settings, Color accent)
    {
        _s = settings;
        _year = recap.Year;
        InitializeComponent();
        var color = ColorUtil.ForDarkSurface(accent);
        Resources["Accent"] = new SolidColorBrush(color);
        Glow.Color = ColorUtil.Lerp(Color.FromRgb(0x14, 0x14, 0x18), color, 0.28);
        Heading.Text = Loc.T("RecB_Title", recap.Year);
        Body.Text = Loc.T("RecB_Body", recap.Minutes >= 120
            ? Loc.T("Rec_HoursMinutes", (int)(recap.Minutes / 60), (int)(recap.Minutes % 60))
            : Loc.T("Rec_Minutes", (int)recap.Minutes));

        Face.Look = settings.FaceStyle;
        Face.Outfit = FaceWindow.OutfitFor(settings, DateTime.Now);
        Face.Accent = color;
        Face.Brain.FollowsClock = false;
        _clock.Tick += (_, _) => Face.Step(new FaceSenses(DateTime.Now, true, true, -1, TimeSpan.Zero, null, false));
        _clock.Start();

        // In the corner above the tray, where Windows puts its own notifications.
        SizeChanged += (_, _) =>
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - ActualWidth;
            Top = area.Bottom - ActualHeight;
        };
        Closed += (_, _) => _clock.Stop();
    }

    /// <summary>Asked to open the recap.</summary>
    public event Action? OpenRequested;

    void Later_Click(object sender, RoutedEventArgs e)
    {
        if (NeverBox.IsChecked == true) _s.RecapDoneYear = _year;
        Close();
    }

    void Open_Click(object sender, RoutedEventArgs e)
    {
        _s.RecapDoneYear = _year;
        OpenRequested?.Invoke();
        Close();
    }
}
