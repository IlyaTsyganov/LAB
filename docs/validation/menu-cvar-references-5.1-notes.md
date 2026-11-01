# DOOM Eternal 5.1 cvar display audit

The metadata assigns `CVAR_MODIFIED` to `0x40000` and `CVAR_MODIFIED2` to `0x80000`. Menu preferences and cheats share the same value-type and protection flags. Neither modified flag classifies menu ownership.

`menu-cvar-references-5.1.json` records settings-controller vtables, methods and referenced cvars. The policy also includes the 35-name profile table at RVA `0x2bcaf80` (stride 16; loop at `0x18be593`). Exact names preserve unrelated debug controls.

Resolution scaling (`rs_forceResolution`) is set by function `0x18c5720` from `idPlayerProfileShell` fields `advResolutionScalingMode` (`0xe464`), `advStaticResolutionScale` (`0xe460`) and `advResolutionScalingTargetFrameRate` (`0xe468`), with setter calls at `0x18c57bc`, `0x18c57f2`, `0x18c5827`.

`r_umbraAccurateOcclusionThreshold` is automatic renderer state, excluded separately from preferences: code `0x94ae29` computes its value from renderer state and writes it through the float setter at `0x94ae7d`.

The initial read-only inspection of running PID 32980 found 53 nondefault protected values and a 25-entry early startup baseline. That left 29 false HUD rows. The corrected reader returned 22 eligible nondefault values, all accounted for by the saved baseline: zero false rows. No cvar values were written during the inspection. `g_infiniteAmmo`, `timescale`, renderer debug controls and difficulty-specific damage controls remain eligible.
