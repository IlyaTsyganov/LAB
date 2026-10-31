# LAB 1.0

- Compact 820×800 interface with all main controls visible without scrolling, slightly larger text and fields, and Help tabs for indicators and console commands.
- In-game LAB indicator in SLMHR order, standalone FPS-only metrics, and coexistence with DESRU. Feature availability is shown separately in Game options.
- Three independently positioned, sized and colored speedometers. Vertical speed supports signed +up/-down and unsigned modes through the UI and lab_speedometer_verticalsign 0|1.
- Logs now use logs/lab.log beside lab.exe. Preferences remain in the existing per-user settings location and survive restarts and updates.
- First-run defaults: speedometer master off; all individual meters on; vertical sign on; all meter sizes 3; red screen opacity 100%.
- The first run checks C:\Program Files (x86)\Steam\steamapps\common\DOOMEternal\DOOMEternalx64vk.exe. An absent executable leaves the path empty for manual selection.
- Closing LAB disables its live features. Lighting Fix retains its supported early-build/NVIDIA restriction, and Modbot retains its internal-FPS-limit condition.
- Windows x64 ZIP checked against an explicit file list. It contains the application, required assets and component licenses; README.md, CREDITS.md, user settings, populated logs, debug symbols and developer files are excluded.

Requirements: Windows x64, .NET 10 Desktop Runtime and Microsoft Edge WebView2 Runtime. Extract the entire program archive before running lab.exe.
