using System.Reflection;
using System.Text.Json;

namespace KeyFlip;

/// <summary>
/// A pair of keyboard layouts as a table of physical keys, loaded from an embedded
/// JSON file (Layouts/{id}.json). Bad data is a developer error, so Load throws.
/// </summary>
public sealed class KeyLayout
{
    public const string DefaultId = "en-us_ar-101";

    private const char Lam = '\u0644'; // a two-character output must be lam + an alef variant

    public string Id { get; }
    public string Name { get; }

    // One row per physical key press: (English character, Arabic character(s) produced by the same key).
    public IReadOnlyList<(char En, string Ar)> Keys { get; }

    private KeyLayout(string id, string name, IReadOnlyList<(char, string)> keys)
    {
        Id = id;
        Name = name;
        Keys = keys;
    }

    /// <summary>Loads and validates the embedded layout with this id.</summary>
    public static KeyLayout Load(string id)
    {
        string file = $"{id}.json";
        string resource = $"KeyFlip.Layouts.{file}";
        using var stream = typeof(KeyLayout).Assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidDataException($"Layout '{file}': embedded resource '{resource}' not found.");
        try
        {
            using var doc = JsonDocument.Parse(stream);
            return Parse(file, id, doc.RootElement);
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"Layout '{file}': invalid JSON. {e.Message}", e);
        }
    }

    private static KeyLayout Parse(string file, string id, JsonElement root)
    {
        string fileId = GetString(root, "id", $"Layout '{file}'");
        if (fileId != id) throw new InvalidDataException($"Layout '{file}': id '{fileId}' does not match the file name.");
        string name = GetString(root, "name", $"Layout '{file}'");

        if (!root.TryGetProperty("keys", out var rows) || rows.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException($"Layout '{file}': missing 'keys' array.");

        var keys = new List<(char, string)>();
        var seenEn = new HashSet<char>();
        var seenAr = new HashSet<string>(StringComparer.Ordinal);
        int n = 0;
        foreach (var row in rows.EnumerateArray())
        {
            n++;
            string where = $"Layout '{file}', row {n}";
            if (row.ValueKind != JsonValueKind.Object) throw new InvalidDataException($"{where}: not an object.");
            string en = GetString(row, "en", where);
            string ar = GetString(row, "ar", where);

            if (en.Length != 1) throw new InvalidDataException($"{where}: 'en' must be one character, got '{en}'.");
            if (ar.Length is < 1 or > 2) throw new InvalidDataException($"{where} ('{en}'): 'ar' must be one or two characters.");
            if (ar.Length == 2 && ar[0] != Lam) throw new InvalidDataException($"{where} ('{en}'): a two-character 'ar' must start with lam.");
            if (!seenEn.Add(en[0])) throw new InvalidDataException($"{where}: duplicate 'en' key '{en}'.");
            // Single characters and lam-alef variants are reversed separately, so each is unique on its own.
            if (!seenAr.Add(ar.Length == 1 ? ar : "L" + ar[1]))
                throw new InvalidDataException($"{where} ('{en}'): duplicate 'ar' value.");
            keys.Add((en[0], ar));
        }
        if (keys.Count == 0) throw new InvalidDataException($"Layout '{file}': 'keys' is empty.");
        return new KeyLayout(id, name, keys);
    }

    private static string GetString(JsonElement obj, string prop, string where)
    {
        if (!obj.TryGetProperty(prop, out var v) || v.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"{where}: missing string '{prop}'.");
        return v.GetString()!;
    }
}
