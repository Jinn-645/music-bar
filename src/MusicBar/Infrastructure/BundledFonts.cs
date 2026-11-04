using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;

namespace MusicBar.Infrastructure;

/// <summary>
/// Free fonts (SIL Open Font License, from Google Fonts) shipped inside MusicBar, all with
/// full Vietnamese support, so a good lyric font is there without installing anything.
/// Their licenses sit in Assets/Fonts/licenses.
/// </summary>
public static class BundledFonts
{
    public static IReadOnlyList<string> Names { get; } =
    [
        "Be Vietnam Pro", "Lexend", "Montserrat", "Quicksand", "Nunito",
        "Comfortaa", "Playfair Display", "Lora", "Dancing Script", "Pacifico",
    ];

    static readonly Uri Base = new("pack://application:,,,/");

    /// <summary>A bundled font from the app's resources, or an installed one by name; Segoe UI fills any gap.</summary>
    public static FontFamily Resolve(string name) => Names.Contains(name)
        ? new FontFamily(Base, $"./Assets/Fonts/#{name}, Segoe UI")
        : new FontFamily(name + ", Segoe UI");
}
