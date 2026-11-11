# Task state

## Objective
Copilot key launches a chosen AI (Claude/ChatGPT/Gemini/Kimi); Ctrl/Shift/long-hold shows a small popup picker of installed/installable AI apps. Single installer, Microsoft Store candidate.

## Plan
1. Key-capture probe app. DONE, see docs/probe-findings.md
2. Default-AI launcher + 3. popup picker: written in src/AiKeyLauncher, on PR #1, awaiting Actions build and a test on Steven's PC.
3. Popup picker (Ctrl / Shift / long hold).
4. Store packaging (settings, start with Windows, icon, MSIX, submission steps).

## Decisions
- Stack: C# / .NET 8 WinForms for probe and launcher (simpler tray + popup than WinUI 3; MSIX-packageable for the Store). Builds on GitHub Actions.
- Builds on GitHub Actions windows-latest (no local toolchain; Linux session cannot build Windows targets).
- Built-in "Customize Copilot key" setting cannot see the Claude desktop app (not MSIX). Our own MSIX app can be the registered target and route from there, or a low-level hook can catch Win+Shift+F23 first.

## Next action
Build src/AiKeyLauncher: hook + chord detection, provider registry, settings, tray menu. Hold gesture ON by default (Steven chose to accept the ~0.5 s tap delay).
