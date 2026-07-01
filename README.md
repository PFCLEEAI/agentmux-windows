# AgentMux Windows

AgentMux Windows is a lightweight Windows terminal workspace for running coding agents side by side.

It is inspired by the terminal/workspace workflow category popularized by cmux, but it is an independent Windows implementation and is not affiliated with cmux, Manaflow, or the macOS cmux project.

## Goals

- Native Windows desktop app, no Electron app shell.
- ConPTY-backed terminal panes.
- Workspace sidebar, surface tabs, split panes, and keyboard-first navigation.
- Agent-aware notifications through OSC sequences, an in-app notification center, and a CLI.
- Local named-pipe API for automation.
- Session/layout restore.
- Public, reproducible Windows builds.

## Current Status

Pre-alpha scaffold.

This repository currently contains the public-safe foundation: project structure, core models, streaming OSC notification parsing from terminal output, named-pipe JSON-RPC contracts, CLI skeleton, a WPF shell with workspace sidebar, workspace list/create/select switching, surface tabs, a lightweight notification center, and recursive split panes, per-pane ConPTY session hosting, bounded direct-child ConPTY cleanup after terminal disposal, terminal pane resize propagation, expanded terminal send-key encoding, bounded active-terminal read-screen output, app-level session restore, a WebView2/xterm terminal-renderer bridge with WPF fallback, a WebView2 browser-pane preview, direct terminal/browser input, lightweight browser automation commands with same-origin frame targeting, frame-tree inspection, an in-memory console log, an in-memory network event log with explicit response-body lookup and metadata-only HAR-like export, and a local download event log, configurable app shortcuts, tests, CI, a framework-dependent Windows package artifact, a prerelease ZIP workflow, and a manual desktop-smoke proof packet. Broader browser automation semantics and true manual Windows desktop smoke evidence remain future implementation work.

Required Windows CI proves the solution restores, builds, runs deterministic unit tests, composes the WPF split-pane window in an STA smoke test, creates/lists/selects/restores workspaces, creates/selects/restores surface tabs, propagates terminal pane dimensions from WPF layout, dispatches supported terminal key sequences through ConPTY, and passes headless ConPTY smoke tests for command output, stdin echo, raw control-sequence transport, live resize, and cooperative/stubborn direct child disappearance after bounded disposal. This is not process-tree cleanup. A real visible Windows desktop smoke test is still required before calling this release-ready.

AgentMux saves `%LOCALAPPDATA%\AgentMux\session.json` and restores workspaces, active workspace, surfaces, active surface, split layout, active pane, pane titles, browser URLs, and last terminal screen text. It does not restore live terminal processes; restored terminal panes start fresh ConPTY sessions as needed.

## Tech Stack

- C# / .NET 9
- WPF for the Windows shell
- ConPTY for terminal hosting
- WebView2-hosted xterm.js terminal renderer bridge without Electron
- Named pipes for local CLI/API automation
- JSON persistence under the user's local app data directory

## Build

Core tests can run anywhere with .NET 9:

```powershell
dotnet test tests/AgentMux.Tests/AgentMux.Tests.csproj
```

The Windows app targets `net9.0-windows10.0.17763.0` and should be built on Windows:

```powershell
dotnet build AgentMux.sln -c Release
dotnet run --project src/AgentMux.Win.App/AgentMux.Win.App.csproj
```

CI publishes a lightweight framework-dependent artifact named `agentmux-windows-package` after the Windows smoke gates pass. The Release workflow uses the same package script to produce `agentmux-windows-<version>-framework-dependent.zip` plus a sibling `.sha256` file. `v*` tag pushes create GitHub prereleases; manual workflow dispatch builds uploadable artifacts only.

The package contains:

- `AgentMux.exe`: WPF desktop app
- `cli\agentmux.exe`: CLI for the running app
- runtime config, dependency files, WebView2/xterm assets, `PACKAGE.json`, `SHA256SUMS.txt`, README, license, and the manual desktop-smoke helper/runbook

It expects .NET 9 Desktop Runtime and WebView2 Runtime on the Windows machine. It is a smoke-ready pre-alpha package, not an installer.

After extracting the artifact on Windows, run this from the package root for a quick CLI sanity check:

```powershell
.\cli\agentmux.exe --help
```

For the manual desktop proof gate, run:

```powershell
.\tools\manual-desktop-smoke.ps1
```

Follow [docs/manual-windows-desktop-smoke.md](docs/manual-windows-desktop-smoke.md) to collect screenshots, helper output, and pass/fail notes. The helper preflights the package and can launch AgentMux, but helper execution alone is not a pass; actual visible Windows desktop and physical-keyboard evidence is still required.

To produce the package from a Windows checkout after building:

```powershell
.\tools\package-windows.ps1 -OutputPath .\artifacts\agentmux-windows-package -Configuration Release -NoBuild
```

