# AgentMux Windows

AgentMux Windows is a lightweight Windows terminal workspace for running coding agents side by side.

It is inspired by the terminal/workspace workflow category popularized by cmux, but it is an independent Windows implementation and is not affiliated with cmux, Manaflow, or the macOS cmux project.

## Goals

- Native Windows desktop app, no Electron app shell.
- ConPTY-backed terminal panes.
- Workspace sidebar, surface tabs, split panes, and keyboard-first navigation.
- Agent-aware notifications through OSC sequences and a CLI.
- Local named-pipe API for automation.
- Session/layout restore.
- Public, reproducible Windows builds.

## Current Status

Pre-alpha scaffold.

This repository currently contains the public-safe foundation: project structure, core models, OSC notification parsing, named-pipe JSON-RPC contracts, CLI skeleton, a WPF shell with workspace sidebar and recursive split panes, per-pane ConPTY session hosting, terminal pane resize propagation, app-level session restore, a WebView2/xterm terminal-renderer bridge with WPF fallback, a WebView2 browser-pane preview, direct terminal/browser input, lightweight browser automation commands with same-origin frame targeting, frame-tree inspection, an in-memory console log, an in-memory network event log with explicit response-body lookup and metadata-only HAR-like export, and a local download event log, configurable app shortcuts, tests, CI, a framework-dependent Windows package artifact, and a prerelease ZIP workflow. Broader browser automation semantics and true manual Windows desktop smoke remain future implementation work.

Required Windows CI proves the solution restores, builds, runs deterministic unit tests, composes the WPF split-pane window in an STA smoke test, propagates terminal pane dimensions from WPF layout, and passes headless ConPTY smoke tests for command output, stdin echo, and live resize. A real visible Windows desktop smoke test is still required before calling this release-ready.

AgentMux saves `%LOCALAPPDATA%\AgentMux\session.json` and restores workspaces, active workspace, split layout, active pane, pane titles, browser URLs, and last terminal screen text. It does not restore live terminal processes; restored terminal panes start fresh ConPTY sessions as needed.

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
- runtime config, dependency files, WebView2/xterm assets, `PACKAGE.json`, `SHA256SUMS.txt`, README, and license

It expects .NET 9 Desktop Runtime and WebView2 Runtime on the Windows machine. It is a smoke-ready pre-alpha package, not an installer.

After extracting the artifact on Windows, run this from the package root for a quick CLI sanity check:

```powershell
.\cli\agentmux.exe --help
```

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
agentmux workspace list
agentmux workspace create --title "API"
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
agentmux browser console --limit 20
agentmux browser console-clear
agentmux browser network --limit 20
agentmux browser network-clear
agentmux browser response-body <request-id>
agentmux browser har-metadata .\network.har.json
agentmux browser downloads --limit 20
agentmux browser downloads-clear
agentmux send "npm test"
agentmux read-screen --lines 50
```

Browser automation commands operate on the active browser pane. `browser eval` runs arbitrary JavaScript in that pane, `browser click` dispatches WebView2 pointer/mouse input at the selector center, `browser type` inserts text into a focused selector, `browser press` sends a key down/up sequence, `browser screenshot` writes a PNG to a local path resolved by the CLI before it is sent to the app, and `browser frames` returns the WebView2/CDP frame tree for inspection. `click`, `fill`, `type`, and `press` accept `--frame <name-or-id>` for same-origin `iframe`/`frame` targets. `browser console` returns a compact local in-memory CDP Runtime console log for the active browser pane, and `browser console-clear` clears it. Console messages may contain tokens, personal data, API responses, localStorage-derived values, auth callbacks, or private document text; messages are returned unredacted to same-user local callers, capped at 4,096 characters each with a `truncated` marker, and are not stored in the session snapshot by the browser pane. CLI stdout can still persist console output in terminal scrollback, shell transcripts, redirected files, or logs. `browser network` returns a compact local in-memory CDP Network event log for the active browser pane, and `browser network-clear` clears it. The network log does not copy headers, cookies, bodies, or post data, but URLs themselves may contain sensitive query data. `browser response-body <request-id>` explicitly asks WebView2/CDP for the body of one recent request id already present in the active browser pane's network log. Returned bodies may contain secrets, are returned unredacted to the local caller, are capped at 1,000,000 characters with a `truncated` marker, and are not stored in the network log or session snapshot by the browser pane. CLI output can still persist in terminal scrollback, shell transcripts, redirected files, or logs. `browser har-metadata <path>` writes a metadata-only HAR-like preview for the active browser pane's currently retained in-memory network events to a caller-provided filesystem path; `browser har <path>` is accepted as an alias. The export omits headers, cookies, post data, response body text, and downloaded file contents, but includes full URLs, approximate timing/size metadata, and persists until the caller deletes the file. `browser downloads` returns a compact local in-memory download log for the active browser pane, and `browser downloads-clear` clears only that log, not downloaded files. Downloads are routed to `%LOCALAPPDATA%\AgentMux\Downloads` with unique sanitized filenames. This is richer preview automation, not full Playwright parity, cross-origin frame automation, request interception or mutation, automatic response capture, response header/cookie capture, full HAR export, tracing, source-map expansion, malware scanning, pause/resume/cancel download control, telemetry, or physical trusted input.
Pane focus commands operate on the active split tree. `focus next` / `focus previous` cycle through panes, and `focus left` / `focus right` / `focus up` / `focus down` choose an adjacent pane from split geometry. The WPF shell also supports `Ctrl+Alt+Arrow` directional pane focus and `Ctrl+Tab` / `Ctrl+Shift+Tab` pane cycling.
Pane action commands operate on the active pane. `zoom` toggles a single-pane view without destroying the split layout, and `close-pane` removes the active pane while preserving a remaining sibling pane.
`pane resize --cols <cols> --rows <rows>` updates the active terminal pane's saved dimensions and resizes its live ConPTY session when one is running.

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
