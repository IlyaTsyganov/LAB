# LAB 1.0.1

- The status line now uses only Game not running, Running <version>, or Restart game. Technical connection errors stay in logs/lab.log.
- A game already running outside LAB before LAB starts shows RESTART GAME(LAB) instead of feature indicators. Ordinary LAB features remain disabled until the game is restarted.
- LAB remembers its own game launches across LAB restarts and validates the process ID, creation time and executable path.
- Launch game disables immediately on click, stays disabled while the selected game process is present, and becomes available again after a failed launch or game exit.
- Package renamed to LAB.zip. It contains one LAB folder with LAB.exe, required runtime files, UI assets and component licenses. User settings, populated logs, README.md, CREDITS.md and debug files are excluded.
- Existing saved preferences and first-run defaults from LAB 1.0 are retained.

Requirements: Windows x64, .NET 10 Desktop Runtime and Microsoft Edge WebView2 Runtime. Extract the complete LAB folder before running LAB.exe.
