using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace KeyFlip;

public sealed record ConvertResult(bool Success, string Title, string Message);

/// <summary>
/// Copies the current selection, converts it, pastes it back, and restores the
/// user's original clipboard. Must be called on the UI (STA) thread.
/// </summary>
public sealed class SelectionConverter
{
    private bool _busy;
    private (string Original, string Converted)? _last;

    /// <summary>Switch the keyboard layout to the language of the pasted text after converting.</summary>
    public bool SwitchLayout { get; set; } = true;

    /// <param name="hotkeyKey">The non-modifier key of the shortcut (for example Space).</param>
    public async Task<ConvertResult> RunAsync(Keys hotkeyKey)
    {
        if (_busy) return new(false, "KeyFlip", "Still working on the previous conversion.");
        _busy = true;
        try { return await DoConvertAsync(hotkeyKey); }
        catch (Exception ex) { return new(false, "KeyFlip error", ex.Message); }
        finally { _busy = false; }
    }

    private async Task<ConvertResult> DoConvertAsync(Keys hotkeyKey)
    {
        // 1. The user is still holding the shortcut keys. Wait until they let go of all of them,
        //    otherwise our Ctrl+C would arrive as Ctrl+Alt+C, or mix with the held key (Ctrl+Space).
        if (!await WaitForKeysReleasedAsync(hotkeyKey, 1500))
            return new(false, "KeyFlip", "Release the shortcut keys and try again.");

        // 2. Remember the user's clipboard BEFORE touching it.
        var snapshot = ClipboardSnapshot.Capture();
        if (snapshot == null)
            return new(false, "KeyFlip", "The clipboard is busy (used by another program). Nothing was changed.");

        // 3. Ask the foreground app to copy. If the clipboard sequence number does not
        //    change, nothing was copied and the clipboard is exactly as the user left it.
        uint before = GetClipboardSequenceNumber();
        SendCtrlKey(VK_C);
        if (!await WaitForClipboardChangeAsync(before, 1000))
            return new(false, "KeyFlip",
                "Nothing was copied. Select some text first, or this app does not allow copying. Your clipboard was not changed.");
        await Task.Delay(50);

        // From here on the clipboard HAS been modified, so every path must restore it.
        string suffix = "";
        try
        {
            string? text = await TryGetTextAsync();
            if (string.IsNullOrEmpty(text))
                return new(false, "KeyFlip", "The selection did not contain text.");

            // Converting KeyFlip's own result again gives back exactly what was selected before
            // (converting back to English has to guess where each lam-alef came from).
            var direction = KeyboardConverter.Detect(text);
            string converted = _last is { } last && last.Converted == text
                ? last.Original
                : KeyboardConverter.Convert(text, direction);
            if (converted == text)
                return new(false, "KeyFlip", "Nothing to convert in the selected text.");

            var data = new DataObject();
            data.SetData(DataFormats.UnicodeText, true, converted);
            ClipboardSnapshot.ExcludeFromHistory(data);
            try { Clipboard.SetDataObject(data, true, 10, 50); }
            catch (ExternalException)
            {
                return new(false, "KeyFlip", "Could not use the clipboard to paste. The selected text was not changed.");
            }

            SendCtrlKey(VK_V);
            _last = (text, converted);
            // Judge the language of what was actually pasted (also right when undoing a conversion).
            if (SwitchLayout)
                LayoutSwitcher.SwitchForeground(KeyboardConverter.Detect(converted) == KeyboardConverter.Direction.ArabicToEnglish
                    ? LayoutSwitcher.LangArabic : LayoutSwitcher.LangEnglish);
            // Give the target app time to read the clipboard before we put the old content back.
            await Task.Delay(400);

            string dir = direction == KeyboardConverter.Direction.EnglishToArabic ? "English → Arabic" : "Arabic → English";
            return new(true, "KeyFlip", $"Converted {text.Length} characters ({dir}).");
        }
        finally
        {
            int failed = snapshot.Restore();
            if (failed != 0)
                suffix = failed < 0
                    ? "WARNING: your original clipboard could not be restored."
                    : "Note: some clipboard formats could not be restored.";
            if (suffix.Length > 0) LastWarning = suffix;
        }
    }

