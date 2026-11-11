# AI Key Launcher

Makes the Windows Copilot key open the AI assistant you choose (Claude, ChatGPT, Gemini or Kimi) instead of Copilot. Holding the key, or pressing it with Ctrl or Shift, opens a small picker listing the AI apps you have installed and the ones you can install.

## Status

Step 1: `probe/` is a small diagnostic tray app that logs what the Copilot key actually sends on a given PC (virtual key, scan code, modifiers, hold time) and tests whether the key can be blocked from reaching Windows. See `task_state.md` for the current plan.

## Building

Builds run on GitHub Actions (`.github/workflows/build-probe.yml`). Each run uploads a self-contained `KeyProbe.exe` as a workflow artifact. Nothing needs to be installed on the PC to run it.
