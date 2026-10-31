First LAB preview release with the UAC Frost / Inline Controls interface.

- Native horizontal, landing and unsigned vertical speedometers.
- Independent visibility, X/Y position, RGB color and text size for each meter.
- Selected console commands renamed to the `lab_` prefix.
- Lighting Fix for audited early game builds on NVIDIA.
- Q/E + Modbot opening-hold protection.
- Adjustable opacity for both low-health red tint layers.
- Option to hide the AMD Proprietary Driver statistics line.
- Original red emblem, bundled Eternal fonts and English interface text.

Extract the complete `lab-0.1.0-win-x64.zip` archive and run `lab.exe`. Requires Windows x64, .NET 10 Desktop Runtime and Microsoft Edge WebView2 Runtime. Full feature descriptions, compatibility and console reference are in the README.

Validation: 126 executed native/settings fixture checks, 24 lighting math and Vulkan-loader checks, plus an actual WebView2 launch and UI persistence/window-control checks. Live-game acceptance testing of this new LAB build is still pending; this is a preview release.

The `.sha256` attachment contains the archive checksum.
