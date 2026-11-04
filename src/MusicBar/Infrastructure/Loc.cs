using System;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using MusicBar.Models;

namespace MusicBar.Infrastructure;

/// <summary>
/// The UI's words in the chosen language. Code asks <see cref="T(string)"/>; XAML binds with
/// <c>{l:Tr Key}</c>, which follows a language switch at once. Text that code builds (menus,
/// messages) picks the new language up the next time it is built.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    AppLanguage _language = AppLanguage.English;

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (value == _language) return;
            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(Binding.IndexerName));
            Changed?.Invoke();
        }
    }

    /// <summary>Raised after the language changes, for text that code builds.</summary>
    public static event Action? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string this[string key]
    {
        get
        {
            if (Strings.All.TryGetValue(key, out var text)) return _language == AppLanguage.Vietnamese ? text.Vi : text.En;
            Log.Info($"Missing string: {key}");
            return key;
        }
    }

    public static string T(string key) => Instance[key];

    /// <summary>A string with {0}-style holes.</summary>
    public static string T(string key, params object?[] args) => string.Format(Instance[key], args);
}

/// <summary><c>{l:Tr Key}</c>: a string from <see cref="Strings"/> that follows the chosen language.</summary>
[MarkupExtensionReturnType(typeof(object))]
public sealed class TrExtension : MarkupExtension
{
    public TrExtension() { }

    public TrExtension(string key) => Key = key;

    [ConstructorArgument("key")]
    public string Key { get; set; } = "";

    public override object ProvideValue(IServiceProvider serviceProvider)
        => new Binding($"[{Key}]") { Source = Loc.Instance, Mode = BindingMode.OneWay }.ProvideValue(serviceProvider);
}
