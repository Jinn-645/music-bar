using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MusicBar.Infrastructure;

namespace MusicBar.Services;

/// <summary>
/// A face made of the user's own pictures instead of a drawn one: one picture (still, GIF or
/// WebP, animated or not) for each state the face can be in. They live in
/// %LOCALAPPDATA%\MusicBar\Face, named after their state ("idle.gif", "playing.webp"...). Only
/// "idle" is needed: a missing state borrows a related one, and in the end the idle picture.
/// Pictures are read with Windows' own decoders (kept up to date by Windows Update), held at
/// 256 pixels and 48 frames at most so a face never weighs on the PC. A GIF's frames are put
/// together the way GIF says (each frame's place, what to clear after it, how long it shows).
/// </summary>
public sealed class FacePack
{
    public const int MaxSize = 256;
    const int MaxFrames = 48;

    /// <summary>The states, in the order the settings list them.</summary>
    public static readonly string[] States = ["idle", "playing", "paused", "sleeping", "bored", "happy", "dizzy", "sad", "focus"];

    // Where a missing state borrows its picture from, before falling back to idle.
    static readonly Dictionary<string, string[]> Borrow = new()
    {
        ["playing"] = ["happy"],
        ["paused"] = [],
        ["sleeping"] = ["paused", "bored"],
        ["bored"] = ["paused"],
        ["happy"] = ["playing"],
        ["dizzy"] = ["happy", "sad"],
        ["sad"] = ["paused"],
        ["focus"] = [],
    };

    public static readonly string[] Extensions = [".gif", ".webp", ".png", ".jpg", ".jpeg", ".bmp"];

    /// <summary>A state's picture: its frames and when each one ends, in seconds from the start.</summary>
    public sealed record Clip(BitmapSource[] Frames, double[] Ends)
    {
        public double Length => Ends[^1];

        public BitmapSource At(double seconds)
        {
            if (Frames.Length == 1 || Length <= 0) return Frames[0];
            double t = seconds % Length;
            int i = Array.BinarySearch(Ends, t);
            i = i < 0 ? ~i : i + 1;
            return Frames[Math.Min(i, Frames.Length - 1)];
        }
    }

    readonly Dictionary<string, Clip?> _clips = new(StringComparer.OrdinalIgnoreCase);

    public static string Folder => Path.Combine(AppPaths.DataDir, "Face");

    /// <summary>The file holding a state's picture, if there is one.</summary>
    public static string? FileFor(string state) => Directory.Exists(Folder)
        ? Extensions.Select(ext => Path.Combine(Folder, state + ext)).FirstOrDefault(File.Exists)
        : null;

    /// <summary>There is a picture for the idle face, which a pack needs at the least.</summary>
    public static bool Exists => FileFor("idle") != null;

    /// <summary>The picture for a state, or the one it borrows; read the first time it is wanted.</summary>
    public Clip? For(string state)
    {
        foreach (var candidate in new[] { state }.Concat(Borrow.GetValueOrDefault(state, [])).Append("idle"))
        {
            if (!_clips.TryGetValue(candidate, out var clip))
            {
                clip = FileFor(candidate) is { } file ? Read(file) : null;
                _clips[candidate] = clip;
            }
            if (clip != null) return clip;
        }
        return null;
    }

