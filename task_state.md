# Task state

## Objective
Copilot key launches a chosen AI (Claude/ChatGPT/Gemini/Kimi); Ctrl/Shift/long-hold shows a small popup picker of installed/installable AI apps. Single installer, Microsoft Store candidate.

## Plan
1. Key-capture probe app, run on Steven's PC (Windows 11 Home 26H2, build 26300). IN PROGRESS
2. Default-AI launcher (desktop app if installed, else website in default browser).
3. Popup picker (Ctrl / Shift / long hold).
4. Store packaging (settings, start with Windows, icon, MSIX, submission steps).

## Decisions
- Stack: C# / .NET 8. Probe uses WinForms + WH_KEYBOARD_LL. Final app: WinUI 3 + MSIX.
- Builds on GitHub Actions windows-latest (no local toolchain; Linux session cannot build Windows targets).
- Built-in "Customize Copilot key" setting cannot see the Claude desktop app (not MSIX). Our own MSIX app can be the registered target and route from there, or a low-level hook can catch Win+Shift+F23 first.

## Next action
Get KeyProbe.exe from the Actions artifact onto Steven's PC, run it, press the Copilot key plain / with Ctrl / with Shift / held, and record the log.
