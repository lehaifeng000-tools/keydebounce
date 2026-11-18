# KeyDebounce

[简体中文](README.md)

KeyDebounce is a Windows utility for mechanical keyboard chatter. If one tap produces two letters, spaces, or punctuation marks, it can filter the extra press that arrives immediately after you release the key.

**[Download the Windows installer from Releases](https://github.com/lehaifeng000-tools/keydebounce/releases/latest)** (select `KeyDebounceSetup.msi` under **Assets**)

Tested on 64-bit Windows 11.

## Why I built it

My wired CHERRY MX 8.2 with Silver switches worked fine when I first got it. Over time, `R`, Space, and comma started registering twice from a single tap. It became especially noticeable when typing Chinese with a Pinyin input method. I would tap `R` once to start typing a phrase, and the input method would receive two `r` characters.

I built a software filter so I could keep using the keyboard. It passes the first press through immediately and blocks another press of the same key if it arrives within a short interval after release. It works without installing a keyboard driver, flashing firmware, or changing the Windows keyboard settings.

## Install and use

1. Download and double-click `KeyDebounceSetup.msi`. **Start KeyDebounce when I sign in to Windows** is selected by default. Clear it if you prefer to start the app yourself, then click **Install**.
2. Open **KeyDebounce** from the Start menu. While the app is running, you can also double-click the keycap icon in the notification area to open its Chinese-language settings.
3. Start with the default **30 ms** window. Try the problem keys in Notepad or in your usual input method. The **拦截次数（按键）** panel shows which keys the app has blocked during this run.
4. Increase the window in small steps if extra presses still get through. Shorten it if intentional rapid taps are blocked. The setting ranges from **5 to 120 ms**.

The default filter covers **A–Z, Space, the top-row digits 0–9, and common punctuation** such as comma, period, semicolon, and quotes. Select **全部按键** (All keys) to extend coverage to keys such as Backspace, the arrow keys, and the number pad. This option is off by default. The app saves your key groups and debounce window and restores them the next time it starts. You can also change the sign-in startup option in the app.

Closing the settings window keeps KeyDebounce running in the notification area. Use the tray menu to pause filtering or quit. To uninstall, select **卸载…** (Uninstall) in the app, or run the installer for the same version again and select **Remove**. Uninstalling removes the startup entry and preserves your saved filter settings.

## Scope

- Filtering applies to all connected keyboards in the current Windows sign-in session. Holding down a key still produces its normal Windows auto-repeat.
- The filter uses the time between releasing and pressing the same key. Very fast intentional taps can fall within the same window. A faulty switch may still need cleaning, repair, or replacement.
- The app runs locally and works offline. It counts blocked presses in memory for the current run and does not record or upload what you type.

## Build from source

Run this command in PowerShell from the repository directory on 64-bit Windows.

```powershell
.\BuildInstaller.ps1
```

The script uses the Windows .NET Framework C# compiler, `makecab.exe`, and Windows Installer to produce `KeyDebounce.exe` and `KeyDebounceSetup.msi` in the repository root.
