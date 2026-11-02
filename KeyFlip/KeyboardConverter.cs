using System.Text;

namespace KeyFlip;

/// <summary>
/// Converts text between English QWERTY and the Windows "Arabic (101)" layout
/// by KEY POSITION. It never translates or spell-corrects.
/// </summary>
public static class KeyboardConverter
{
    // The key table lives in Layouts/en-us_ar-101.json (embedded resource).
    // Four keys (b, T, G, B) produce TWO Arabic characters (lam + alef variant).
    private static readonly IReadOnlyList<(char En, string Ar)> KeyTable = KeyLayout.Load(KeyLayout.DefaultId).Keys;

    private const char Lam = 'ل';

    private static readonly Dictionary<char, string> EnToAr = BuildEnToAr();
    private static readonly Dictionary<char, char> ArToEn = BuildArToEn();
    private static readonly Dictionary<char, char> LamAlefToEn = BuildLamAlefToEn();

    private static Dictionary<char, string> BuildEnToAr()
    {
        var d = new Dictionary<char, string>();
        foreach (var (en, ar) in KeyTable) d.Add(en, ar);
        return d;
    }

    // Reverse map for single-character outputs only. The two-character outputs
    // (lam + an alef) are handled in ToEnglish, using LamAlefToEn.
    private static Dictionary<char, char> BuildArToEn()
    {
        var d = new Dictionary<char, char>();
        foreach (var (en, ar) in KeyTable)
            if (ar.Length == 1) d.Add(ar[0], en);
        return d;
    }

    // Alef variant that follows lam => the key that types both: alef => b, alef-hamza-above => G, ...
    private static Dictionary<char, char> BuildLamAlefToEn()
    {
        var d = new Dictionary<char, char>();
        foreach (var (en, ar) in KeyTable)
            if (ar.Length == 2 && ar[0] == Lam) d.Add(ar[1], en);
        return d;
    }

    public enum Direction { EnglishToArabic, ArabicToEnglish }

    /// <summary>Majority vote: Arabic letters vs ASCII letters. Ties or no letters => EnglishToArabic.</summary>
    public static Direction Detect(string text)
    {
        int arabic = 0, latin = 0;
        foreach (char c in text)
        {
            if (c >= '\u0600' && c <= '\u06FF') arabic++;
            else if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z')) latin++;
        }
        return arabic > latin ? Direction.ArabicToEnglish : Direction.EnglishToArabic;
    }

    public static string Convert(string text, Direction direction)
        => direction == Direction.EnglishToArabic ? ToArabic(text) : ToEnglish(text);

    private static string ToArabic(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
            sb.Append(EnToAr.TryGetValue(c, out var ar) ? ar : c.ToString());
        return sb.ToString();
    }

    // Lam followed by an alef variant can come from one key (b, G, T, B) or from two (g then h, H, Y, N).
    // English almost never has "gH", "gY" or "gN", so those always become G, T, B.
    // "gh" is common enough that plain lam-alef is decided per word: "b" unless "gh" spells a known word.
    private static string ToEnglish(string text)
    {
        var sb = new StringBuilder(text.Length);
        var lamAlefB = new List<int>(); // positions in sb where a plain lam-alef was written as 'b'
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == Lam && i + 1 < text.Length && LamAlefToEn.TryGetValue(text[i + 1], out char key))
            {
                if (key == 'b') lamAlefB.Add(sb.Length);
                sb.Append(key);
                i++;
            }
            else
            {
                sb.Append(ArToEn.TryGetValue(c, out var en) ? en : c);
            }
        }
        return lamAlefB.Count == 0 ? sb.ToString() : ResolveLamAlef(sb.ToString(), lamAlefB);
    }

    private static string ResolveLamAlef(string s, List<int> positions)
    {
        var result = new StringBuilder(s.Length + positions.Count);
        int p = 0, copied = 0;
        while (p < positions.Count)
        {
            int start = positions[p], end = positions[p];
            while (start > 0 && IsAsciiLetter(s[start - 1])) start--;
            while (end + 1 < s.Length && IsAsciiLetter(s[end + 1])) end++;

            var inWord = new List<int>();
            while (p < positions.Count && positions[p] <= end) inWord.Add(positions[p++] - start);

            result.Append(s, copied, start - copied);
            result.Append(ChooseSpelling(s.Substring(start, end - start + 1), inWord));
            copied = end + 1;
        }
        result.Append(s, copied, s.Length - copied);
        return result.ToString();
    }

    // Tries "gh" in place of each ambiguous 'b' (at most 4 per word) and keeps the first known word.
    private static string ChooseSpelling(string word, List<int> bPositions)
    {
        if (bPositions.Count > 4) return word;
        for (int mask = 1; mask < 1 << bPositions.Count; mask++)
        {
            var sb = new StringBuilder(word.Length + bPositions.Count);
            int next = 0;
            for (int i = 0; i < word.Length; i++)
            {
                if (next < bPositions.Count && bPositions[next] == i)
                    sb.Append((mask & (1 << next++)) != 0 ? "gh" : "b");
                else
                    sb.Append(word[i]);
            }
            string candidate = sb.ToString();
            if (GhWords.IsKnown(candidate.ToLowerInvariant())) return candidate;
        }
        return word;
    }

    private static bool IsAsciiLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

    /// <summary>Auto-detects direction, then converts.</summary>
    public static string Convert(string text) => Convert(text, Detect(text));

    // Exposed for tests only.
    public static IReadOnlyList<(char En, string Ar)> Table => KeyTable;
}
