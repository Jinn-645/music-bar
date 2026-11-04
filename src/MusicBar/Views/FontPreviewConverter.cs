using System;
using System.Globalization;
using System.Windows.Data;
using MusicBar.Infrastructure;

namespace MusicBar.Views;

/// <summary>A font name to the font itself, bundled ones included, for previews in the font list.</summary>
public sealed class FontPreviewConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => BundledFonts.Resolve(value as string ?? "Segoe UI");

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