    /// <summary>Set when the clipboard could not be fully restored. The tray reads and clears it.</summary>
    public string? LastWarning { get; set; }

    private static async Task<string?> TryGetTextAsync()
    {
        for (int i = 0; i < 10; i++)
        {
            try { return Clipboard.ContainsText() ? Clipboard.GetText(TextDataFormat.UnicodeText) : null; }
            catch (ExternalException) { await Task.Delay(50); }
        }
        return null;
    }

    private static async Task<bool> WaitForClipboardChangeAsync(uint before, int timeoutMs)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            if (GetClipboardSequenceNumber() != before) return true;
            await Task.Delay(20);
            waited += 20;
        }
        return GetClipboardSequenceNumber() != before;
    }

    private static async Task<bool> WaitForKeysReleasedAsync(Keys hotkeyKey, int timeoutMs)
    {
        int waited = 0;
        while (waited < timeoutMs)
        {
            if (!IsDown(VK_CONTROL) && !IsDown(VK_MENU) && !IsDown(VK_SHIFT) && !IsDown(VK_LWIN) && !IsDown(VK_RWIN)
                && !IsDown((int)hotkeyKey))
            {
                await Task.Delay(30); // small settle time
                return true;
            }
            await Task.Delay(20);
            waited += 20;
        }
        return false;
    }

    private static bool IsDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    // ---- Win32 plumbing -------------------------------------------------------------

    private const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    private const ushort VK_C = 0x43, VK_V = 0x56;
    private const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public UIntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    private static INPUT Key(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, dwFlags = up ? KEYEVENTF_KEYUP : 0 } }
    };

    private static void SendCtrlKey(ushort vk)
    {
        var inputs = new[] { Key(VK_CONTROL, false), Key(vk, false), Key(vk, true), Key(VK_CONTROL, true) };
        uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length)
            throw new InvalidOperationException("Windows blocked the simulated keystrokes (is the target app running as administrator?).");
    }
}

/// <summary>In-memory copy of the clipboard so it can be put back after we use it.</summary>
internal sealed class ClipboardSnapshot
{
    private readonly List<(string Format, object Data)> _items = new();
    private bool _wasEmpty;
    private int _notCaptured;

    /// <summary>Returns null if the clipboard could not be read at all.</summary>
    public static ClipboardSnapshot? Capture()
    {
        System.Windows.Forms.IDataObject? data = null;
        for (int i = 0; ; i++)
        {
            try { data = Clipboard.GetDataObject(); break; }
            catch (ExternalException)
            {
                if (i >= 9) return null;
                Thread.Sleep(30);
            }
        }

        var snap = new ClipboardSnapshot();
        string[] formats = data?.GetFormats(false) ?? Array.Empty<string>();
        if (data == null || formats.Length == 0) { snap._wasEmpty = true; return snap; }

        foreach (string f in formats)
        {
            try
            {
                object? o = data.GetData(f, false);
                if (o is MemoryStream ms) o = new MemoryStream(ms.ToArray());
                if (o != null) snap._items.Add((f, o)); else snap._notCaptured++;
            }
            catch { snap._notCaptured++; }
        }
        return snap;
    }

    // Windows (Win+V history, cloud clipboard) and clipboard managers skip clipboard content
    // that carries this format, so KeyFlip's temporary writes do not show up there.
    private const string ExcludeFromHistoryFormat = "ExcludeClipboardContentFromMonitorProcessing";

    public static void ExcludeFromHistory(DataObject obj) =>
        obj.SetData(ExcludeFromHistoryFormat, false, new MemoryStream(new byte[4]));

    /// <summary>0 = fully restored, &gt;0 = number of formats lost, -1 = restore failed entirely.</summary>
    public int Restore()
    {
        try
        {
            if (_wasEmpty) { Clipboard.Clear(); return 0; }
            var obj = new DataObject();
            int failed = _notCaptured;
            foreach (var (format, data) in _items)
            {
                try { obj.SetData(format, false, data); } catch { failed++; }
            }
            // The original content is already in the history from when the user copied it.
            ExcludeFromHistory(obj);
            Clipboard.SetDataObject(obj, true, 10, 50);
            return failed;
        }
        catch { return -1; }
    }
}
