# Copilot key probe findings (2026-11-11)

Machine: Windows 11 Home 26H2, build 26300. KeyProbe 0.1 (WH_KEYBOARD_LL).

| Gesture | What arrives |
|---|---|
| Plain press | LWin ↓, LShift ↓, F23 (vk 0x86, sc 0x6E) ↓, then LWin ↑, LShift ↑, F23 ↑ within ~25 ms. A duplicate set of ↑ events often follows. |
| Ctrl + key | Ctrl is held (auto-repeating) and still down when F23 ↓ arrives. |
| Shift + key | Identical chord. Only difference: the user's LShift ↓ came ~340 ms before LWin ↓, versus ~7 ms after LWin ↓ for a plain press. |
| Hold | Chord fires and releases in ~28 ms, then F23 alone auto-repeats from ~450 ms later until release (1084 ms logged). |
| Block in hook | Returning 1 for F23 stops it reaching Windows. |

## Consequences

- A tray app with a low-level keyboard hook is sufficient to take over the key; no MSIX registration is needed for interception.
- Ctrl is a clean modifier. Shift needs a timing heuristic (Shift down > ~100 ms before Win down means the user is holding it).
- A hold can be recognised only ~0.5 s after the chord releases, so supporting it delays plain taps by that much. Default: hold off, toggle in settings.
- Swallow the whole chord (Win, Shift, F23) rather than F23 alone, so a stray Win tap can't open Start.