    /// <summary>
    /// Takes a picture for a state: checked by reading it, then copied into the folder under the
    /// state's name (a bigger one is shrunk to <see cref="MaxSize"/> when it is read, every time).
    /// Returns why it can't be used, or null.
    /// </summary>
    public static string? Import(string state, string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!Extensions.Contains(ext)) return Loc.T("Face_Pic_Kind");
        if (new FileInfo(path).Length > 12 * 1024 * 1024) return Loc.T("Face_Pic_TooBig");
        if (Read(path) == null) return Loc.T("Face_Pic_Unreadable");
        Directory.CreateDirectory(Folder);
        Remove(state);
        File.Copy(path, Path.Combine(Folder, state + ext), overwrite: true);
        Log.Info($"Face picture for '{state}': {Path.GetFileName(path)}");
        return null;
    }

    public static void Remove(string state)
    {
        foreach (var ext in Extensions)
        {
            var file = Path.Combine(Folder, state + ext);
            if (File.Exists(file)) File.Delete(file);
        }
    }

    /// <summary>A picture as frames WPF can draw, each with how long it shows; null when Windows can't read it.</summary>
    public static Clip? Read(string file)
    {
        try
        {
            var bytes = File.ReadAllBytes(file);
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int count = decoder.Frames.Count;
            if (count == 0) return null;
            bool gif = decoder is GifBitmapDecoder;
            // Windows reads an animated WebP's frames but not their timing, which the file has in plain sight.
            var webp = gif ? null : WebpDurations(bytes);
            if (webp != null && webp.Count != count) webp = null;
            int width = gif ? Query<ushort>(decoder.Metadata, "/logscrdesc/Width") ?? decoder.Frames[0].PixelWidth : decoder.Frames[0].PixelWidth;
            int height = gif ? Query<ushort>(decoder.Metadata, "/logscrdesc/Height") ?? decoder.Frames[0].PixelHeight : decoder.Frames[0].PixelHeight;
            if (width <= 0 || height <= 0) return null;
            double scale = Math.Min(1, MaxSize / (double)Math.Max(width, height));
            int w = Math.Max(1, (int)Math.Round(width * scale)), h = Math.Max(1, (int)Math.Round(height * scale));

            // Long animations keep every n-th frame, each showing as long as the frames it stands in for.
            int keepEvery = Math.Max(1, (int)Math.Ceiling(count / (double)MaxFrames));
            var frames = new List<BitmapSource>();
            var ends = new List<double>();
            double total = 0, owed = 0;
            BitmapSource? canvas = null;
            for (int i = 0; i < count; i++)
            {
                var frame = decoder.Frames[i];
                var meta = frame.Metadata as BitmapMetadata;
                owed += webp != null ? Math.Max(0.02, webp[i]) : Delay(meta, gif);
                BitmapSource shown;
                if (gif)
                {
                    // Drawn over what is left of the frames before it, at its own place.
                    double left = (Query<ushort>(meta, "/imgdesc/Left") ?? 0) * scale;
                    double top = (Query<ushort>(meta, "/imgdesc/Top") ?? 0) * scale;
                    var place = new Rect(left, top, frame.PixelWidth * scale, frame.PixelHeight * scale);
                    shown = Draw(w, h, canvas, frame, place, null);
                    canvas = (Query<byte>(meta, "/grctlext/Disposal") ?? 0) switch
                    {
                        2 => Draw(w, h, shown, null, Rect.Empty, place),   // cleared back to nothing
                        3 => canvas,                                       // back to how it was before
                        _ => shown,
                    };
                }
                else
                {
                    shown = Draw(w, h, null, frame, new Rect(0, 0, w, h), null);
                }
                if (i % keepEvery != 0 && i != count - 1) continue;
                frames.Add(shown);
                total += owed;
                owed = 0;
                ends.Add(total);
            }
            if (owed > 0 && ends.Count > 0) ends[^1] += owed;
            return new Clip([.. frames], [.. ends]);
        }
        catch (Exception ex)
        {
            Log.Error($"Reading the face picture {Path.GetFileName(file)} failed", ex);
            return null;
        }
    }

    /// <summary>A new frame: the canvas so far, a picture on it, and a rectangle cleared out of it.</summary>
    static BitmapSource Draw(int w, int h, BitmapSource? under, BitmapSource? picture, Rect place, Rect? clear)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            if (under != null)
            {
                if (clear is { } c) dc.PushClip(new CombinedGeometry(GeometryCombineMode.Exclude, new RectangleGeometry(new Rect(0, 0, w, h)), new RectangleGeometry(c)));
                dc.DrawImage(under, new Rect(0, 0, w, h));
                if (clear != null) dc.Pop();
            }
            if (picture != null) dc.DrawImage(picture, place);
        }
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.HighQuality);
        var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>How long a frame shows: a GIF's delay is in hundredths; too fast, unset or unknown is a tenth.</summary>
    static double Delay(BitmapMetadata? meta, bool gif)
    {
        double seconds = gif ? (Query<ushort>(meta, "/grctlext/Delay") ?? 0) / 100.0 : 0;
        return seconds >= 0.02 ? seconds : 0.1;
    }

    /// <summary>
    /// An animated WebP's frame durations in seconds, from its ANMF chunks (RIFF: four-letter tag,
    /// little-endian size, data padded to even); null for a still or a file that isn't WebP.
    /// </summary>
    static List<double>? WebpDurations(byte[] b)
    {
        if (b.Length < 20 || b[0] != 'R' || b[1] != 'I' || b[2] != 'F' || b[3] != 'F' || b[8] != 'W' || b[9] != 'E' || b[10] != 'B' || b[11] != 'P') return null;
        var durations = new List<double>();
        for (int at = 12; at + 8 <= b.Length;)
        {
            int size = b[at + 4] | b[at + 5] << 8 | b[at + 6] << 16 | b[at + 7] << 24;
            if (size < 0 || at + 8 + size > b.Length) break;
            // ANMF: x, y, width-1, height-1 (three bytes each), then the duration in milliseconds (three bytes).
            if (b[at] == 'A' && b[at + 1] == 'N' && b[at + 2] == 'M' && b[at + 3] == 'F' && size >= 15)
            {
                int d = at + 8 + 12;
                durations.Add((b[d] | b[d + 1] << 8 | b[d + 2] << 16) / 1000.0);
            }
            at += 8 + size + (size & 1);
        }
        return durations.Count > 0 ? durations : null;
    }

    static T? Query<T>(ImageMetadata? metadata, string query) where T : struct
    {
        try
        {
            return metadata is BitmapMetadata m && m.ContainsQuery(query) && m.GetQuery(query) is T value ? value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
