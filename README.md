# LAB

LAB is a focused DOOM Eternal utility with a UAC Frost interface, the original red emblem and the bundled Eternal typefaces. All application text is English. LAB Plus remains a separate application.

![LAB UAC Frost interface](docs/interface.png)

## Run

Extract the complete archive and open `lab.exe`. Windows x64, .NET 10 Desktop Runtime and Microsoft Edge WebView2 Runtime are required. Choose `DOOMEternalx64vk.exe`, set preferences, then click **Launch game**. LAB can also connect to an already running copy whose full executable path matches the selected path.

Keep LAB open for live HUD updates, console settings, tint control and Q/E protection. Preferences are saved in `%LOCALAPPDATA%\lab\settings.json`, independently of LAB Plus. `LAB_DATA_DIRECTORY` may override this with an absolute directory. Logs are written to `logs/lab.log` next to `lab.exe`. Changing the settings directory does not relocate logs.

## First-run defaults

The overall speedometer switch is off, while Horizontal, Landing and Vertical remain individually enabled. Vertical direction signs are on, every meter uses size 3, and red screen opacity is 100%. Lighting Fix, Q/E + Modbot fix and Hide AMD driver line remain enabled.

On the first run, LAB checks `C:\Program Files (x86)\Steam\steamapps\common\DOOMEternal\DOOMEternalx64vk.exe`. If the file is absent, the path remains empty until the user selects it. Existing preferences, including a manually selected path or disabled direction sign, survive updates and restarts.

## Features

- **Lighting Fix**: Vulkan synchronization correction for the audited early 1.0, 1.1 and 2.0 executables on NVIDIA. Launch the game through LAB to load the layer; the switch can then change its active state. Enabling it for an externally launched game requires a restart through LAB. Other executable/GPU combinations pass through without changing barriers. The layer is enabled only in the launched child's environment; no global Vulkan registry entries or game files are installed. Some Windows Vulkan loaders require an ASCII path for layer manifests. If the Windows profile path contains non-ASCII characters and lighting fails to load, choose an ASCII `LAB_DATA_DIRECTORY`.
- **Horizontal speed**: `sqrt(vx² + vy²)`, in m/s.
- **Landing speed**: horizontal speed at ground contact, retaining the impact peak from the following 8 ms. Attaching while already grounded does not count as landing.
- **Vertical speed**: in m/s with two decimals. By default it shows `+` for ascent and `-` for descent. Disable **Show +/− sign** below Vertical, or use `lab_speedometer_verticalsign 0`, to show `abs(vz)` without a sign. Zero displays as `0.00`. The setting is saved independently of meter visibility.
- **Independent HUD controls**: each meter has its own visibility, X/Y physical-pixel coordinates from the top left, text size (0.1–10) and RGB text color. Size 1 corresponds to the original native HUD scale. The game draws the numeric values itself; there is no separate desktop HUD window. Readings hide outside gameplay, while unfocused/loading, or after the update heartbeat expires. `--` means no valid reading/landing yet.
- **Q/E + Modbot fix**: prevents Q or E held while opening the Modbot from repeatedly changing weapon tabs. Fresh navigation resumes after release and the queued release event drains. This preserves the upstream internal-FPS-cap condition below 1000 Hz. It does not remap keys or add an FPS limiter.
- **Red screen opacity**: 0–100% of the original low-health tint, applied to both identified tint layers. It does not change health, armor or damage.
- **Performance metrics**: while LAB is connected without DESRU, the game displays only FPS and the LAB indicator. With DESRU running, its formatted text is retained; LAB follows it and precedes the AMD driver line when that line is visible. LAB reads DESRU's current strings on the game rendering thread and does not overwrite them. The indicator uses the original small metrics font.
- **Hide AMD driver line**: suppresses that row during coexistence with DESRU. With LAB alone, all metrics text other than FPS and LAB is hidden regardless of this preference.
- **Feature indicator**: `LAB` with enabled feature letters in the fixed order `SLMHR`: Speedometers, Lighting Fix, Modbot protection, Hide AMD driver line, and Red screen opacity other than 100%. Example: `LAB (SLMHR)` or `LAB (MR)`. Letters reflect the selected preferences, independently of native availability. Game options shows actual Lighting Fix, Modbot and tint status separately. For example, enabled Lighting Fix on AMD still contributes `L`, while its status says that an NVIDIA GPU is required; Modbot at an internal limit of 1000 contributes `M` and reports standby.
- **Shutdown**: closing LAB stops its HUD, tint, input protection, metrics filtering and lighting correction. User preferences are retained for the next launch. The lighting layer checks both the LAB owner's PID and creation time; an exited or reused PID cannot keep the fix enabled. Native adapters retain dormant executable allocations until game exit so registered callbacks and in-flight threads can return safely.

