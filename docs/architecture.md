# Architecture

AgentMux Windows is split into small modules so the first implementation can stay light and replaceable.

## Modules

- `AgentMux.Core`: shared models, streaming OSC notification parsing, JSON-RPC contracts, persistence helpers.
- `AgentMux.Cli`: command-line client that sends JSON-RPC commands to the app over a named pipe.
- `AgentMux.Win.Pty`: Windows ConPTY abstraction and native interop.
- `AgentMux.Win.App`: WPF desktop shell, workspace UI, named-pipe server, and app lifecycle.
- `AgentMux.Tests`: cross-platform unit tests for core behavior.

## Runtime Shape

```text
agentmux.exe CLI
    |
    | JSON-RPC over named pipe
    v
AgentMux.Win.App
    |
    +-- workspace/session model
    +-- workspace switcher sidebar and RPC/CLI state
    +-- notification state
    +-- WPF notification center
    +-- best-effort native Windows toast requests
    +-- per-pane ConPTY terminal hosts
    +-- WebView2/xterm terminal renderer bridge
    +-- WebView2 browser pane bridge
```

## MVP Data Model

- Window contains workspaces.
- Workspace contains surfaces.
- Workspace switching is app-level state. `workspace.list`, `workspace.create`, and `workspace.select` expose the same active-workspace model used by the WPF sidebar. The list response returns compact metadata only: ids, titles, indexes, active state, working directory, unread count, active surface summary, active pane id, and pane counts. Selecting by zero-based index or workspace id changes `_activeWorkspaceIndex`, updates the sidebar selected item, re-renders the active surface, queues a session save, and starts the selected workspace's active terminal pane lazily.
- Surface contains a split tree.
- Split leaves contain panes.
- Pane focus movement is computed from the split-tree geometry in `AgentMux.Core`, so CLI/RPC and WPF shortcuts share the same target selection.
- Pane actions use shared Core tree helpers: zoom stores a `ZoomedPaneId` on the surface, while close collapses the split tree to the closed pane's sibling branch.
- Terminal panes own independent ConPTY sessions, started lazily when a pane becomes active or receives input. `surface.send_text` writes submitted text, and `surface.send_key` maps a fixed set of logical terminal key names to VT/control byte sequences before writing them to the active terminal's ConPTY input stream. This is active-terminal automation only, not Windows-wide keyboard injection, trusted physical input, global hotkeys, or browser key automation.
- ConPTY session disposal closes the pane's input pipe, waits briefly for the direct child process to exit, and attempts to kill only that direct child if it is still running after the bounded grace window. This keeps terminal pane shutdown bounded, but it is not process-tree cleanup, Windows Job Object containment, or live terminal process restore.
- Terminal pane dimensions are estimated from the rendered WPF content size and stored on `PaneState.Cols` / `PaneState.Rows`. Size changes update the model, resize the cached xterm view, and resize an existing live ConPTY session through `ResizePseudoConsole` on a best-effort path. The same path is available explicitly through `surface.resize_terminal` / `agentmux pane resize`.
- Terminal panes render through a cached WebView2/xterm bridge with a WPF text fallback. The bridge displays current pane text and keeps `PaneState.LastScreenText` as the active-terminal automation/read-screen source. `surface.read_screen` can return the full active terminal text or a bounded tail through `lines`, with `truncated`, `paneId`, and `paneKind` metadata. Browser panes return empty read-screen text. This preview does not mine xterm's internal scrollback buffer, parse alternate-screen state, or maintain a transcript database. ConPTY output is passed through a lightweight streaming OSC processor before it reaches `LastScreenText`; OSC 9/99/777 notification sequences are stripped from visible text and become in-memory `TerminalNotification` entries, while OSC title and working-directory events update the emitting pane metadata.
- The WPF notification center reads the same capped in-memory notification list as `notifications.list`. Opening or closing it is read-only; its jump-latest and clear-unread actions reuse the same semantics as `notifications.jump_latest` and `notifications.clear`. New notifications also route through `INativeToastService`: production uses a best-effort Windows toast request with length-bounded XML text, while tests inject a recording service so hosted CI proves routing without showing real desktop toasts. Native toast failures are swallowed and do not block in-app notification state. Toast text is unredacted local notification content, and `notifications.clear` clears AgentMux unread state only.
- Browser panes render through a cached WebView2 bridge with a WPF fallback. `PaneState.Url` is the current browser navigation source, and `surface.open_url` converts or navigates the active pane.
- Surface tabs are active-workspace state. `surface.list`, `surface.create`, and `surface.select` expose the same model used by the WPF tab strip. Selecting a surface changes `WorkspaceState.ActiveSurfaceIndex` and re-renders that surface's split tree without disposing hidden panes.
- Browser automation uses the active browser pane and WebView2 directly: `surface.eval_js`, `surface.browser_text`, `surface.click_selector`, `surface.fill_selector`, `surface.browser_type_text`, `surface.browser_press_key`, `surface.capture_screenshot`, `surface.browser_frame_tree`, `surface.browser_wait_for_selector`, `surface.browser_wait_for_load`, `surface.browser_console_log`, `surface.browser_clear_console_log`, `surface.browser_network_log`, `surface.browser_clear_network_log`, `surface.browser_response_body`, `surface.browser_har_metadata`, `surface.browser_trace`, `surface.browser_downloads`, `surface.browser_clear_downloads`, `surface.browser_route_list`, `surface.browser_route_block`, `surface.browser_route_fulfill`, and `surface.browser_route_clear`. These are local trust-boundary APIs; JavaScript executes in the active browser pane, browser-text preview returns capped rendered `innerText`/`textContent` from the active document or selector, click/type/press use WebView2 input automation where practical, screenshots write to local paths, frame-tree preview returns the WebView2/CDP `Page.getFrameTree` shape for inspection, wait-for-selector preview polls bounded `visible`/`attached`/`hidden` selector state for one active-pane call, wait-load preview polls bounded active-pane `domcontentloaded`/`load`/preview `network-idle` readiness, console-log preview records compact CDP Runtime console messages in memory, network-log preview records compact CDP Network event metadata in memory, explicit response-body preview calls WebView2/CDP `Network.getResponseBody` for one caller-provided request id already present in that active pane's network log, metadata-only HAR-like preview writes currently retained network metadata to a caller-provided filesystem path, trace preview uses WebView2/CDP `Tracing.start`/`Tracing.end` with a fixed category preset and writes a bounded one-shot JSON trace to a caller-provided filesystem path, download preview records compact WebView2 download metadata in memory while routing files under `%LOCALAPPDATA%/AgentMux/Downloads`, and route preview uses WebView2/CDP Fetch interception to block or fulfill substring-matched active-pane requests until cleared. Synthetic fulfill responses include the caller-provided content type plus a fixed `Access-Control-Allow-Origin: *` header. Browser-text results, console messages, and response bodies may contain tokens, personal data, API responses, localStorage-derived values, auth callbacks, or private document text; they are returned unredacted to same-user local callers and are not copied into the session snapshot by the browser pane. Browser text defaults to 10,000 characters and is hard-capped at 100,000 characters; console messages are capped at 4,096 characters each; explicit response-body results are capped at 1,000,000 characters. The network log, wait-load `network-idle`, and HAR-like export avoid headers, cookies, request/response bodies, and post data by default, but retained URLs may carry tokens, account ids, query params, auth callbacks, or other sensitive data. Trace files may contain URLs, page titles, frame ids, user-timing labels, script/source names, timing, process/thread metadata, and content-derived browser data; the browser pane returns only compact metadata over RPC and does not copy trace data, trace paths, or trace tokens into the session snapshot. Route rules and route hits are transient and not copied into the session snapshot; `route list` still returns full recent hit URLs to same-user local callers, and synthetic fulfill bodies are stored in memory until rules are cleared. CLI stdout, automation callers, and caller-chosen export paths can still persist sensitive output outside that browser-pane boundary. The download log includes URLs and local result paths, clears only in-memory metadata, and does not delete downloaded files. Text extraction, selector actions, and selector waits can optionally target a same-origin `iframe`/`frame` by top-document `name` or `id`; cross-origin frame automation, full DOM snapshotting, form-value scraping, full Playwright-style load-state/network-idle parity, persistent selector watches, browser-context routing, caller-controlled response header mutation beyond status/content type/body, header/cookie/post-data capture, request-body capture, automatic response capture, full HAR export, full browser-context tracing, log streaming, source-map expansion, telemetry, malware scanning, pause/resume/cancel download control, and full Playwright-style browser control are intentionally out of scope for this preview.
- App shortcuts are matched inside the focused WPF shell from `%LOCALAPPDATA%/AgentMux/shortcuts.json`, with hard-coded defaults used when the file or individual entries are missing or invalid. They are app-window bindings, not global hotkeys or IPC commands.

