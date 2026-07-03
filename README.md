# AgentMux Windows

AgentMux Windows is a lightweight Windows terminal workspace for running coding agents side by side.

It is inspired by the terminal/workspace workflow category popularized by cmux, but it is an independent Windows implementation and is not affiliated with cmux, Manaflow, or the macOS cmux project.

## Goals

- Native Windows desktop app, no Electron app shell.
- ConPTY-backed terminal panes.
- Workspace sidebar, surface tabs, split panes, and keyboard-first navigation.
- Agent-aware notifications through OSC sequences, an in-app notification center, a best-effort native Windows toast mirror, and a CLI.
- Local named-pipe API for automation.
- Session/layout restore.
- Public, reproducible Windows builds.

## Current Status

Pre-alpha scaffold.

This repository currently contains the public-safe foundation: project structure, core models, streaming OSC notification parsing from terminal output, named-pipe JSON-RPC contracts, CLI skeleton, a WPF shell with workspace sidebar, workspace list/create/select switching with lightweight git-branch, latest-notification, explicit pull-request/port annotations, and in-memory workspace log/status metadata, surface tabs, a lightweight notification center, a read-only shortcut reference panel, best-effort local native Windows toast requests for new notifications, and recursive split panes, per-pane ConPTY session hosting, bounded direct-child ConPTY cleanup after terminal disposal, terminal pane resize propagation, expanded terminal send-key encoding, bounded active-terminal read-screen output, app-level session restore, a WebView2/xterm terminal-renderer bridge with WPF fallback, a WebView2 browser-pane preview, direct terminal/browser input, lightweight browser automation commands with same-origin frame targeting, active-browser back/forward/reload/current-url commands, bounded active-browser text and element-data extraction, read-only element find/state checks, a capped top-document DOM snapshot preview, dropdown select, hover/focus/click/fill/type/press selector actions, frame-tree inspection, an in-memory console log, an in-memory network event log with explicit response-body lookup and metadata-only HAR-like export, a bounded active-pane WebView2/CDP trace export, a local download event log, and transient active-pane browser route block/fulfill preview, configurable app shortcuts, tests, CI, a framework-dependent Windows package artifact, a prerelease ZIP workflow, and a manual desktop-smoke proof packet. Broader browser automation semantics and true manual Windows desktop smoke evidence remain future implementation work.

Required Windows CI proves the solution restores, builds, runs deterministic unit tests, composes the WPF split-pane window in an STA smoke test, creates/lists/selects/restores workspaces, creates/selects/restores surface tabs, propagates terminal pane dimensions from WPF layout, dispatches supported terminal key sequences through ConPTY, proves xterm runtime input events, renderer bridge input, and synthetic WebView2/xterm DOM keydown events from the hosted terminal runtime reach the active terminal pane's ConPTY input path, uploads `terminal-key-capture.png`, and passes headless ConPTY smoke tests for command output, stdin echo, raw control-sequence transport, live resize, and cooperative/stubborn direct child disappearance after bounded disposal. This is not process-tree cleanup, physical keyboard proof, trusted OS input, native focus/IME proof, or release-ready desktop QA. A real visible Windows desktop smoke test is still required before calling this release-ready.

AgentMux saves `%LOCALAPPDATA%\AgentMux\session.json` and restores workspaces, active workspace, explicit workspace PR/port annotations, surfaces, active surface, split layout, active pane, pane titles, browser URLs, and last terminal screen text. It does not restore live terminal processes or in-memory workspace logs/statuses; restored terminal panes start fresh ConPTY sessions as needed.

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
- runtime config, dependency files, WebView2/xterm assets, `PACKAGE.json`, `EVIDENCE.json`, `SHA256SUMS.txt`, README, license, a no-admin user install helper, the manual desktop-smoke helper/runbook, and a manual evidence verifier

It expects .NET 9 Desktop Runtime and WebView2 Runtime on the Windows machine. It is a smoke-ready pre-alpha package with a lightweight user install helper, not a signed installer or MSIX package.

After extracting the artifact on Windows, run this from the package root for a quick CLI sanity check:

```powershell
.\cli\agentmux.exe --help
```

