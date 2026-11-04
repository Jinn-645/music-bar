using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using MusicBar.Infrastructure;
using MusicBar.Models;

namespace MusicBar.Services;

public sealed class SettingsService
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters =
        {
            new StyleConverter<SeekStyle>(SeekStyle.Aurora),
            new StyleConverter<ControlStyle>(ControlStyle.Glow),
            new StyleConverter<AppLanguage>(AppLanguage.English),
            new StyleConverter<DisplayTarget>(DisplayTarget.Main),
            new StyleConverter<SeekDirection>(SeekDirection.FromStart),
            new StyleConverter<PlayerTarget>(PlayerTarget.SameAsBar),
            new StyleConverter<PlayerMode>(PlayerMode.Full),
            new JsonStringEnumConverter(),
        },
    };

    /// <summary>A style removed (or not yet known) in another version loads as the default instead of failing the whole file.</summary>
    sealed class StyleConverter<T>(T fallback) : JsonConverter<T> where T : struct, Enum
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.TokenType == JsonTokenType.String
               && Enum.TryParse<T>(reader.GetString(), ignoreCase: true, out var style)
               && Enum.IsDefined(style)
                ? style
                : fallback;

        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
            => writer.WriteStringValue(value.ToString());
    }

    readonly DispatcherTimer _saveTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    public SettingsService()
    {
        Current = Load(out var firstRun);
        IsFirstRun = firstRun;
        Current.PropertyChanged += (_, _) =>
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveNow();
        };
    }

    public AppSettings Current { get; }
    public bool IsFirstRun { get; }

    static AppSettings Load(out bool firstRun)
    {
        firstRun = !File.Exists(AppPaths.SettingsFile);
        if (firstRun) return new AppSettings();
        try
        {
            var json = File.ReadAllText(AppPaths.SettingsFile);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log.Error("Settings file unreadable, using defaults", ex);
            return new AppSettings();
        }
    }

    public void SaveNow()
    {
        try
        {
            var tmp = AppPaths.SettingsFile + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Current, JsonOptions));
            File.Move(tmp, AppPaths.SettingsFile, overwrite: true);
        }
        catch (Exception ex)
        {
            Log.Error("Saving settings failed", ex);
        }
    }
}
