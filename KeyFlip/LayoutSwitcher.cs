using System.Runtime.InteropServices;

namespace KeyFlip;

/// <summary>Switches the foreground window's keyboard layout to Arabic or English.</summary>
public static class LayoutSwitcher
{
    public const ushort LangArabic = 0x01, LangEnglish = 0x09; // primary language IDs

    // Preferred keyboards (high word of the HKL): Arabic (101) and US, whatever the locale.
    private const ushort PreferredArabic = 0x0401, PreferredEnglish = 0x0409;

    /// <summary>
    /// Picks the layout for a primary language (LangArabic or LangEnglish) from installed layouts.
    /// Prefers the Arabic (101) / US keyboard, then any variant of the language. Returns null if none match.
    /// </summary>
    public static IntPtr? Choose(IReadOnlyList<IntPtr> layouts, ushort primaryLang)
    {
        ushort preferred = primaryLang == LangArabic ? PreferredArabic : PreferredEnglish;
        IntPtr? any = null;
        foreach (var hkl in layouts)
        {
            long v = hkl.ToInt64();
            ushort lang = (ushort)(v & 0xFFFF), keyboard = (ushort)((v >> 16) & 0xFFFF);
            if ((lang & 0x3FF) != primaryLang) continue;
            if (keyboard == preferred) return hkl;
            any ??= hkl;
        }
        return any;
    }

    /// <summary>Best effort: never throws, does nothing if no matching layout is installed.</summary>
    public static void SwitchForeground(ushort primaryLang)
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return;

            int count = GetKeyboardLayoutList(0, null);
            if (count <= 0) return;
            var list = new IntPtr[count];
            count = GetKeyboardLayoutList(count, list);
            if (count <= 0) return;

            IntPtr? target = Choose(list.Take(count).ToArray(), primaryLang);
            if (target == null) return;

            uint thread = GetWindowThreadProcessId(fg, out _);
            if (GetKeyboardLayout(thread) == target.Value) return;

            PostMessage(fg, WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, target.Value);
        }
        catch { /* switching layout is a convenience; never fail the conversion */ }
    }

    private const uint WM_INPUTLANGCHANGEREQUEST = 0x0050;

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll")] private static extern IntPtr GetKeyboardLayout(uint idThread);
    [DllImport("user32.dll")] private static extern int GetKeyboardLayoutList(int nBuff, [Out] IntPtr[]? lpList);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
}
