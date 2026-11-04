using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Threading;
using MusicBar.Infrastructure;
using JapanesePhoneticAnalyzer = Windows.Globalization.JapanesePhoneticAnalyzer;

namespace MusicBar.Services;

/// <summary>The writing system a song's lyrics are in, as far as romanizing goes.</summary>
public enum LyricScript { None, Korean, Japanese, Chinese }

/// <summary>
/// Writes Korean, Japanese and Chinese lyric lines in Latin letters so they can be sung along:
/// Revised Romanization for Hangul, Hepburn for kana (kanji are read by the Japanese analyzer
/// that comes with Windows' Japanese language support), and pinyin with tone marks for Han
/// characters, from the Unicode Unihan kMandarin readings embedded in the app.
/// </summary>
public static class Romanizer
{
    const int PinyinFirst = 0x3400, PinyinLast = 0x9FFF;
    const int AnalyzerChunk = 100;

    static readonly Lazy<string?[]> Pinyin = new(LoadPinyin, LazyThreadSafetyMode.ExecutionAndPublication);
    static volatile bool _noAnalyzer;

    /// <summary>
    /// Which script a song is written in, judged from all its lines: any kana means Japanese;
    /// otherwise any Hangul means Korean; otherwise any Han ideographs mean Chinese; else None.
    /// </summary>
    public static LyricScript Detect(IEnumerable<string> lines)
    {
        bool hangul = false, han = false;
        foreach (var line in lines)
        {
            if (string.IsNullOrEmpty(line)) continue;
            foreach (var rune in line.EnumerateRunes())
            {
                int cp = rune.Value;
                if (IsKana(cp)) return LyricScript.Japanese;
                hangul |= IsHangul(cp);
                han |= IsHan(cp);
            }
        }
        return hangul ? LyricScript.Korean : han ? LyricScript.Chinese : LyricScript.None;
    }

    /// <summary>
    /// The line in Latin letters, or null when it holds nothing to romanize (already Latin, blank).
    /// Latin words, digits and punctuation are kept; CJK punctuation becomes its ASCII look-alike.
    /// Hangul and kana are romanized whatever the script; <paramref name="script"/> decides how Han
    /// characters are read (kanji for Japanese, pinyin for Chinese, kept as written for Korean).
    /// None judges the line on its own. Thread-safe.
    /// </summary>
    public static string? Romanize(string line, LyricScript script)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        line = Normalized(line, NormalizationForm.FormC);
        if (script == LyricScript.None) script = Detect([line]);
        if (script == LyricScript.None) return null;

        bool japanese = script == LyricScript.Japanese;
        var w = new LineWriter(line.Length * 4);
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            int cp = CodePointAt(line, i);
            int width = cp > 0xFFFF ? 2 : 1;