## Persistence

The WPF shell persists session state through `SessionSnapshotStore` at `%LOCALAPPDATA%/AgentMux/session.json`. Saves are serialized and written through a temp file before replacing the snapshot. Startup restores workspaces, active workspace, surfaces, active surface index, split tree, active pane, pane titles, browser URLs, and last terminal screen text; malformed or unreadable snapshots fall back to one default workspace. Notification bodies, unread markers, browser text extraction results, browser console messages, network events, response bodies, HAR exports, trace exports, download logs, and browser route rules/hits are not serialized into the session snapshot. Live ConPTY processes are not serialized or resumed. Restored terminal panes start fresh sessions lazily when they become active or receive input.

## Packaging

CI publishes a lightweight framework-dependent Windows artifact after the Windows smoke gates pass. The package includes the WPF app at the root, the CLI under `cli\`, runtime config, dependency files, WebView2/xterm assets, package provenance metadata, `EVIDENCE.json`, SHA-256 checksums, README, license, a no-admin user install helper, the manual desktop-smoke runbook/helper, and a manual evidence verifier. CI also runs `cli\agentmux.exe --help` from the packaged output.

Packaging is centralized in `tools/package-windows.ps1` so CI artifacts and release ZIPs use the same layout and required-file gate. `PACKAGE.json` records package layout and entrypoint provenance, while `EVIDENCE.json` records expected hosted gates, related smoke artifacts, checksum coverage, proof boundaries, and the still-required manual desktop gate. CI and Release both run the manual desktop-smoke helper in `-SkipLaunch` mode against the packaged output to verify its deterministic preflight path, then run `tools\verify-manual-desktop-evidence.ps1 -PreflightOnly` to verify helper output shape, strict package checksum coverage, and the `manualGate.required` / `releaseReadiness.blocked` boundary. CI and Release also run `tools\install-user.ps1` into a temporary user-local install root with `-SkipPathUpdate` to verify package installability without mutating runner PATH. These checks are not substitutes for the real desktop smoke gate. The install helper copies an extracted package to `%LOCALAPPDATA%\Programs\AgentMux` by default, creates `agentmux.cmd` and `agentmux-app.cmd` shims under the install `bin` directory, verifies `SHA256SUMS.txt`, and only changes the current user's PATH unless skipped. The Release workflow builds the package on Windows, compresses the package contents directly into `agentmux-windows-<version>-framework-dependent.zip`, emits an outer `.sha256`, expands the ZIP back out, verifies the root layout, reruns CLI help smoke, checks extracted package checksums, and creates a GitHub prerelease only from `v*` tag pushes. It intentionally avoids signed installer/MSIX/winget distribution and a self-contained runtime bundle until release readiness is higher.

## IPC

The local API uses one request per named-pipe connection with a JSON-RPC-like envelope:

```json
{"id":"...","method":"workspace.list","params":{}}
```

Responses are JSON:

```json
{"id":"...","ok":true,"result":{},"error":null}
```

The default pipe is per-user named and created with .NET's current-user-only pipe option. Any local process running as the same user that can access that pipe should be treated as able to invoke local automation APIs against the active AgentMux window.

## Verification Boundary

macOS can verify shared library tests, Windows-targeted builds, and documentation. Hosted Windows CI verifies WPF composition, workspace create/list/select/render/restore behavior, surface tab create/select/restore behavior, notification center open/jump/clear behavior, native-toast routing through an injected recording service, terminal pane size propagation, xterm runtime input events, renderer bridge input, and synthetic DOM keydown dispatch from the hosted WebView2/xterm runtime into the active pane's ConPTY input path, logical send-key dispatch and raw ConPTY byte delivery, session restore smoke with a temp snapshot, corrupt snapshot fallback, shortcut dispatch, live OSC notification capture through the terminal-output path, WebView2 runtime smoke with PNG artifacts, active browser RPC automation including text extraction, same-origin frame-targeted actions, frame-tree inspection, loopback-backed network event capture, explicit loopback-backed response-body retrieval, metadata-only HAR-like export, bounded WebView2/CDP trace export, route block/fulfill/clear preview behavior, loopback-backed download capture, ConPTY output/input/raw-control-sequence/resize smoke, bounded direct-child ConPTY cleanup on disposal, Windows builds, framework-dependent package creation, user install helper preflight, and non-launch manual helper preflight. Hosted CI does not prove physical keyboard input, trusted OS input, native focus/IME behavior, global shortcut capture, human-visible desktop usability, or that a human saw a native Windows toast. A true manual Windows desktop smoke is still required for physical keyboard behavior, visible desktop usability, native-toast visibility under real desktop notification settings, and release readiness.
