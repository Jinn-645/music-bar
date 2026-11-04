using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace MusicBar.Services;

/// <summary>
/// LRCLIB's Lyricsfile: YAML with each line's text and times and, when someone synced the song word
/// by word, each word's start. Only that much of YAML is read (the block style LRCLIB and its
/// clients write, sequences indented or not); anything else makes it give up, and the plain synced
/// lyrics are used instead.
/// </summary>
public static class Lyricsfile
{
    sealed class Item
    {
        public long? Start;
        public string? Text;
        public List<Item> Words = [];
    }

    /// <summary>
    /// The lyrics as enhanced LRC ("[mm:ss.xx]&lt;mm:ss.xxx&gt;word &lt;…&gt;word"), which MusicBar's
    /// LRC reader takes word times from; null when the file has no word timing or can't be read.
    /// </summary>
    public static string? ToWordLrc(string? yaml)
    {
        if (string.IsNullOrWhiteSpace(yaml) || !yaml.Contains("words:", StringComparison.Ordinal)) return null;
        List<Item> lines;
        try
        {
            lines = Read(yaml);
        }
        catch (FormatException)
        {
            return null;
        }
        if (!lines.Any(l => l.Words.Any(w => w.Start != null))) return null;

        var sb = new StringBuilder();
        foreach (var line in lines)
        {
            if (line.Start is not long start) continue;
            sb.Append(LrcParser.Stamp(TimeSpan.FromMilliseconds(start)));
            if (line.Words.Count == 0)
            {
                sb.Append(line.Text);
            }
            else
            {
                foreach (var w in line.Words)
                {
                    if (w.Start is long ws) sb.Append(LrcParser.Stamp(TimeSpan.FromMilliseconds(ws), word: true));
                    sb.Append(w.Text);
                }
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    static List<Item> Read(string yaml)
    {
        var lines = new List<Item>();
        bool inLines = false, inWords = false;
        int lineItemIndent = -1, wordItemIndent = -1;
        Item? line = null, word = null;

        foreach (var raw in yaml.Replace("\r\n", "\n").Split('\n'))
        {
            if (raw.Trim().Length == 0 || raw.TrimStart().StartsWith('#')) continue;
            int indent = raw.Length - raw.TrimStart(' ').Length;
            var text = raw.Trim();
            bool item = text.StartsWith("- ", StringComparison.Ordinal) || text == "-";

            // A top-level key opens or closes the "lines" block (its items may sit at column 0).
            if (indent == 0 && !item)
            {
                inLines = text.StartsWith("lines:", StringComparison.Ordinal);
                inWords = false;
                continue;
            }
            if (!inLines) continue;

            if (item)
            {
                text = text.Length > 1 ? text[2..].TrimStart() : "";
                if (lineItemIndent < 0 || indent <= lineItemIndent)
                {
                    lineItemIndent = indent;
                    line = new Item();
                    lines.Add(line);
                    word = null;
                    inWords = false;
                    wordItemIndent = -1;
                    if (text.Length > 0) inWords = OpensWords(Assign(line, text), text, inWords);
                }
                else if (inWords && line != null)
                {
                    if (wordItemIndent < 0) wordItemIndent = indent;
                    word = new Item();
                    line.Words.Add(word);
                    if (text.Length > 0) Assign(word, text);
                }
                continue;
            }

            if (line == null) continue;
            // A key deeper than the word items belongs to the current word; otherwise to the line.
            if (inWords && word != null && wordItemIndent >= 0 && indent > wordItemIndent) Assign(word, text);
            else inWords = OpensWords(Assign(line, text), text, inWords);
        }
        return lines;
    }

    /// <summary>A line's "words:" opens its word list (unless it is "[]"); another of its keys after it closes it.</summary>
    static bool OpensWords(string? key, string text, bool inWords) => key switch
    {
        "words" => !text.TrimEnd().EndsWith("[]", StringComparison.Ordinal),
        "end_ms" or null => inWords,
        _ => false,
    };

    /// <summary>One "key: value" into an item; returns the key.</summary>
    static string? Assign(Item item, string text)
    {
        int colon = text.IndexOf(':');
        if (colon <= 0) return null;
        var key = text[..colon].Trim();
        var value = text[(colon + 1)..].Trim();
        switch (key)
        {
            case "text":
                if (value.StartsWith('|') || value.StartsWith('>')) throw new FormatException("block scalar");
                item.Text = Scalar(value);
                break;
            case "start_ms":
                item.Start = long.TryParse(Scalar(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) ? ms : null;
                break;
        }
        return key;
    }

    /// <summary>A YAML scalar: 'single' (with '' for a quote), "double" (with backslash escapes) or plain.</summary>
    static string Scalar(string value)
    {
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].Replace("''", "'");
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
        {
            var s = value[1..^1];
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length)
                {
                    sb.Append(s[i]);
                    continue;
                }
                char c = s[++i];
                switch (c)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u' when i + 4 < s.Length && int.TryParse(s.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code):
                        sb.Append((char)code);
                        i += 4;
                        break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }
        return value is "~" or "null" ? "" : value;
    }
}
