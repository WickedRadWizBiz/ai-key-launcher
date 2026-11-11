using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace KeyProbe;

static class Program
{
    [STAThread]
    static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new ProbeForm());
    }
}

/// <summary>
/// Diagnostic window: installs a low-level keyboard hook and logs what the Copilot key
/// (expected: Left Win + Left Shift + F23) actually sends on this PC, including modifier
/// state and hold duration. Only modifier keys, F13-F24 and unusual virtual keys are logged
/// by default, so normal typing is not recorded.
/// </summary>
sealed class ProbeForm : Form
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const uint LLKHF_EXTENDED = 0x01, LLKHF_INJECTED = 0x10, LLKHF_ALTDOWN = 0x20, LLKHF_UP = 0x80;
    const int VK_F23 = 0x86;

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT
    {
        public uint vkCode;
        public uint scanCode;
        public uint flags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? lpModuleName);

    readonly LowLevelKeyboardProc _proc; // kept alive so the GC never collects the hook callback
    IntPtr _hook = IntPtr.Zero;

    readonly TextBox _log = new();
    readonly CheckBox _block = new();
    readonly CheckBox _all = new();
    readonly Label _status = new();
    readonly NotifyIcon _tray = new();
    readonly string _logPath;

    readonly Stopwatch _f23Hold = new();
    bool _f23Down;

    public ProbeForm()
    {
        _proc = HookCallback;
        _logPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "KeyProbe", "keyprobe.log");
        Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);

        Text = "Key Probe 0.1 — press the Copilot key";
        Width = 760; Height = 520;
        StartPosition = FormStartPosition.CenterScreen;

        _log.Multiline = true; _log.ReadOnly = true; _log.ScrollBars = ScrollBars.Vertical;
        _log.Font = new Font("Consolas", 9.5f); _log.Dock = DockStyle.Fill; _log.WordWrap = false;

        _block.Text = "Block the Copilot key from reaching Windows (tests whether we can stop Copilot opening)";
        _block.AutoSize = true;
        _all.Text = "Log every key for the next 15 seconds (use only if the key shows nothing)";
        _all.AutoSize = true;
        _all.CheckedChanged += (_, _) => { if (_all.Checked) { _allUntil = DateTime.Now.AddSeconds(15); Log("--- logging ALL keys for 15 s ---"); } };

        var copy = new Button { Text = "Copy log", AutoSize = true };
        copy.Click += (_, _) => { if (_log.Text.Length > 0) Clipboard.SetText(_log.Text); };
        var clear = new Button { Text = "Clear", AutoSize = true };
        clear.Click += (_, _) => _log.Clear();
        var open = new Button { Text = "Open log file", AutoSize = true };
        open.Click += (_, _) => Process.Start(new ProcessStartInfo(_logPath) { UseShellExecute = true });

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.AddRange([copy, clear, open]);
        var options = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, Padding = new Padding(6) };
        options.Controls.AddRange([_block, _all]);
        _status.Dock = DockStyle.Bottom; _status.AutoSize = false; _status.Height = 24; _status.Padding = new Padding(6, 4, 6, 0);

        Controls.Add(_log);
        Controls.Add(options);
        Controls.Add(buttons);
        Controls.Add(_status);

        _tray.Icon = SystemIcons.Application; _tray.Text = "Key Probe"; _tray.Visible = true;
        _tray.DoubleClick += (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); };
        var menu = new ContextMenuStrip();
        menu.Items.Add("Show", null, (_, _) => { Show(); WindowState = FormWindowState.Normal; Activate(); });
        menu.Items.Add("Exit", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;

        Load += (_, _) => InstallHook();
        FormClosing += (_, e) =>
        {
            if (!_exiting) { e.Cancel = true; Hide(); _tray.ShowBalloonTip(1500, "Key Probe", "Still running in the tray. Right-click the icon to exit.", ToolTipIcon.Info); return; }
            if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
            _tray.Visible = false;
        };
    }

    bool _exiting;
    DateTime _allUntil = DateTime.MinValue;

    void InstallHook()
    {
        using var mod = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod?.ModuleName), 0);
        if (_hook == IntPtr.Zero)
        {
            var err = Marshal.GetLastWin32Error();
            _status.Text = $"Hook FAILED (Win32 error {err}).";
            Log($"SetWindowsHookEx failed, error {err}");
            return;
        }
        _status.Text = $"Hook active. Log file: {_logPath}";
        Log($"=== Key Probe started {DateTime.Now:yyyy-MM-dd HH:mm:ss} on {Environment.OSVersion} ===");
        Log("Press the Copilot key: plain, then with Ctrl, with Shift, and hold it for ~1 s.");
    }

    IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);

        var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        int msg = (int)wParam;
        bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
        bool interesting = _all.Checked && DateTime.Now < _allUntil || IsInteresting(k.vkCode);
        bool block = false;

        if (k.vkCode == VK_F23)
        {
            if (!up && !_f23Down) { _f23Down = true; _f23Hold.Restart(); }
            else if (up && _f23Down) { _f23Down = false; _f23Hold.Stop(); }
            block = _block.Checked;
        }

        if (interesting)
        {
            var sb = new StringBuilder();
            sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(up ? "  UP   " : "  DOWN ");
            sb.Append($"vk=0x{k.vkCode:X2} ({(Keys)k.vkCode,-10}) sc=0x{k.scanCode:X2}");
            sb.Append(" flags=[");
            if ((k.flags & LLKHF_EXTENDED) != 0) sb.Append("ext ");
            if ((k.flags & LLKHF_INJECTED) != 0) sb.Append("INJECTED ");
            if ((k.flags & LLKHF_ALTDOWN) != 0) sb.Append("alt ");
            sb.Append("] mods=").Append(Mods());
            if (k.vkCode == VK_F23 && up) sb.Append($"  HELD {_f23Hold.ElapsedMilliseconds} ms");
            if (block) sb.Append("  (BLOCKED)");
            Log(sb.ToString());
        }

        return block ? (IntPtr)1 : CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    static bool IsInteresting(uint vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0x5B or 0x5C or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 // shift/ctrl/alt/win
        || (vk >= 0x7C && vk <= 0x87)   // F13..F24
        || vk >= 0xA6;                  // browser/media/OEM/unassigned keys

    static string Mods()
    {
        static bool D(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
        var s = new StringBuilder();
        if (D(0xA2)) s.Append("LCtrl ");
        if (D(0xA3)) s.Append("RCtrl ");
        if (D(0xA0)) s.Append("LShift ");
        if (D(0xA1)) s.Append("RShift ");
        if (D(0xA4)) s.Append("LAlt ");
        if (D(0xA5)) s.Append("RAlt ");
        if (D(0x5B)) s.Append("LWin ");
        if (D(0x5C)) s.Append("RWin ");
        return s.Length == 0 ? "none" : s.ToString().TrimEnd();
    }

    void Log(string line)
    {
        try { File.AppendAllText(_logPath, line + Environment.NewLine); } catch { /* best effort */ }
        if (InvokeRequired) { BeginInvoke(() => Log(line)); return; }
        _log.AppendText(line + Environment.NewLine);
    }
}
