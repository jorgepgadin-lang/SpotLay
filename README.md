# SpotLay

**English** · [Español](README.es.md)

**See how many frames your GPU really renders.** With Frame Generation on, the FPS counter shows the total, including generated frames. SpotLay splits it: **real FPS, generated FPS and the Frame Generation multiplier** (x2, x3, x4… and whether NVIDIA's Multi Frame Generation is **fixed or dynamic**), measured live from the game, not estimated.

```
FG  x4 DYNAMIC      FPS 140
Real FPS  35        Generated FPS 105
```

![SpotLay overlay in Cyberpunk 2077: x5 dynamic Frame Generation, 164 FPS of which 33 real and 131 generated](docs/screenshots/overlay.jpg)

SpotLay draws a clean overlay on top of your games with real FPS, generated FPS, the Frame Generation multiplier, 1% / 0.1% lows and any sensor of your PC (temperatures, power, clocks, fans…). It also records every gaming session, can control your fans with custom curves and can warn you — or shut Windows down in an orderly way — if a sensor gets dangerously hot.


## Features

- **Frame Generation, measured, not guessed.** Real (rendered) FPS, generated FPS, total FPS and the multiplier (x2, x3, x4…), including NVIDIA Dynamic Multi Frame Generation (shown as `x4 DYNAMIC` / `FIXED`).
- **Stability.** 1% and 0.1% lows for displayed frames and for real frames, plus an in-game frametime graph.
- **Any sensor.** GPU, CPU, memory, motherboard, drives, network and fans. If a sensor does not exist on your PC, SpotLay shows `—`: it never shows a substitute value.
- **Live overlay editor.** Edit the overlay while you look at it on screen: drag it with the mouse, Ctrl + drag a card to reorder it, change colors, sizes and gauges (number, arc, ring, bars, timeline) and see every change instantly.
- **Layouts and profiles.** 5 global layouts with their own hotkeys and a separate layout per game.
- **Performance history.** Every session is recorded automatically: FPS over time, lows, stutters and temperatures.
- **Fan control.** Curves per fan, identify and calibrate channels, critical temperature, and everything returns to the BIOS when SpotLay closes.
- **Alerts and emergency shutdown.** Per-sensor alerts on top of the game and, if you enable it, an orderly Windows shutdown with a countdown you can cancel.
- **English and Spanish.** Chosen on first start and changeable in Settings.
- **Automatic updates** from this page.


## Screenshots

**Live overlay editor** — the overlay on screen shows every change as you make it.
![Overlay editor](docs/screenshots/overlay-editor.png)

**Performance history** — every session recorded automatically, with stutters marked.
![Performance](docs/screenshots/performance.png)

**Fan control** — curves, calibration and 0 RPM at idle for graphics card fans.
![Cooling](docs/screenshots/cooling.png)

**Home** — 5 overlay layouts, each with its own hotkey.
![Home](docs/screenshots/home.png)

## Requirements

- Windows 11, 64-bit (tested). Windows 10 64-bit should work but has not been tested yet.
- Administrator rights (SpotLay reads frame events and hardware sensors; Windows shows a UAC prompt when it starts).
- For Frame Generation metrics: a game using NVIDIA Reflex (DLSS Frame Generation / Multi Frame Generation). Without Reflex, SpotLay shows total FPS only. Vulkan and OpenGL games show FPS only.
- For motherboard fans and some motherboard sensors: the free [PawnIO](https://pawnio.eu/) driver. GPU sensors work without it.
- Nothing else to install: the .NET runtime is included in `SpotLay.exe`.

## Install

1. Download `SpotLay.exe` from the [latest release](../../releases/latest).
2. Put it in a folder of your choice (for example `C:\Tools\SpotLay`) and run it.
3. Choose your language, and you are ready. Open a game and the overlay appears.

**Windows SmartScreen:** SpotLay is not digitally signed yet, so the first time Windows may say it comes from an unknown publisher. Click **More info → Run anyway**.

## Updates

SpotLay checks this page for a new version when it starts and every few hours. You can turn this off in **Settings → Check for updates automatically**.

- If SpotLay starts with Windows, the update is installed right then, before it starts measuring.
- If SpotLay is open, it tells you a new version is ready and lets you restart SpotLay now or when you close it.
- Every download is checked against its SHA-256 fingerprint before it is installed.

## Default keys

| Action | Key |
|---|---|
| Show / hide the overlay | Shift + F8 |
| Show / hide the frametime graph | Shift + F9 |
| Switch to layout 1…5 | Ctrl + Alt + 1…5 |
| Back to the game's layout | Ctrl + Alt + 0 |
| Move the overlay in game | Ctrl + Shift + arrows |
| Cancel an emergency shutdown | Ctrl + Alt + F9 |

All keys can be changed in the app.

## Safety notice

Fan control and emergency shutdown act directly on your hardware and on Windows. They are **off by default** and you must accept a notice before enabling them. SpotLay is provided "as is", without warranty of any kind; see [LICENSE](LICENSE.md).

## Privacy

SpotLay has no telemetry and no account. The only internet connection it makes is to this GitHub page to check for updates (and you can turn it off). Settings, logs and session history stay on your PC in `%LOCALAPPDATA%\SpotLay`.

## Uninstall

1. In **Settings**, untick **Start SpotLay with Windows**.
2. Quit SpotLay from the tray icon (fans go back to the BIOS).
3. Delete `SpotLay.exe` and, if you want to remove your settings too, the folder `%LOCALAPPDATA%\SpotLay`.

## FAQ

**The overlay does not appear over my game.** SpotLay cannot draw over *exclusive* fullscreen. Use borderless / windowed fullscreen in the game.

**A sensor shows `—`.** That sensor is not exposed on your PC (by the driver or by the hardware), so there is no data to show.

**Frame Generation shows FIXED at the start of a game and then DYNAMIC.** SpotLay detects dynamic mode when the multiplier changes. Once it has seen it, it keeps showing DYNAMIC until you close the game.

**My motherboard fans are missing.** Install [PawnIO](https://pawnio.eu/) and check that the fan headers are in PWM or DC mode in the BIOS.

## Credits

Made by **Yurival**. SpotLay uses [PresentMon](https://github.com/GameTechDev/PresentMon) and [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor); see [THIRD-PARTY-NOTICES](THIRD-PARTY-NOTICES.md).

Bug reports and ideas: [open an issue](../../issues).