Experimental ConPTY smoke tests:

```powershell
dotnet test tests/AgentMux.Windows.Tests/AgentMux.Windows.Tests.csproj -c Release
```

## CLI Preview

The CLI talks to a running AgentMux instance over a named pipe:

```powershell
agentmux ping
agentmux status
agentmux notify --title "Codex" --body "Waiting for input"
agentmux notifications list --limit 20
agentmux notifications jump-latest
agentmux notifications clear
agentmux workspace list
agentmux workspace create --title "API" --cwd "C:\src\api"
agentmux workspace select --index 0
agentmux workspace select --id <workspace-id>
agentmux workspace select 0
agentmux surface list
agentmux surface create --title "Scratch"
agentmux surface select --index 0
agentmux surface select --id <surface-id>
agentmux split right
agentmux focus right
agentmux zoom
agentmux close-pane
agentmux pane resize --cols 100 --rows 30
agentmux open-url https://example.com
agentmux browser open https://example.com
agentmux browser eval "document.title"
agentmux browser click "#submit"
agentmux browser click --frame agentmux-child-frame "#submit"
agentmux browser fill "#prompt" "write tests"
agentmux browser fill --frame agentmux-child-frame "#prompt" "write tests"
agentmux browser type "#prompt" "write tests"
agentmux browser press Enter --selector "#prompt" --frame agentmux-child-frame
agentmux browser screenshot .\browser.png
agentmux browser frames
agentmux browser wait-for-selector "#ready" --timeout-ms 5000
agentmux browser wait-load --state network-idle --timeout-ms 5000
agentmux browser console --limit 20
agentmux browser console-clear
agentmux browser network --limit 20
agentmux browser network-clear
agentmux browser response-body <request-id>
agentmux browser har-metadata .\network.har.json
agentmux browser downloads --limit 20
agentmux browser downloads-clear
agentmux send "npm test"
agentmux send-key Enter
agentmux send-key Ctrl+C
agentmux send-key PageDown
agentmux send-key F5
agentmux send-key Alt+Left
agentmux read-screen --lines 50
```

Workspace commands operate on app-level workspace state. `workspace list` returns compact workspace metadata for each workspace, including id, title, zero-based index, active flag, working directory, unread count, surface count, active surface index/title, active pane id, pane count, and browser pane count; it does not return the full raw workspace/surface/pane object graph. `workspace create --title <name> --cwd <path>` creates and selects a new workspace, and `workspace select` accepts either zero-based `--index`, positional index, or `--id`. `workspace ls`, `workspace new`, and `workspace use` are accepted aliases. Workspace selection updates the WPF sidebar, active surface render, and saved session state.

