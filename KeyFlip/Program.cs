using System.Drawing;
using System.Windows.Forms;

namespace KeyFlip;

internal static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        // Only one copy at a time (two copies would fight over the same shortcut).
        using var mutex = new Mutex(true, @"Local\KeyFlip.SingleInstance", out bool isNew);
        if (!isNew)
        {
            MessageBox.Show("KeyFlip is already running. Look for its icon near the clock.", "KeyFlip");
            return;
        }

        Application.Run(new TrayContext(startedAtSignIn: args.Contains(StartupManager.StartupArgument)));
    }
}

internal sealed class TrayContext : ApplicationContext
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly NotifyIcon _tray;
    private readonly ToolStripMenuItem _enableItem;
    private readonly HotkeyManager _hotkey = new();
    private readonly SelectionConverter _converter;
    private bool _settingsOpen;

    public TrayContext(bool startedAtSignIn)
    {
        _converter = new SelectionConverter { SwitchLayout = _settings.SwitchLayout };
        try { StartupManager.RefreshEntry(); } catch { /* keep the old entry */ }

        _enableItem = new ToolStripMenuItem("Enabled") { Checked = _settings.Enabled, CheckOnClick = true };
        _enableItem.CheckedChanged += (_, _) =>
        {
            _settings.Enabled = _enableItem.Checked;
            _settings.Save();
            ApplyEnabledState();
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add(_enableItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _tray = new NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "KeyFlip",
            ContextMenuStrip = menu,
            Visible = true
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        _hotkey.HotkeyPressed += OnHotkey;
        // No "Ready" popup at every sign-in; problems are still reported.
        ApplyEnabledState(announce: !startedAtSignIn);
    }

    // Uses the icon embedded in KeyFlip.exe; falls back to a generic one if that fails.
    private static Icon LoadTrayIcon()
    {
        try
        {
            string? path = Environment.ProcessPath;
            if (path != null)
            {
                var icon = Icon.ExtractAssociatedIcon(path);
                if (icon != null) return icon;
            }
        }
        catch { }
        return SystemIcons.Application;
    }

    private string ShortcutText => HotkeyFormat.Describe(_settings.Mods, _settings.KeyCode);

    private void ApplyEnabledState(bool announce = true)
    {
        if (_settingsOpen) return; // the shortcut is off while Settings is open; OpenSettings re-applies it
        if (_enableItem.Checked)
        {
            if (!_hotkey.Register(_settings.Mods, _settings.KeyCode, out string error))
                NotifyShortcutProblem(error);
            else if (announce)
                Notify("KeyFlip", "Ready. Shortcut: " + ShortcutText, ToolTipIcon.Info);
        }
        else
        {
            _hotkey.Unregister();
            if (announce) Notify("KeyFlip", "Disabled", ToolTipIcon.Info);
        }
    }

    private void NotifyShortcutProblem(string error)
        => Notify("KeyFlip - shortcut problem", error + " Open Settings to choose another shortcut.", ToolTipIcon.Error);

    private async void OnHotkey()
    {
        var result = await _converter.RunAsync(_settings.KeyCode);
        string message = result.Message;
        if (_converter.LastWarning != null)
        {
            message += " " + _converter.LastWarning;
            _converter.LastWarning = null;
        }
        Notify(result.Title, message, result.Success ? ToolTipIcon.Info : ToolTipIcon.Warning);
    }

    private void OpenSettings()
    {
        if (_settingsOpen) return;
        _settingsOpen = true;
        // Turn the shortcut off while the dialog is open: pressing it there should be captured
        // as a key combination, not convert text inside the dialog.
        _hotkey.Unregister();
        try
        {
            bool startupWasOn = StartupManager.IsEnabled();
            using var form = new SettingsForm(_settings.Mods, _settings.KeyCode, startupWasOn, _settings.SwitchLayout);
            if (form.ShowDialog() != DialogResult.OK) return;

            // 1. Start with Windows (only changes when the user ticks/unticks the box).
            if (form.RunAtStartup != startupWasOn)
            {
                try { StartupManager.Set(form.RunAtStartup); }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not change the startup setting: " + ex.Message, "KeyFlip");
                }
            }

            // 2. Keyboard layout switching.
            if (form.SwitchLayout != _settings.SwitchLayout)
            {
                _settings.SwitchLayout = form.SwitchLayout;
                _converter.SwitchLayout = form.SwitchLayout;
                _settings.Save();
            }

            // 3. Shortcut.
            bool changed = form.SelectedModifiers != _settings.Mods || form.SelectedKey != _settings.KeyCode;
            if (changed)
            {
                if (!_enableItem.Checked)
                {
                    SaveShortcut(form.SelectedModifiers, form.SelectedKey);
                }
                else if (_hotkey.Register(form.SelectedModifiers, form.SelectedKey, out string error))
                {
                    SaveShortcut(form.SelectedModifiers, form.SelectedKey);
                    Notify("KeyFlip", "New shortcut: " + ShortcutText, ToolTipIcon.Info);
                }
                else
                {
                    MessageBox.Show(error + "\n\nThe previous shortcut was kept.", "KeyFlip");
                }
            }
        }
        finally
        {
            _settingsOpen = false;
            // Put the saved shortcut back unless a new one was registered above.
            if (_enableItem.Checked && !_hotkey.IsRegistered
                && !_hotkey.Register(_settings.Mods, _settings.KeyCode, out string error))
                NotifyShortcutProblem(error);
        }
    }

    private void SaveShortcut(HotkeyModifiers mods, Keys key)
    {
        _settings.Modifiers = (int)mods;
        _settings.Key = (int)key;
        _settings.Save();
    }

    private void Notify(string title, string text, ToolTipIcon icon)
        => _tray.ShowBalloonTip(2000, title, text, icon);

    private void ExitApp()
    {
        _hotkey.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        Application.Exit();
    }
}