To install the extracted package for the current user without administrator privileges:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\tools\install-user.ps1
```

The install helper copies the package to `%LOCALAPPDATA%\Programs\AgentMux`, creates `agentmux.cmd` and `agentmux-app.cmd` shims under the install folder's `bin` directory, verifies `SHA256SUMS.txt`, and adds that shim directory to the current user's PATH. Use `-PackagePath` to point at a package from a repo checkout, `-InstallRoot` to choose a different user-local target, and `-SkipPathUpdate` for CI/preflight runs that should not change PATH. Open a new terminal after a PATH update. Package checksums verify local package integrity; they do not prove publisher authenticity by themselves. `EVIDENCE.json` records commit/run provenance, expected hosted gates, smoke artifact names, and the remaining manual desktop gate; it is audit metadata, not a signature or release approval.

For the manual desktop proof gate, run:

```powershell
.\tools\manual-desktop-smoke.ps1
```

Follow [docs/manual-windows-desktop-smoke.md](docs/manual-windows-desktop-smoke.md) to collect screenshots, helper output, and pass/fail notes. After the evidence folder is filled, run `.\tools\verify-manual-desktop-evidence.ps1 -EvidencePath <evidence-folder> -PackagePath <package-folder>` to check packet shape, package checksums, and manual-gate metadata. The helper and verifier preflight the package and evidence packet, but script execution alone is not a pass; actual visible Windows desktop and physical-keyboard evidence is still required.

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
agentmux log "Server started" --level warn --source server
agentmux list-log --limit 20
agentmux clear-log
agentmux set-status build "Running tests" --icon checkmark --color "#22c55e"
agentmux list-status
agentmux clear-status build
agentmux clear-status --all
agentmux set-progress 0.75 --label "Deploying"
agentmux clear-progress
agentmux workspace list
agentmux workspace create --title "API" --cwd "C:\src\api"
agentmux workspace select --index 0
agentmux workspace select --id <workspace-id>
agentmux workspace select 0
agentmux workspace pr 123 --status open --url https://github.com/org/repo/pull/123
agentmux workspace pr clear
agentmux workspace ports 3000 5173
agentmux workspace ports --ports 3000,5173
agentmux workspace ports clear
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
agentmux browser back
agentmux browser forward
agentmux browser reload
agentmux browser url
agentmux browser get-url
agentmux browser get title
agentmux browser get url
agentmux browser get text "main"
agentmux browser get html "#content"
agentmux browser get value "input[name='email']"
agentmux browser get attr "img" --attr src
agentmux browser get count ".item"
agentmux browser get box ".element"
agentmux browser get styles ".button" --property color
agentmux browser eval "document.title"
agentmux browser text --selector "main" --max-chars 10000
agentmux browser snapshot --interactive --compact --max-depth 3
agentmux browser snapshot --selector "main" --cursor
agentmux browser click "#submit"
agentmux browser click --frame agentmux-child-frame "#submit"
agentmux browser hover ".menu-item"
agentmux browser hover --frame agentmux-child-frame "#submit"
agentmux browser focus "input[name='email']"
agentmux browser focus --frame agentmux-child-frame "#prompt"
agentmux browser is visible ".modal"
agentmux browser is enabled "button.submit"
agentmux browser is checked "input[type='checkbox']"
agentmux browser find role button --name "Submit" --exact
agentmux browser find text "Welcome"
agentmux browser find label "Email"
agentmux browser find placeholder "Search"
agentmux browser find testid login-submit
agentmux browser find nth ".item" 0
agentmux browser select "#country" US
agentmux browser select --frame agentmux-child-frame "#country" FR
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
agentmux browser trace .\browser-trace.json --duration-ms 500
agentmux browser downloads --limit 20
agentmux browser downloads-clear
agentmux browser route list
agentmux browser route block --url-contains "/api/private"
agentmux browser route fulfill --url-contains "/api/mock" --status 200 --content-type text/plain --body "mocked"
agentmux browser route clear
agentmux send "npm test"
agentmux send-key Enter
agentmux send-key Ctrl+C
agentmux send-key PageDown
agentmux send-key F5
agentmux send-key Alt+Left
agentmux read-screen --lines 50
```