Browser automation commands operate on the active browser pane. `browser eval` runs arbitrary JavaScript in that pane, `browser click` dispatches WebView2 pointer/mouse input at the selector center, `browser type` inserts text into a focused selector, `browser press` sends a key down/up sequence, `browser screenshot` writes a PNG to a local path resolved by the CLI before it is sent to the app, and `browser frames` returns the WebView2/CDP frame tree for inspection. `click`, `fill`, `type`, and `press` accept `--frame <name-or-id>` for same-origin `iframe`/`frame` targets. `browser wait-for-selector` is a bounded one-shot synchronization helper for `visible`, `attached`, or `hidden` selector states in the active browser pane; `browser wait` is accepted as an alias, and `--frame <name-or-id>` follows the same same-origin frame boundary as selector actions. `browser wait-load` waits for active-pane `domcontentloaded`, `load`, or preview `network-idle` readiness, where `network-idle` is derived from currently tracked active-pane in-flight request metadata and a short quiet window. These waits are synchronization helpers, not full Playwright load-state parity, retry-action, cross-context readiness, or persistent-watch APIs. `browser console` returns a compact local in-memory CDP Runtime console log for the active browser pane, and `browser console-clear` clears it. Console messages may contain tokens, personal data, API responses, localStorage-derived values, auth callbacks, or private document text; messages are returned unredacted to same-user local callers, capped at 4,096 characters each with a `truncated` marker, and are not stored in the session snapshot by the browser pane. CLI stdout can still persist console output in terminal scrollback, shell transcripts, redirected files, or logs. `browser network` returns a compact local in-memory CDP Network event log for the active browser pane, and `browser network-clear` clears it. The network log does not copy headers, cookies, bodies, or post data, but URLs themselves may contain sensitive query data. `browser response-body <request-id>` explicitly asks WebView2/CDP for the body of one recent request id already present in the active browser pane's network log. Returned bodies may contain secrets, are returned unredacted to the local caller, are capped at 1,000,000 characters with a `truncated` marker, and are not stored in the network log or session snapshot by the browser pane. CLI output can still persist in terminal scrollback, shell transcripts, redirected files, or logs. `browser har-metadata <path>` writes a metadata-only HAR-like preview for the active browser pane's currently retained in-memory network events to a caller-provided filesystem path; `browser har <path>` is accepted as an alias. The export omits headers, cookies, post data, response body text, and downloaded file contents, but includes full URLs, approximate timing/size metadata, and persists until the caller deletes the file. `browser downloads` returns a compact local in-memory download log for the active browser pane, and `browser downloads-clear` clears only that log, not downloaded files. Downloads are routed to `%LOCALAPPDATA%\AgentMux\Downloads` with unique sanitized filenames. This is richer preview automation, not full Playwright parity, cross-origin frame automation, request interception or mutation, automatic response capture, response header/cookie capture, full HAR export, tracing, source-map expansion, malware scanning, pause/resume/cancel download control, telemetry, or physical trusted input.
Surface commands operate on the active workspace. `surface list` returns each surface id/title/index, active pane id, pane count, browser pane count, and zoomed pane id. `surface create --title <name>` creates and selects a new surface, and `surface select` accepts either zero-based `--index` or `--id`. `surfaces`, `tab`, and `tabs` are namespace aliases, with `surface ls`, `surface new`, and `surface use` accepted as action aliases.
Pane focus commands operate on the active split tree. `focus next` / `focus previous` cycle through panes, and `focus left` / `focus right` / `focus up` / `focus down` choose an adjacent pane from split geometry. The WPF shell also supports `Ctrl+Alt+Arrow` directional pane focus and `Ctrl+Tab` / `Ctrl+Shift+Tab` pane cycling.
Pane action commands operate on the active pane. `zoom` toggles a single-pane view without destroying the split layout, and `close-pane` removes the active pane while preserving a remaining sibling pane.
`pane resize --cols <cols> --rows <rows>` updates the active terminal pane's saved dimensions and resizes its live ConPTY session when one is running.
`send-key <key>` operates on the active terminal pane and sends supported logical terminal key sequences through the same ConPTY input path as text input. Supported names include Enter/Return, Tab, Shift+Tab, Escape, Backspace, Delete, Insert, arrows, Home/End, PageUp/PageDown, F1-F12, generic Ctrl+A through Ctrl+Z, common Ctrl symbols such as Ctrl+[, and Alt/Meta-prefixed supported keys or single printable characters. Unsupported or ambiguous forms fail instead of sending raw escape payloads. This is terminal automation input, not Windows `SendInput`, trusted physical keyboard proof, global shortcut dispatch, or browser `press`.
`read-screen` returns the active terminal pane's current `LastScreenText`. `read-screen --lines <count>` returns only the last logical lines and adds `lines`, `truncated`, `paneId`, and `paneKind` metadata to the response. Browser panes return empty text rather than DOM content. This is a bounded active-pane snapshot, not full xterm scrollback extraction, transcript storage, redaction, or physical trusted input.
Notification commands and the in-app notification center operate on local in-memory notification state capped at the most recent 200 events. Terminal panes strip OSC 9/99/777 notification sequences from visible terminal text and turn them into unread workspace/pane markers. The sidebar notification center shows the unread count and recent notifications, can jump to the newest unread notification, can clear unread state, and can be opened or closed without marking items read. `notifications jump-latest` and the UI jump action select the pane for the newest unread notification when it still exists. Notification bodies are not written to session snapshots, but the UI, CLI output, screenshots, terminal scrollback, shell transcripts, redirected files, or logs can still expose them.

## Shortcut Settings

Default app shortcuts:

- `Ctrl+Tab`: focus next pane
- `Ctrl+Shift+Tab`: focus previous pane
- `Ctrl+Alt+Left/Right/Up/Down`: focus adjacent pane
- `Ctrl+Shift+Z`: toggle active pane zoom
- `Ctrl+Shift+X`: close active pane

To customize shortcuts, create `%LOCALAPPDATA%\AgentMux\shortcuts.json`:

```json
{
  "toggleZoom": "Ctrl+Shift+F11",
  "closePane": "Ctrl+Shift+W",
  "focusRight": "Ctrl+Alt+L"
}
```

Supported actions are `focusNext`, `focusPrevious`, `focusLeft`, `focusRight`, `focusUp`, `focusDown`, `toggleZoom`, and `closePane`. Invalid or missing entries keep their defaults.

Shortcut matching is app-window only; these are not global hotkeys and the file is read at startup. Bare text-producing keys such as `A` or `Shift+A` are ignored so terminal/browser text input is not stolen.

## Attribution

AgentMux Windows studies public workflow ideas from:

- cmux: https://github.com/manaflow-ai/cmux
- mkurman/cmux-windows: https://github.com/mkurman/cmux-windows
- wmux: https://github.com/amirlehmam/wmux

No cmux source code, assets, screenshots, branding, or documentation are included.

## License

MIT. See [LICENSE](LICENSE).
