using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AiKeyLauncher;

enum KeyGesture { Tap, Ctrl, Shift, Hold }

/// <summary>
/// Low-level keyboard hook that owns the Copilot key (LWin + LShift + F23) and turns it into gestures.
/// Observed on real hardware (docs/probe-findings.md):
///   plain:  Win↓ Shift↓ F23↓ Win↑ Shift↑ F23↑ within ~25 ms
///   ctrl:   same, with Ctrl already down
///   shift:  same, but the user's Shift↓ arrived well before Win↓
///   hold:   same chord, then F23 auto-repeats alone from ~450 ms later until release
/// F23 is swallowed so Windows never opens Copilot. Must be created on a thread with a message loop.
/// </summary>
sealed class KeyHook : IDisposable
{
    const int WH_KEYBOARD_LL = 13;
    const int WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;
    const int VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LWIN = 0x5B, VK_RWIN = 0x5C, VK_CONTROL = 0x11, VK_F23 = 0x86;
    const uint LLKHF_INJECTED = 0x10;
    const int ShiftLeadMs = 100;   // user-held Shift precedes Win by more than this; the key's own Shift follows Win

    delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public UIntPtr dwExtraInfo; }

    [DllImport("user32.dll", SetLastError = true)] static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? lpModuleName);

    readonly LowLevelKeyboardProc _proc;
    IntPtr _hook;
    readonly SynchronizationContext _ui;
    readonly System.Windows.Forms.Timer _tapTimer = new();

    public Func<bool> HoldEnabled { get; set; } = () => false;
    public Func<int> HoldDelayMs { get; set; } = () => 600;
    public event Action<KeyGesture>? Gesture;

    long _winDownAt = -1, _shiftDownAt = -1, _chordAt;
    bool _f23Down, _chordPending, _holdFired;
    const int HoldRepeatMs = 150;   // F23 arriving later than this after the chord can only be a hold

    public bool IsActive => _hook != IntPtr.Zero;

    public KeyHook()
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _proc = Callback;
        Reinstall();
        _tapTimer.Tick += (_, _) => { _tapTimer.Stop(); if (_chordPending) { _chordPending = false; Fire(KeyGesture.Tap); } };
    }

    /// <summary>
    /// (Re)installs the hook. Windows silently drops a low-level hook whose callback stalls past the
    /// LowLevelHooksTimeout, so the tray app calls this periodically as a self-heal.
    /// </summary>
    public void Reinstall()
    {
        if (_hook != IntPtr.Zero) { UnhookWindowsHookEx(_hook); _hook = IntPtr.Zero; }
        using var mod = Process.GetCurrentProcess().MainModule;
        _hook = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(mod?.ModuleName), 0);
        if (_hook == IntPtr.Zero) Log.Write($"hook: install FAILED, win32 error {Marshal.GetLastWin32Error()}");
    }

    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
    static long Now => Environment.TickCount64;

    void Fire(KeyGesture g)
    {
        Log.Write($"gesture: {g}");
        _ui.Post(_ => { try { Gesture?.Invoke(g); } catch (Exception ex) { Log.Error($"gesture {g}", ex); } }, null);
    }

    IntPtr Callback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0) return CallNextHookEx(_hook, nCode, wParam, lParam);
        var k = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
        if ((k.flags & LLKHF_INJECTED) != 0) return CallNextHookEx(_hook, nCode, wParam, lParam); // not from the keyboard
        int msg = (int)wParam;
        bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
        bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;

        switch (k.vkCode)
        {
            case VK_LWIN or VK_RWIN:
                if (down && _winDownAt < 0) _winDownAt = Now;
                if (up) _winDownAt = -1;
                break;
            case VK_LSHIFT or VK_RSHIFT:
                if (down && _shiftDownAt < 0) _shiftDownAt = Now;
                if (up) _shiftDownAt = -1;
                break;
            case VK_F23:
                return HandleF23(down, up) ? (IntPtr)1 : CallNextHookEx(_hook, nCode, wParam, lParam);
        }
        return CallNextHookEx(_hook, nCode, wParam, lParam);
    }

    /// <returns>true to swallow the event</returns>
    bool HandleF23(bool down, bool up)
    {
        bool chord = _winDownAt >= 0 || Down(VK_LWIN) || Down(VK_RWIN);

        if (down)
        {
            // Any F23 down well after the chord started, while a tap is still undecided, means the key
            // is being held: the firmware re-sends F23 (as repeats or a fresh press) from ~450 ms on.
            if (_chordPending && Now - _chordAt > HoldRepeatMs)
            {
                _tapTimer.Stop(); _chordPending = false; _holdFired = true; _f23Down = true;
                Fire(KeyGesture.Hold);
                return true;
            }
            if (_f23Down) return true; // auto-repeat while we own the key
            _f23Down = true;
            if (chord)
            {
                _tapTimer.Stop();
                _chordPending = false; _holdFired = false; _chordAt = Now;
                if (Down(VK_CONTROL)) { Fire(KeyGesture.Ctrl); return true; }
                bool userShift = _shiftDownAt >= 0 && _winDownAt >= 0 && _winDownAt - _shiftDownAt > ShiftLeadMs;
                if (userShift) { Fire(KeyGesture.Shift); return true; }
                if (HoldEnabled()) { _chordPending = true; _tapTimer.Interval = Math.Max(300, HoldDelayMs()); _tapTimer.Start(); }
                else Fire(KeyGesture.Tap);
                return true;
            }
            if (_chordPending || _holdFired) return true;
            _f23Down = false;
            return false; // some other F23 source: leave it alone
        }
        if (up)
        {
            bool owned = _f23Down || _chordPending || _holdFired;
            _f23Down = false; _holdFired = false;
            return owned;
        }
        return false;
    }

    public void Dispose()
    {
        _tapTimer.Dispose();
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
    }
}