Native Modbot attachment retries temporary initialization failures instead of permanently giving up after its first attempt. Unsupported layouts remain disabled.

The UI includes three window presets: Mini (680×580), Compact (820×800, default), and Comfortable (920×860), selected in the footer. **Help** has separate **Indicators** and **Console commands** tabs. Compact uses slightly larger text and fields, with all main controls visible without scrolling. Mini and manually reduced windows retain scrolling so settings stay accessible. The UI contains no tint/HUD previews or invented live numbers. Window buttons use the LAB Plus geometry. The first visible window frame appears after local fonts, logo and WebView2 content are ready; the only native child is the WebView2 host.

## Console commands

Commands register in the game's console after the native HUD initializes. They do not require cheats. Run any command without arguments to print its current value and usage. Valid changes persist to the same preferences used by the UI.

| Command | Behavior |
| --- | --- |
| `lab_speedometer_show <0\|1> [current\|landing\|vertical]` | Overall visibility, or one meter. The overall switch retains secondary preferences. |
| `lab_speedometer_showlanding <0\|1>` | Landing meter visibility. |
| `lab_speedometer_showvertical <0\|1>` | Vertical meter visibility. |
| `lab_speedometer_verticalsign <0\|1>` | Show direction: 1 = +up/-down, 0 = unsigned magnitude; 1 = signed (default). |
| `lab_speedometer_x_pos <pixels> [current\|landing\|vertical]` | X position; omit the target to change all three. |
| `lab_speedometer_y_pos <pixels> [current\|landing\|vertical]` | Y position; omit the target to change all three. |
| `lab_speedometer_size <0.1..10> [current\|landing\|vertical]` | Independent text size; omit the target to change all three. |
| `lab_speedometer_color <R> <G> <B> [current\|landing\|vertical]` | Integer RGB channels 0–255; omit the target to change all three. |
| `lab_lowhptintopacity <0..100>` | Both red tint layers; 100 restores the original opacity. |

Examples:

```text
lab_speedometer_size 1.2 current
lab_speedometer_size 0.8 landing
lab_speedometer_size 1.5 vertical
lab_speedometer_color 138 213 233 vertical
lab_speedometer_showvertical 1
lab_lowhptintopacity 35
```

Lighting Fix, Q/E protection and driver-line hiding are controlled by the LAB switches. No unrelated DESRU commands are registered. LAB supports coexistence with the upstream DESRU metrics controls. LAB Plus remains a separate application; foreign HUD hooks are detected and retained rather than overwritten.

## Compatibility and validation

Telemetry uses exact executable hashes from `Assets/game-profiles.json`; native HUD/input/tint layouts are independently resolved from the executable's metadata and checked against the loaded image. Unknown or modified builds are refused with an English status message. The Game Pass placeholder profile is not a verified hash and is not accepted automatically. Per-feature metadata failures are reported, with original behavior retained.

Automated checks execute the generated x64 metrics formatter, speedometer, command, input and both tint adapters. Metrics fixtures cover standalone FPS-only output, DESRU ordering, the original small font, AMD hiding, minimal metrics and shutdown/lease expiry. The formatter layout was also resolved from the installed 1.0, 1.1, 5.1 and current executables. Native lighting lifetime fixtures cover normal close, abrupt owner termination and PID reuse. Live-game and visual acceptance testing of this build remains required. The 1 ms velocity worker is sampled telemetry; scheduling stalls can miss an exceptionally short landing impulse.

See [CREDITS.md](CREDITS.md), `LICENSE` and `Assets/licenses` for attribution.

## Build locally

Run `pwsh -File build.ps1 -Test` from the repository root with the .NET 10 SDK, PowerShell 7 and either Visual Studio C++ build tools or LLVM-MinGW. The build discovers the installed compiler; Vulkan headers are included under `third_party`.

The script builds the lighting DLL and application, executes the native fixtures, and produces `artifacts/lab-1.0-win-x64.zip` plus its SHA-256 file. CPU-only CI may add `-SkipVulkanProbe`; this still runs lighting math and ownership tests. The local runtime package is `artifacts/package-1.0`.

Packaging uses an explicit file list and validates the ZIP contents. The program archive includes runtime files, UI assets and component licenses. README.md, CREDITS.md, debug symbols, settings, populated logs, developer tools, source and documentation screenshots are excluded. Attribution remains available under `Assets/licenses` and in the repository.

`lab.exe --preview` starts an isolated design preview. `--qa-output <absolute-directory>` also checks UI controls, persistence, all window presets and Compact without scrolling, then closes. Run UI QA in the normal Windows user session.
