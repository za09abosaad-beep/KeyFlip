using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Forms;

namespace KeyFlip;

public static class HotkeyFormat
{
    public static string Describe(HotkeyModifiers m, Keys k)
    {
        var parts = new List<string>();
        if (m.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (m.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (m.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (m.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(k.ToString());
        return string.Join("+", parts);
    }
}

public static class HotkeyRules
{
    // Taking these globally would break them in every program.
    private static readonly (HotkeyModifiers Mods, Keys Key)[] Reserved =
    {
        (HotkeyModifiers.Control, Keys.X), (HotkeyModifiers.Control, Keys.Z), (HotkeyModifiers.Control, Keys.Y),
        (HotkeyModifiers.Control, Keys.A), (HotkeyModifiers.Control, Keys.S), (HotkeyModifiers.Control, Keys.Insert),
        (HotkeyModifiers.Control, Keys.Escape), (HotkeyModifiers.Control | HotkeyModifiers.Shift, Keys.Escape),
        (HotkeyModifiers.Alt, Keys.F4), (HotkeyModifiers.Alt, Keys.Tab), (HotkeyModifiers.Alt, Keys.Escape),
        (HotkeyModifiers.Alt, Keys.Space),
    };

    /// <summary>Returns why the shortcut cannot be used, or null if it is fine.</summary>
    public static string? Problem(HotkeyModifiers mods, Keys key)
    {
        mods &= ~HotkeyModifiers.NoRepeat;
        if (key is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin)
            return "Press a key together with the modifier keys.";
        if ((mods & (HotkeyModifiers.Control | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0)
            return "Please include Ctrl, Alt or Win.";
        string name = HotkeyFormat.Describe(mods, key);
        // KeyFlip sends Ctrl+C and Ctrl+V itself; as its own shortcut they would trigger KeyFlip instead of copying.
        if (mods == HotkeyModifiers.Control && key is Keys.C or Keys.V)
            return name + " is needed for copy and paste.";
        if (Reserved.Contains((mods, key)))
            return name + " is used by Windows or most programs.";
        return null;
    }
}

/// <summary>Saved in %AppData%\KeyFlip\settings.json</summary>
public sealed class AppSettings
{
    public int Modifiers { get; set; } = (int)(HotkeyModifiers.Control | HotkeyModifiers.Alt);
    public int Key { get; set; } = (int)Keys.Space;
    public bool Enabled { get; set; } = true;
    public bool SwitchLayout { get; set; } = true;

    [JsonIgnore] public HotkeyModifiers Mods => (HotkeyModifiers)Modifiers;
    [JsonIgnore] public Keys KeyCode => (Keys)Key;

    private static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "KeyFlip", "settings.json");

    public static AppSettings Load()
    {
        var settings = new AppSettings();
        try
        {
            if (File.Exists(FilePath))
                settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? settings;
        }
        catch { /* corrupt file: fall back to defaults */ }

        // A hand-edited file could contain a shortcut the Settings window would refuse.
        if (HotkeyRules.Problem(settings.Mods, settings.KeyCode) != null)
        {
            var defaults = new AppSettings();
            settings.Modifiers = defaults.Modifiers;
            settings.Key = defaults.Key;
        }
        return settings;
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* settings are a convenience; never crash the app */ }
    }
}
