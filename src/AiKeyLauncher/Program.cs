namespace AiKeyLauncher;

static class Program
{
    [STAThread]
    static void Main()
    {
        using var single = new Mutex(true, @"Local\AiKeyLauncher", out bool first);
        if (!first) return; // already running in the tray

        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp());
    }
}

/// <summary>Tray icon, settings menu, and the glue from key gestures to launching.</summary>
sealed class TrayApp : ApplicationContext
{
    readonly Settings _settings = Settings.Load();
    readonly NotifyIcon _tray = new();
    readonly KeyHook _hook;
    PickerForm? _picker;

    public TrayApp()
    {
        _hook = new KeyHook
        {
            HoldEnabled = () => _settings.HoldOpensPicker,
            HoldDelayMs = () => _settings.HoldDelayMs,
        };
        _hook.Gesture += OnGesture;

        _tray.Icon = SystemIcons.Application;
        _tray.Visible = true;
        _tray.DoubleClick += (_, _) => Default.Launch(_settings.AlwaysUseWeb);
        _tray.ContextMenuStrip = new ContextMenuStrip();
        _tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        BuildMenu();

        if (!_hook.IsActive)
            _tray.ShowBalloonTip(4000, "AI Key Launcher", "Could not hook the keyboard. The Copilot key will not be captured.", ToolTipIcon.Error);
    }

    AiProvider Default => Providers.Get(_settings.DefaultProvider);

    void OnGesture(KeyGesture g)
    {
        switch (g)
        {
            case KeyGesture.Tap: Default.Launch(_settings.AlwaysUseWeb); break;
            default: ShowPicker(); break;
        }
    }

    void ShowPicker()
    {
        if (_picker is { IsDisposed: false }) { _picker.Close(); return; }
        _picker = new PickerForm(_settings);
        _picker.FormClosed += (_, _) => { _picker = null; BuildMenu(); };
        _picker.Show();
    }

    void BuildMenu()
    {
        var m = _tray.ContextMenuStrip!;
        m.Items.Clear();
        m.Items.Add($"Open {Default.Name}", null, (_, _) => Default.Launch(_settings.AlwaysUseWeb));
        m.Items.Add("Choose…", null, (_, _) => ShowPicker());
        m.Items.Add(new ToolStripSeparator());

        var def = new ToolStripMenuItem("Default AI");
        foreach (var p in Providers.All)
        {
            var item = new ToolStripMenuItem(p.Name + (p.IsInstalled ? "" : "  (web)")) { Checked = p.Id == _settings.DefaultProvider };
            item.Click += (_, _) => { _settings.DefaultProvider = p.Id; _settings.Save(); BuildMenu(); };
            def.DropDownItems.Add(item);
        }
        m.Items.Add(def);

        var hold = new ToolStripMenuItem("Hold key to choose (adds a short delay to taps)") { Checked = _settings.HoldOpensPicker };
        hold.Click += (_, _) => { _settings.HoldOpensPicker = !_settings.HoldOpensPicker; _settings.Save(); BuildMenu(); };
        m.Items.Add(hold);

        var web = new ToolStripMenuItem("Always open the website") { Checked = _settings.AlwaysUseWeb };
        web.Click += (_, _) => { _settings.AlwaysUseWeb = !_settings.AlwaysUseWeb; _settings.Save(); BuildMenu(); };
        m.Items.Add(web);

        var startup = new ToolStripMenuItem("Start with Windows") { Checked = Settings.StartsWithWindows };
        startup.Click += (_, _) => { Settings.StartsWithWindows = !Settings.StartsWithWindows; BuildMenu(); };
        m.Items.Add(startup);

        m.Items.Add(new ToolStripSeparator());
        m.Items.Add("Exit", null, (_, _) => { _hook.Dispose(); _tray.Visible = false; ExitThread(); });
        _tray.Text = $"AI Key Launcher — Copilot key opens {Default.Name}";
    }
}