Workspace commands operate on app-level workspace state. `workspace list` returns compact workspace metadata for each workspace, including id, title, zero-based index, active flag, working directory, detected git branch, manually set pull-request metadata, manually set port annotations, unread count, latest unread notification preview, latest workspace log preview, latest workspace status preview, latest workspace progress preview, structured workspace progress metadata, workspace log/status/progress counts, surface count, active surface index/title, active pane id, pane count, and browser pane count; it does not return the full raw workspace/surface/pane object graph. Git branch detection reads the nearest `.git/HEAD` or `.git` indirection file from the workspace working directory without invoking `git`; detached HEAD, unreadable metadata, dirty status, live refresh, automatic PR discovery, and automatic port discovery are intentionally not reported. Latest notification, log, status, and progress previews are whitespace-compacted, capped display strings for sidebar/API context only; notification bodies, log messages, status text, and progress labels may still contain sensitive content and are visible to same-user local callers. `workspace create --title <name> --cwd <path>` creates and selects a new workspace, and `workspace select` accepts either zero-based `--index`, positional index, or `--id`. `workspace pr 123 --status open --url <url>` and `workspace pr clear` set or clear explicit pull-request annotations for the active workspace; `--index <n>` or `--id <workspace-id>` can target another workspace. PR annotations are stored in the session snapshot and shown as sidebar context, but they are manual/stale-able and do not prove that a GitHub pull request exists, is open, is mergeable, passed checks, or belongs to the workspace. `workspace ports 3000 5173`, `workspace ports --ports 3000,5173`, and `workspace ports clear` set or clear explicit numeric port annotations for the active workspace; `--index <n>` or `--id <workspace-id>` can target another workspace. Port annotations are sorted, deduplicated, stored in the session snapshot, and shown as sidebar context, but they may be stale and do not prove a listener exists, is reachable, is healthy, or is owned by that workspace. `agentmux log "message" [--level info|warn|error|debug] [--source text] [--workspace <id-or-index>]`, `agentmux list-log [--limit n] [--workspace <id-or-index>]`, and `agentmux clear-log [--workspace <id-or-index>]` manage explicit local workspace log entries. Logs are capped in memory at 200 entries, are not written to the session snapshot, and are not inferred from terminal output, files, processes, ports, Windows Event Log, shell history, telemetry, or background watchers. `clear-log` clears only AgentMux's in-memory copy, not CLI stdout, terminal scrollback, screenshots, redirected files, or shell transcripts. `agentmux set-status <key> <text> [--icon <text>] [--color <text>] [--workspace <id-or-index>]`, `agentmux list-status [--workspace <id-or-index>]`, and `agentmux clear-status <key|--all> [--workspace <id-or-index>]` manage explicit local workspace status entries. Statuses are keyed per workspace, replace on repeated key, capped in memory at 100 entries, are not written to the session snapshot, and are not inferred from terminal output, files, processes, ports, Windows Event Log, shell history, telemetry, or background watchers. `clear-status` clears only AgentMux's in-memory status copy, not CLI stdout, terminal scrollback, screenshots, redirected files, or shell transcripts. `agentmux set-progress <0..1> [--label <text>] [--workspace <id-or-index>]` and `agentmux clear-progress [--workspace <id-or-index>]` manage one explicit local progress value per workspace. Progress is not written to the session snapshot and is not inferred from terminal output, files, tests, processes, ports, git, Windows Event Log, shell history, telemetry, or background watchers. `clear-progress` clears only AgentMux's in-memory progress copy, not CLI stdout, terminal scrollback, screenshots, redirected files, or shell transcripts. `workspace ls`, `workspace new`, `workspace use`, `workspace pull-request`, `workspace pull`, and `workspace port` are accepted aliases. Workspace selection updates the WPF sidebar, active surface render, and saved session state.

