using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AiKeyLauncher;

/// <summary>Small popup near the cursor listing every AI: open it, or get the app if it isn't installed.</summary>
sealed class PickerForm : Form
{
    public PickerForm(Settings settings)
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(32, 32, 32);
        ForeColor = Color.White;
        Padding = new Padding(8);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        KeyPreview = true;

        var table = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Dock = DockStyle.Fill };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var title = new Label { Text = "Open with…", AutoSize = true, Font = new Font(Font, FontStyle.Bold), Margin = new Padding(4, 2, 4, 6) };
        table.Controls.Add(title, 0, 0);
        table.SetColumnSpan(title, 3);

        int row = 1;
        foreach (var p in Providers.All)
        {
            bool installed = p.IsInstalled;
            bool isDefault = p.Id == settings.DefaultProvider;

            var open = Button(installed ? $"{p.Name}" : $"{p.Name} (web)");
            if (isDefault) open.Font = new Font(open.Font, FontStyle.Bold);
            open.Click += (_, _) => { Close(); p.Launch(settings.AlwaysUseWeb); };
            table.Controls.Add(open, 0, row);

            if (installed)
            {
                var web = Button("Web"); web.Click += (_, _) => { Close(); p.Launch(forceWeb: true); };
                table.Controls.Add(web, 1, row);
            }
            else if (p.InstallUrl != p.WebUrl)
            {
                var get = Button("Get app"); get.ForeColor = Color.FromArgb(120, 190, 255);
                get.Click += (_, _) => { Close(); Process.Start(new ProcessStartInfo(p.InstallUrl) { UseShellExecute = true }); };
                table.Controls.Add(get, 1, row);
            }

            var def = Button(isDefault ? "Default" : "Make default");
            def.Enabled = !isDefault;
            def.Click += (_, _) => { settings.DefaultProvider = p.Id; settings.Save(); Close(); };
            table.Controls.Add(def, 2, row);
            row++;
        }
        Controls.Add(table);

        Deactivate += (_, _) => Close();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape) Close(); };
        Shown += (_, _) => { ForceForeground(); InstallMouseHook(); };
        FormClosed += (_, _) => RemoveMouseHook();
    }

    // A tray app has no foreground window, so Windows refuses plain Activate(). Sending a synthetic
    // Alt tap first makes this process the last to receive input, which unlocks SetForegroundWindow.
    // (The keyboard hook ignores injected events, so this never looks like a gesture.)
    void ForceForeground()
    {
        try
        {
            var alt = new INPUT[]
            {
                new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0x12 } } },
                new() { type = 1, u = new InputUnion { ki = new KEYBDINPUT { wVk = 0x12, dwFlags = 2 } } },
            };
            SendInput((uint)alt.Length, alt, Marshal.SizeOf<INPUT>());
            SetForegroundWindow(Handle);
            Activate();
        }
        catch (Exception ex) { Log.Error("foreground", ex); }
    }

    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public KEYBDINPUT ki; [FieldOffset(0)] public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] struct INPUT { public uint type; public InputUnion u; }
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    // Belt and braces: a click anywhere outside the popup closes it even if activation was refused.
    const int WH_MOUSE_LL = 14, WM_LBUTTONDOWN = 0x0201, WM_RBUTTONDOWN = 0x0204, WM_MBUTTONDOWN = 0x0207;
    delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
    LowLevelMouseProc? _mouseProc;
    IntPtr _mouseHook;

    void InstallMouseHook()
    {
        _mouseProc = (nCode, wParam, lParam) =>
        {
            if (nCode >= 0)
            {
                int msg = (int)wParam;
                if (msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_MBUTTONDOWN && !Bounds.Contains(Cursor.Position))
                    BeginInvoke(Close);
            }
            return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
        };
        using var mod = Process.GetCurrentProcess().MainModule;
        _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, GetModuleHandle(mod?.ModuleName), 0);
    }

    void RemoveMouseHook()
    {
        if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
        _mouseProc = null;
    }

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? lpModuleName);

    static Button Button(string text) => new()
    {
        Text = text, AutoSize = true, FlatStyle = FlatStyle.Flat, Margin = new Padding(2),
        BackColor = Color.FromArgb(48, 48, 48), ForeColor = Color.White, MinimumSize = new Size(0, 30),
        FlatAppearance = { BorderColor = Color.FromArgb(70, 70, 70) },
    };

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        var pt = Cursor.Position;
        var area = Screen.FromPoint(pt).WorkingArea;
        Location = new Point(
            Math.Min(Math.Max(pt.X - Width / 2, area.Left), area.Right - Width),
            Math.Min(Math.Max(pt.Y - 20, area.Top), area.Bottom - Height));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var pen = new Pen(Color.FromArgb(90, 90, 90));
        e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
    }
}
