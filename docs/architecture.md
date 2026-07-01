# Architecture

AgentMux Windows is split into small modules so the first implementation can stay light and replaceable.

## Modules

- `AgentMux.Core`: shared models, OSC notification parsing, JSON-RPC contracts, persistence helpers.
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
    +-- notification state
    +-- per-pane ConPTY terminal hosts
    +-- WebView2/xterm terminal renderer bridge
    +-- WebView2 browser pane bridge
```

## MVP Data Model

- Window contains workspaces.
- Workspace contains surfaces.
- Surface contains a split tree.
- Split leaves contain panes.
- Pane focus movement is computed from the split-tree geometry in `AgentMux.Core`, so CLI/RPC and WPF shortcuts share the same target selection.
- Pane actions use shared Core tree helpers: zoom stores a `ZoomedPaneId` on the surface, while close collapses the split tree to the closed pane's sibling branch.
- Terminal panes own independent ConPTY sessions, started lazily when a pane becomes active or receives input.
- Terminal pane dimensions are estimated from the rendered WPF content size and stored on `PaneState.Cols` / `PaneState.Rows`. Size changes update the model, resize the cached xterm view, and resize an existing live ConPTY session through `ResizePseudoConsole` on a best-effort path. The same path is available explicitly through `surface.resize_terminal` / `agentmux pane resize`.
- Terminal panes render through a cached WebView2/xterm bridge with a WPF text fallback. The bridge displays current pane text and keeps `PaneState.LastScreenText` as the automation/read-screen source.
- Browser panes render through a cached WebView2 bridge with a WPF fallback. `PaneState.Url` is the current browser navigation source, and `surface.open_url` converts or navigates the active pane.
- Browser automation uses the active browser pane and WebView2 directly: `surface.eval_js`, `surface.click_selector`, `surface.fill_selector`, `surface.browser_type_text`, `surface.browser_press_key`, `surface.capture_screenshot`, `surface.browser_frame_tree`, `surface.browser_network_log`, `surface.browser_clear_network_log`, `surface.browser_response_body`, `surface.browser_downloads`, and `surface.browser_clear_downloads`. These are local trust-boundary APIs; JavaScript executes in the active browser pane, click/type/press use WebView2 input automation where practical, screenshots write to local paths, frame-tree preview returns the WebView2/CDP `Page.getFrameTree` shape for inspection, network-log preview records compact CDP Network event metadata in memory, explicit response-body preview calls WebView2/CDP `Network.getResponseBody` for one caller-provided request id already present in that active pane's network log, and download preview records compact WebView2 download metadata in memory while routing files under `%LOCALAPPDATA%/AgentMux/Downloads`. The network log avoids headers, cookies, request/response bodies, and post data by default, but it does include URLs, which may carry sensitive query data. Explicit response-body results may contain sensitive page data, are capped at 1,000,000 characters with a truncation marker, and are not copied into the network log or session snapshot by the browser pane. CLI stdout and automation callers can still persist the returned body outside that browser-pane boundary. The download log includes URLs and local result paths, clears only in-memory metadata, and does not delete downloaded files. Selector actions can optionally target a same-origin `iframe`/`frame` by top-document `name` or `id`; cross-origin frame automation, request interception/mutation, automatic response capture, response header/cookie capture, HAR export, tracing, telemetry, malware scanning, pause/resume/cancel download control, and full Playwright-style browser control are intentionally out of scope for this preview.
- App shortcuts are matched inside the focused WPF shell from `%LOCALAPPDATA%/AgentMux/shortcuts.json`, with hard-coded defaults used when the file or individual entries are missing or invalid. They are app-window bindings, not global hotkeys or IPC commands.

## Persistence

The WPF shell persists session state through `SessionSnapshotStore` at `%LOCALAPPDATA%/AgentMux/session.json`. Saves are serialized and written through a temp file before replacing the snapshot. Startup restores workspaces, active workspace, split tree, active pane, pane titles, browser URLs, and last terminal screen text; malformed or unreadable snapshots fall back to one default workspace. Live ConPTY processes are not serialized or resumed. Restored terminal panes start fresh sessions lazily when they become active or receive input.

## Packaging

CI publishes a lightweight framework-dependent Windows artifact after the Windows smoke gates pass. The package includes the WPF app at the root, the CLI under `cli\`, runtime config, dependency files, WebView2/xterm assets, package provenance metadata, SHA-256 checksums, README, and license. CI also runs `cli\agentmux.exe --help` from the packaged output.

Packaging is centralized in `tools/package-windows.ps1` so CI artifacts and release ZIPs use the same layout and required-file gate. The Release workflow builds the package on Windows, compresses the package contents directly into `agentmux-windows-<version>-framework-dependent.zip`, emits an outer `.sha256`, expands the ZIP back out, verifies the root layout, reruns CLI help smoke, checks extracted package checksums, and creates a GitHub prerelease only from `v*` tag pushes. It intentionally avoids an installer and self-contained runtime bundle until release readiness is higher.

## IPC

The local API uses one request per named-pipe connection with a JSON-RPC-like envelope:

```json
{"id":"...","method":"workspace.list","params":{}}
```

Responses are JSON:

```json
{"id":"...","ok":true,"result":{},"error":null}
```

The default pipe is per-user named and intended for the current user session only. Any local process that can access that pipe should be treated as able to invoke local automation APIs against the active AgentMux window.

## Verification Boundary

macOS can verify shared library tests, Windows-targeted builds, and documentation. Hosted Windows CI verifies WPF composition, terminal pane size propagation, session restore smoke with a temp snapshot, corrupt snapshot fallback, shortcut dispatch, WebView2 runtime smoke with PNG artifacts, active browser RPC automation including same-origin frame-targeted actions, frame-tree inspection, loopback-backed network event capture, explicit loopback-backed response-body retrieval, and loopback-backed download capture, ConPTY output/input/resize smoke, Windows builds, and framework-dependent package creation. A true manual Windows desktop smoke is still required for physical keyboard behavior, clipboard behavior, and release readiness.