            if (IsHangulSyllable(c))
            {
                int end = i;
                while (end < line.Length && IsHangulSyllable(line[end])) end++;
                w.Text(KoreanWord(line, i, end), romanized: true);
                i = end;
            }
            else if (InKanaRun(cp, japanese))
            {
                int end = i;
                while (end < line.Length)
                {
                    int next = CodePointAt(line, end);
                    if (!InKanaRun(next, japanese)) break;
                    end += next > 0xFFFF ? 2 : 1;
                }
                var run = line[i..end];
                if (japanese) JapaneseRun(run, w);
                else KanaOnly(run, w);
                i = end;
            }
            else if (script == LyricScript.Chinese && (IsHan(cp) || cp == 0x3007))
            {
                var reading = PinyinOf(cp);
                w.Word(reading ?? line.Substring(i, width), romanized: reading != null);
                i += width;
            }
            else if (char.IsWhiteSpace(c) || c is '・' or '･')
            {
                w.Space();
                i++;
            }
            else
            {
                w.Text(Lookalike(c) ?? line.Substring(i, width));
                i += width;
            }
        }
        return w.Romanized ? w.ToString() : null;
    }

    /// <summary>The text in the given normalization form, or as it is when it holds broken surrogates.</summary>
    static string Normalized(string s, NormalizationForm form)
    {
        try
        {
            return s.IsNormalized(form) ? s : s.Normalize(form);
        }
        catch (ArgumentException)
        {
            return s;
        }
    }

    static int CodePointAt(string s, int i)
        => char.IsSurrogatePair(s, i) ? char.ConvertToUtf32(s[i], s[i + 1]) : s[i];

    static bool IsHangulSyllable(char c) => c is >= '가' and <= '힣';

    static bool IsHangul(int cp)
        => cp is >= 0xAC00 and <= 0xD7A3 or >= 0x1100 and <= 0x11FF or >= 0x3131 and <= 0x318E
            or >= 0xA960 and <= 0xA97F or >= 0xD7B0 and <= 0xD7FF;

    /// <summary>Hiragana and katakana letters, full and half width; not the ー and ・ marks other scripts borrow.</summary>
    static bool IsKana(int cp)
        => cp is >= 0x3041 and <= 0x3096 or >= 0x309D and <= 0x309F or >= 0x30A1 and <= 0x30FA
            or >= 0x30FD and <= 0x30FF or >= 0x31F0 and <= 0x31FF or >= 0xFF66 and <= 0xFF6F or >= 0xFF71 and <= 0xFF9F;

    static bool IsHan(int cp)
        => cp is >= 0x3400 and <= 0x4DBF or >= 0x4E00 and <= 0x9FFF or >= 0xF900 and <= 0xFAFF or >= 0x20000 and <= 0x323AF;

    /// <summary>Whether the character belongs to a run of Japanese text; kanji only count in Japanese songs.</summary>
    static bool InKanaRun(int cp, bool withKanji)
        => IsKana(cp) || cp is 0x30FC or 0xFF70
            || withKanji && (IsHan(cp) || cp is 0x3005 or 0x3006 or 0x3007);

    /// <summary>The ASCII look-alike of CJK and full-width punctuation, or null to keep the character.</summary>
    static string? Lookalike(char c) => c switch
    {
        '、' or '､' => ",",
        '。' or '｡' => ".",
        '「' or '『' or '｢' => "“",
        '」' or '』' or '｣' => "”",
        '【' or '〔' => "[",
        '】' or '〕' => "]",
        '《' or '〈' => "«",
        '》' or '〉' => "»",
        '〜' => "~",
        >= '！' and <= '～' => ((char)(c - 0xFEE0)).ToString(),
        _ => null,
    };

    /// <summary>
    /// Builds the output line. Romanized Japanese and Chinese words get spaces around them;
    /// everything else, Korean included, keeps the spacing of the source.
    /// </summary>
    sealed class LineWriter(int capacity)
    {
        readonly StringBuilder _sb = new(capacity);
        bool _afterWord;

        public bool Romanized { get; private set; }

        public void Word(string word, bool romanized)
        {
            if (word.Length == 0) return;
            if (NeedsSpace()) _sb.Append(' ');
            _sb.Append(word);
            _afterWord = true;
            Romanized |= romanized;
        }

        public void Text(string text, bool romanized = false)
        {
            if (text.Length == 0) return;
            if (_afterWord && (char.IsLetterOrDigit(text[0]) || IsOpening(text[0]))) _sb.Append(' ');
            _afterWord = false;
            _sb.Append(text);
            Romanized |= romanized;
        }

        public void Space()
        {
            if (_sb.Length > 0 && _sb[^1] != ' ') _sb.Append(' ');
            _afterWord = false;
        }

        bool NeedsSpace()
        {
            if (_sb.Length == 0) return false;
            char last = _sb[^1];
            if (last == ' ' || IsOpening(last) || last is '/' or '-') return false;
            // A straight quote opens when it starts the line or follows a space.
            if (last is '"' or '\'') return _sb.Length > 1 && _sb[^2] != ' ';
            return true;
        }

        static bool IsOpening(char c) => c is '(' or '[' or '{' or '“' or '‘' or '«' or '¿' or '¡';

        public override string ToString() => _sb.ToString().Trim();
    }

    // Korean

    // Initial consonant (choseong) indices.
    const int IG = 0, IKK = 1, IN = 2, ID = 3, IR = 5, IM = 6, IB = 7, IS = 9, ISS = 10, INone = 11,
        IJ = 12, ICh = 14, IK = 15, IT = 16, IP = 17, IH = 18;

    // Final consonant (jongseong) indices.
    const int FNone = 0, FG = 1, FKK = 2, FGS = 3, FN = 4, FNJ = 5, FNH = 6, FD = 7, FL = 8, FLG = 9,
        FLB = 11, FLP = 14, FLH = 15, FM = 16, FB = 17, FBS = 18, FS = 19, FSS = 20, FNg = 21,
        FJ = 22, FCh = 23, FK = 24, FT = 25, FP = 26, FH = 27;

    const int MI = 20;

    enum Coda { None, K, N, T, L, M, P, Ng }

    static readonly string[] Initials =
        ["g", "kk", "n", "d", "tt", "r", "m", "b", "pp", "s", "ss", "", "j", "jj", "ch", "k", "t", "p", "h"];

    static readonly string[] Medials =
        ["a", "ae", "ya", "yae", "eo", "e", "yeo", "ye", "o", "wa", "wae", "oe", "yo", "u", "wo", "we", "wi", "yu", "eu", "ui", "i"];

    /// <summary>How each of the 28 finals sounds at the end of a syllable.</summary>
    static readonly Coda[] CodaOf =
    [
        Coda.None, Coda.K, Coda.K, Coda.K, Coda.N, Coda.N, Coda.N, Coda.T, Coda.L, Coda.K, Coda.M, Coda.L, Coda.L, Coda.L,
        Coda.P, Coda.L, Coda.M, Coda.P, Coda.P, Coda.T, Coda.T, Coda.Ng, Coda.T, Coda.T, Coda.K, Coda.T, Coda.P, Coda.T,
    ];

    static readonly string[] CodaLetters = ["", "k", "n", "t", "l", "m", "p", "ng"];

    /// <summary>
    /// What each final keeps and what it hands over as the next initial when that syllable starts
    /// with a silent ㅇ: 있어 → 이써, 없어 → 업서, 많이 → 마니.
    /// </summary>
    static readonly (int Keep, int Move)[] Liaison =
    [
        (FNone, INone), (FNone, IG), (FNone, IKK), (FG, IS), (FNone, IN), (FN, IJ), (FNone, IN), (FNone, ID),
        (FNone, IR), (FL, IG), (FL, IM), (FL, IB), (FL, IS), (FL, IT), (FL, IP), (FNone, IR),
        (FNone, IM), (FNone, IB), (FB, IS), (FNone, IS), (FNone, ISS), (FNg, INone), (FNone, IJ), (FNone, ICh),
        (FNone, IK), (FNone, IT), (FNone, IP), (FNone, INone),
    ];

    /// <summary>One run of Hangul syllables in Revised Romanization, written as one word.</summary>
    static string KoreanWord(string s, int start, int end)
    {
        int n = end - start;
        var initial = new int[n];
        var medial = new int[n];
        var final = new int[n];
        for (int k = 0; k < n; k++)
        {
            int index = s[start + k] - 0xAC00;
            initial[k] = index / (21 * 28);
            medial[k] = index / 28 % 21;
            final[k] = index % 28;
        }
        for (int k = 0; k + 1 < n; k++)
            (final[k], initial[k + 1]) = Join(final[k], initial[k + 1], medial[k + 1]);

        var sb = new StringBuilder(n * 4);
        for (int k = 0; k < n; k++)
        {
            bool afterL = k > 0 && CodaOf[final[k - 1]] == Coda.L;
            sb.Append(initial[k] == IR && afterL ? "l" : Initials[initial[k]]);
            sb.Append(Medials[medial[k]]);
            sb.Append(CodaLetters[(int)CodaOf[final[k]]]);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The sound changes across one syllable boundary inside a word: liaison (있어 → isseo),
    /// aspiration (좋다 → jota, 축하 → chuka), nasalization (합니다 → hamnida) and the ㄹ rules
    /// (신라 → silla, 정리 → jeongni). Returns the first syllable's new final and the second's new initial.
    /// </summary>
    static (int Final, int Initial) Join(int final, int initial, int nextMedial)
    {
        if (final == FNone) return (final, initial);

        if (initial == INone)
        {
            if (final == FNg) return (final, initial);
            var (keep, move) = Liaison[final];
            if (nextMedial == MI) move = move switch { ID => IJ, IT => ICh, _ => move };  // 같이 → gachi
            return (keep, move);
        }

        if (final is FH or FNH or FLH)
        {
            int keep = final == FNH ? FN : final == FLH ? FL : FNone;
            return initial switch
            {
                IG => (keep, IK),
                ID => (keep, IT),
                IJ => (keep, ICh),
                IS => (keep, ISS),
                IN => final == FLH ? (FL, IR) : (FN, IN),
                _ => (keep, initial),
            };
        }

        if (initial == IH)
        {
            return final switch
            {
                FG or FKK or FGS or FK => (FNone, IK),
                FLG => (FL, IK),
                FD or FS or FSS or FT => (FNone, IT),
                FJ or FCh => (FNone, ICh),
                FNJ => (FN, ICh),
                FB or FBS or FP => (FNone, IP),
                FLB or FLP => (FL, IP),
                _ => (final, initial),
            };
        }

        var coda = CodaOf[final];
        if (initial == IR)
        {
            return coda switch
            {
                Coda.N or Coda.L => (FL, IR),
                Coda.M or Coda.Ng => (final, IN),
                Coda.K => (FNg, IN),
                Coda.T => (FN, IN),
                Coda.P => (FM, IN),
                _ => (final, initial),
            };
        }

        if (initial is IN or IM)
        {
            if (coda == Coda.L && initial == IN) return (FL, IR);  // 설날 → seollal
            return coda switch
            {
                Coda.K => (FNg, initial),
                Coda.T => (FN, initial),
                Coda.P => (FM, initial),
                _ => (final, initial),
            };
        }

        return (final, initial);
    }

    // Japanese

    /// <summary>Hepburn for U+3041 ぁ to U+3096 ゖ; the "-" slot is っ, which the caller handles.</summary>
    static readonly string[] Kana =
        ("a a i i u u e e o o ka ga ki gi ku gu ke ge ko go sa za shi ji su zu se ze so zo "
        + "ta da chi ji - tsu zu te de to do na ni nu ne no ha ba pa hi bi pi fu bu pu he be pe ho bo po "
        + "ma mi mu me mo ya ya yu yu yo yo ra ri ru re ro wa wa i e o n vu ka ke").Split(' ');

    /// <summary>
    /// A run of kana and kanji, read word by word by Windows' analyzer and written as spaced words.
    /// Without the analyzer, kana are still romanized and kanji stay as written.
    /// </summary>
    static void JapaneseRun(string run, LineWriter w)
    {
        if (run.AsSpan().ContainsAnyInRange('｡', 'ﾟ')) run = Normalized(run, NormalizationForm.FormKC);
        var words = Analyze(run);
        if (words == null)
        {
            KanaOnly(run, w);
            return;
        }

        string heldText = "", heldReading = "";
        for (int k = 0; k < words.Count; k++)
        {
            var (text, reading) = words[k];
            text = heldText + text;
            reading = heldReading + ToHiragana(reading);
            // A word ending in small tsu (待っ|て, 分かっ|た) runs into the next one: matte, wakatta.
            if (k + 1 < words.Count && reading.EndsWith('っ'))
            {
                (heldText, heldReading) = (text, reading);
                continue;
            }
            (heldText, heldReading) = ("", "");

            var romaji = k > 0 && text == "は" ? "wa" : k > 0 && text == "へ" ? "e" : KanaToRomaji(reading);
            w.Word(romaji, romanized: romaji != text);
        }
    }

    /// <summary>
    /// Kana without the analyzer: each stretch of hiragana, katakana or other characters (kanji)
    /// becomes one word, kana in romaji and kanji as written. A lone は or へ after kanji is read
    /// as the particle.
    /// </summary>
    static void KanaOnly(string run, LineWriter w)
    {
        int i = 0, previousKind = -1;
        while (i < run.Length)
        {
            int kind = KanaKind(run[i]);
            int end = i + 1;
            while (end < run.Length && (run[end] == 'ー' || KanaKind(run[end]) == kind)) end++;
            var piece = run[i..end];

            if (kind == 0) w.Word(piece, romanized: false);
            else if (previousKind == 0 && piece is "は" or "へ") w.Word(piece == "は" ? "wa" : "e", romanized: true);
            else w.Word(KanaToRomaji(ToHiragana(piece)), romanized: true);

            previousKind = kind;
            i = end;
        }
    }

    /// <summary>1 for hiragana, 2 for katakana, 0 for anything else.</summary>
    static int KanaKind(char c)
        => c is >= 'ぁ' and <= 'ゟ' ? 1 : c is >= '゠' and <= 'ヿ' or >= 'ㇰ' and <= 'ㇿ' ? 2 : 0;

    /// <summary>
    /// Words and their readings from Windows' Japanese phonetic analyzer, or null when it fails or
    /// is missing (it needs the Japanese language features, which most non-Japanese PCs lack).
    /// </summary>
    static List<(string Text, string Reading)>? Analyze(string run)
    {
        if (_noAnalyzer) return null;
        try
        {
            var words = new List<(string, string)>();
            for (int start = 0; start < run.Length;)
            {
                int length = Math.Min(AnalyzerChunk, run.Length - start);
                if (start + length < run.Length && char.IsHighSurrogate(run[start + length - 1])) length--;
                foreach (var word in JapanesePhoneticAnalyzer.GetWords(run.Substring(start, length)))
                    words.Add((word.DisplayText, string.IsNullOrEmpty(word.YomiText) ? word.DisplayText : word.YomiText));
                start += length;
            }
            return words.Count > 0 ? words : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (Exception ex)
        {
            if (!_noAnalyzer) Log.Info($"Japanese phonetic analyzer unavailable, kanji stay as written: {ex.Message.Trim()}");
            _noAnalyzer = true;
            return null;
        }
    }

    /// <summary>Katakana turned into hiragana; everything else is left alone.</summary>
    static string ToHiragana(string s)
    {
        var sb = new StringBuilder(s.Length + 2);
        foreach (char c in s)
        {
            if (c is >= 'ァ' and <= 'ヶ' or 'ヽ' or 'ヾ') sb.Append((char)(c - 0x60));
            else if (c is >= 'ヷ' and <= 'ヺ') sb.Append('ゔ').Append("ぁぃぇぉ"[c - 'ヷ']);
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Hiragana in Hepburn: っ doubles the next consonant (っち → tchi), ん before a vowel or y is
    /// n', and ー doubles the vowel before it. Anything that is not kana passes through.
    /// </summary>
    static string KanaToRomaji(string kana)
    {
        var sb = new StringBuilder(kana.Length * 3);
        bool doubled = false;
        for (int i = 0; i < kana.Length;)
        {
            char c = kana[i];
            if (c == 'っ')
            {
                doubled = true;
                i++;
                continue;
            }
            doubled &= c != 'ー' && c != 'ん';

            if (c == 'ー')
            {
                if (sb.Length > 0 && IsVowel(sb[^1])) sb.Append(sb[^1]);
                i++;
            }
            else if (c == 'ん')
            {
                var next = i + 1 < kana.Length ? Syllable(kana, i + 1, out _) : null;
                sb.Append(next != null && (IsVowel(next[0]) || next[0] == 'y') ? "n'" : "n");
                i++;
            }
            else if (Syllable(kana, i, out int length) is { } syllable)
            {
                if (doubled && !IsVowel(syllable[0])) sb.Append(syllable.StartsWith("ch", StringComparison.Ordinal) ? 't' : syllable[0]);
                doubled = false;
                sb.Append(syllable);
                i += length;
            }
            else
            {
                doubled = false;
                sb.Append(c);
                i++;
            }
        }
        return sb.ToString();
    }

    static bool IsVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u';

    /// <summary>
    /// The romaji of the kana at <paramref name="i"/>, merged with a small kana after it where the
    /// pair is one sound (きゃ kya, しぇ she, ふぁ fa, てぃ ti); null for っ, ん and non-kana.
    /// </summary>
    static string? Syllable(string kana, int i, out int length)
    {
        length = 1;
        char c = kana[i];
        if (c is < 'ぁ' or > 'ゖ' or 'っ' or 'ん') return null;
        var basic = Kana[c - 'ぁ'];
        if (i + 1 >= kana.Length || IsSmall(c)) return basic;

        char next = kana[i + 1];
        var stem = basic[..^1];
        char vowel = basic[^1];
        if (next is 'ゃ' or 'ゅ' or 'ょ' && stem.Length > 0)
        {
            length = 2;
            char v = Kana[next - 'ぁ'][1];
            return stem is "sh" or "ch" or "j" ? stem + v : stem + "y" + v;
        }
        if (next is 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ')
        {
            char v = Kana[next - 'ぁ'][0];
            // Other pairs (かぁ, そぉ) are drawn-out vowels; the small kana is then read on its own.
            string? merged = vowel switch
            {
                'u' when v != 'u' => stem.Length == 0 ? "w" + v : stem is "k" or "g" ? stem + "w" + v : stem + v,
                'i' when v == 'e' => stem.Length == 0 ? "ye" : stem is "sh" or "ch" or "j" ? stem + v : stem + "y" + v,
                'e' when v == 'i' && stem is "t" or "d" => stem + v,
                'o' when v == 'u' && stem is "t" or "d" => stem + v,
                _ => null,
            };
            if (merged != null)
            {
                length = 2;
                return merged;
            }
        }
        return basic;
    }

    static bool IsSmall(char c) => c is 'ぁ' or 'ぃ' or 'ぅ' or 'ぇ' or 'ぉ' or 'ゃ' or 'ゅ' or 'ょ' or 'ゎ' or 'ゕ' or 'ゖ';

    // Chinese

    static string? PinyinOf(int cp)
    {
        if (cp == 0x3007) return "líng";
        var table = Pinyin.Value;
        int index = cp - PinyinFirst;
        return (uint)index < (uint)table.Length ? table[index] : null;
    }

    /// <summary>
    /// Reads the embedded pinyin table: gzip text, one line per run of consecutive code points,
    /// "first-code-point-in-hex reading reading ...". Built from Unihan kMandarin (first reading).
    /// </summary>
    static string?[] LoadPinyin()
    {
        var table = new string?[PinyinLast - PinyinFirst + 1];
        try
        {
            using var resource = typeof(Romanizer).Assembly.GetManifestResourceStream("MusicBar.pinyin.txt.gz");
            if (resource == null)
            {
                Log.Error("The pinyin table is missing from the app");
                return table;
            }
            using var reader = new StreamReader(new GZipStream(resource, CompressionMode.Decompress), Encoding.UTF8);
            var shared = new Dictionary<string, string>(StringComparer.Ordinal);
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int cp)) continue;
                for (int k = 1; k < parts.Length; k++, cp++)
                {
                    int index = cp - PinyinFirst;
                    if ((uint)index >= (uint)table.Length) break;
                    if (!shared.TryGetValue(parts[k], out var reading)) shared[parts[k]] = reading = parts[k];
                    table[index] = reading;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("Reading the pinyin table failed", ex);
        }
        return table;
    }
}
