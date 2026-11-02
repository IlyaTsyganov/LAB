# LAB 1.0.2

Ports the LAB-tab improvements from LAB Plus 0.8.21 into the standalone LAB application.

- Includes all 22 exact executable profiles, the missing base 6.66 metrics profile, and audited native player/HUD/velocity chains for 2.0 through 5.0 and base 6.66.
- Restricts active features to the exact process launched through LAB, identified by PID, creation time and executable path. External launches before or after LAB remain inactive; a late launch record is recognized without a race. No restart-notice hook is attached to external processes.
- Extends **Hide AMD driver line** to actual CPU/GPU labels from any manufacturer, retaining arbitrary DESRU text. LAB precedes the original driver row, preserving its font and formatting arguments.
- Displays the **LAB (MR)** indicator. Cheat cvars and noclip/notarget/god rows are not collected or rendered by LAB.
- Preserves LAB.exe, `lab_` commands, LAB preferences and independent runtime identifiers. LAB Plus remains a separate application.
- Applies the existing early-build NVIDIA Lighting Fix policy before configuring a child Vulkan layer. Missing or unsupported executable paths keep lighting disabled without breaking application startup.

Extract the complete ZIP and open **LAB/LAB.exe**. Requires .NET 10 Desktop Runtime x64 and WebView2 Runtime. Close the old LAB instance and fully restart the game through this build to replace the older metrics adapter.

Validation: 315 managed checks with all 12 installed executable layouts passed, including executed x64 metrics, HUD, tint and Modbot fixtures. The metrics regressions seed old cvar/player-cheat text and verify that it is never drawn in standalone or DESRU mode. Native lighting ownership, shutdown and PID-reuse tests and four lighting math checks passed. The runtime package is validated against its explicit file list. No retail game was launched. Live visual/gameplay acceptance remains a separate user check.