Browser automation commands operate on the active browser pane. `browser back`, `browser forward`, `browser reload`, and `browser url` / `browser get-url` / `browser get url` provide CMUX-spelled active-pane navigation and current-URL inspection; they do not implement CMUX surface targeting, `--snapshot-after`, browser-history export, or global browser-context automation. Current URLs are returned unredacted to same-user local callers and supported browser URLs may be stored in the session snapshot as pane URL state. `browser eval` runs arbitrary JavaScript in that pane, `browser text [--selector <css>] [--frame <name-or-id>] [--max-chars <count>]` returns capped rendered text from the active document or selector, and `browser get <text|html|value|attr|count|box|styles|title|url>` returns CMUX-style active-pane element data. Selector-based `browser get` commands accept CSS selectors and same-origin `--frame <name-or-id>` targets; `attr` requires `--attr <name>`, `styles` requires `--property <name>`, and `box` returns WebView viewport CSS pixels rather than screen coordinates. Text-like `browser get` output is capped at 10,000 characters with a `truncated` marker. `browser is visible|enabled|checked` returns an immediate DOM/style/property/ARIA state result for a selector without waiting or retrying. `browser find role|text|label|placeholder|testid|first|last|nth` returns capped, read-only, best-effort match metadata for the active document or same-origin frame; `nth` is zero-based, `testid` matches `data-testid` exactly, and returned selector strings are inspection hints, not stable element handles for later actions. `browser snapshot [--interactive] [--cursor] [--compact] [--max-depth <count>] [--selector <css>]` returns a capped, DOM-derived, top-document-only snapshot with line text plus structured nodes; it does not traverse frames, return stable refs, use the browser accessibility tree, or scrape form/control values by default. `browser select <selector> <value>` sets one HTML `<select>` to an exact option value, dispatching DOM `input` and `change` events only when the value changes; multi-select, label matching, and custom combobox widgets are not implemented. `browser hover` moves the WebView2/CDP pointer to the selector center, `browser focus` scrolls and DOM-focuses a selector, `browser click` dispatches WebView2 pointer/mouse input at the selector center, `browser type` inserts text into a focused selector, `browser press` sends a key down/up sequence, `browser screenshot` writes a PNG to a local path resolved by the CLI before it is sent to the app, and `browser frames` returns the WebView2/CDP frame tree for inspection. `text`, `get` selector reads, `is`, `find`, `select`, `hover`, `focus`, `click`, `fill`, `type`, and `press` accept `--frame <name-or-id>` for same-origin `iframe`/`frame` targets; `snapshot` is intentionally top-document only and rejects frame/surface/snapshot-after options. `browser text` defaults to 10,000 characters and is hard-capped at 100,000 characters with a `truncated` marker; returned page text, HTML, values, selected dropdown values, attributes, styles, geometry, titles, selector/frame names, labels, placeholders, test IDs, best-effort find match metadata, snapshot text/node metadata, and boolean state-check results may contain private content and CLI stdout can still persist them in terminal scrollback, shell transcripts, redirected files, or logs. `browser wait-for-selector` is a bounded one-shot synchronization helper for `visible`, `attached`, or `hidden` selector states in the active browser pane; `browser wait` is accepted as an alias, and `--frame <name-or-id>` follows the same same-origin frame boundary as selector actions. `browser wait-load` waits for active-pane `domcontentloaded`, `load`, or preview `network-idle` readiness, where `network-idle` is derived from currently tracked active-pane in-flight request metadata and a short quiet window. These waits are synchronization helpers, not full Playwright load-state parity, retry-action, cross-context readiness, or persistent-watch APIs. `browser console` returns a compact local in-memory CDP Runtime console log for the active browser pane, and `browser console-clear` clears it. Console messages may contain tokens, personal data, API responses, localStorage-derived values, auth callbacks, or private document text; messages are returned unredacted to same-user local callers, capped at 4,096 characters each with a `truncated` marker, and are not stored in the session snapshot by the browser pane. CLI stdout can still persist console output in terminal scrollback, shell transcripts, redirected files, or logs. `browser network` returns a compact local in-memory CDP Network event log for the active browser pane, and `browser network-clear` clears it. The network log does not copy headers, cookies, bodies, or post data, but URLs themselves may contain sensitive query data. `browser response-body <request-id>` explicitly asks WebView2/CDP for the body of one recent request id already present in the active browser pane's network log. Returned bodies may contain secrets, are returned unredacted to the local caller, are capped at 1,000,000 characters with a `truncated` marker, and are not stored in the network log or session snapshot by the browser pane. CLI output can still persist in terminal scrollback, shell transcripts, redirected files, or logs. `browser har-metadata <path>` writes a metadata-only HAR-like preview for the active browser pane's currently retained in-memory network events to a caller-provided filesystem path; `browser har <path>` is accepted as an alias. The export omits headers, cookies, post data, response body text, and downloaded file contents, but includes full URLs, approximate timing/size metadata, and persists until the caller deletes the file. `browser trace <path> [--duration-ms <ms>] [--max-bytes <bytes>]` writes a bounded one-shot WebView2/CDP JSON trace for the active browser pane to a caller-provided local path and returns compact metadata only. The app caps duration and bytes, uses a fixed narrow category preset, and deletes partial temp output when the byte cap is exceeded. Trace files may still include full URLs, query tokens, page titles, frame ids, script/source names, timing, process/thread metadata, and other content-derived browser data, and persist until the caller deletes them. `browser downloads` returns a compact local in-memory download log for the active browser pane, and `browser downloads-clear` clears only that log, not downloaded files. Downloads are routed to `%LOCALAPPDATA%\AgentMux\Downloads` with unique sanitized filenames. `browser route` is a transient active-pane request interception preview: `route block --url-contains <text>` fails matching requests, `route fulfill --url-contains <text> [--status <code>] [--content-type <type>] [--body <text>]` returns a synthetic UTF-8 body with a fixed permissive CORS header, `route list` returns rules and recent hit URLs, and `route clear` removes the in-memory rules and disables Fetch interception. This is richer preview automation, not full Playwright parity, CMUX surface targeting, cross-origin frame automation, trusted OS input, full accessibility-tree snapshotting, stable element references, form-value scraping beyond explicit selector/value reads, full browser-context routing, persistent route state, caller-controlled response header mutation beyond status/content type/body, header/cookie/post-data capture, request-body capture, automatic response capture, full HAR export, full browser-context tracing, source-map expansion, malware scanning, pause/resume/cancel download control, telemetry, or physical trusted input.
Surface commands operate on the active workspace. `surface list` returns each surface id/title/index, active pane id, pane count, browser pane count, and zoomed pane id. `surface create --title <name>` creates and selects a new surface, and `surface select` accepts either zero-based `--index` or `--id`. `surfaces`, `tab`, and `tabs` are namespace aliases, with `surface ls`, `surface new`, and `surface use` accepted as action aliases.
Pane focus commands operate on the active split tree. `focus next` / `focus previous` cycle through panes, and `focus left` / `focus right` / `focus up` / `focus down` choose an adjacent pane from split geometry. The WPF shell also supports `Ctrl+Alt+Arrow` directional pane focus and `Ctrl+Tab` / `Ctrl+Shift+Tab` pane cycling.
Pane action commands operate on the active pane. `zoom` toggles a single-pane view without destroying the split layout, and `close-pane` removes the active pane while preserving a remaining sibling pane.
`pane resize --cols <cols> --rows <rows>` updates the active terminal pane's saved dimensions and resizes its live ConPTY session when one is running.
`send-key <key>` operates on the active terminal pane and sends supported logical terminal key sequences through the same ConPTY input path as text input. Supported names include Enter/Return, Tab, Shift+Tab, Escape, Backspace, Delete, Insert, arrows, Home/End, PageUp/PageDown, F1-F12, generic Ctrl+A through Ctrl+Z, common Ctrl symbols such as Ctrl+[, and Alt/Meta-prefixed supported keys or single printable characters. Unsupported or ambiguous forms fail instead of sending raw escape payloads. This is terminal automation input, not Windows `SendInput`, trusted physical keyboard proof, global shortcut dispatch, or browser `press`.
`read-screen` returns the active terminal pane's current `LastScreenText`. `read-screen --lines <count>` returns only the last logical lines and adds `lines`, `truncated`, `paneId`, and `paneKind` metadata to the response. Browser panes return empty `read-screen` text; use `browser text` or `browser get ...` for explicit active-browser rendered text or element-data extraction. This is a bounded active-pane snapshot, not full xterm scrollback extraction, transcript storage, redaction, or physical trusted input.
Notification commands and the in-app notification center operate on local in-memory notification state capped at the most recent 200 events. Terminal panes strip OSC 9/99/777 notification sequences from visible terminal text and turn them into unread workspace/pane markers. The sidebar notification center shows the unread count and recent notifications, and workspace sidebar metadata shows a capped preview of the latest unread notification for that workspace. It can jump to the newest unread notification, can clear unread state, and can be opened or closed without marking items read. `notifications jump-latest` and the UI jump action select the pane for the newest unread notification when it still exists. The native Windows toast preview mirrors new notification title/subtitle/body through a best-effort local Windows toast request with bounded text length; if Windows notification registration, desktop settings, elevation, or platform policy rejects the toast, in-app notification state still works. Toast text is not redacted and may be displayed outside the AgentMux window or retained by Windows notification UI according to user/system settings. Any same-user local process that can access the AgentMux pipe can call `agentmux notify` and raise AgentMux-branded local notification content. `notifications clear` clears AgentMux unread state only; it is not secure deletion and may not remove already displayed Windows notifications. Notification bodies and latest-notification previews are not written to session snapshots, but native toasts, the UI, CLI output, screenshots, terminal scrollback, shell transcripts, redirected files, or logs can still expose them.

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

The sidebar `Shortcuts` panel shows the effective bindings and the config path loaded at startup. It is read-only; edit `shortcuts.json` and restart AgentMux to change bindings.

Shortcut matching is app-window only; these are not global hotkeys and the file is read at startup. Bare text-producing keys such as `A` or `Shift+A` are ignored so terminal/browser text input is not stolen.

## Attribution

AgentMux Windows studies public workflow ideas from:

- cmux: https://github.com/manaflow-ai/cmux
- mkurman/cmux-windows: https://github.com/mkurman/cmux-windows
- wmux: https://github.com/amirlehmam/wmux

No cmux source code, assets, screenshots, branding, or documentation are included.

## License

MIT. See [LICENSE](LICENSE).
