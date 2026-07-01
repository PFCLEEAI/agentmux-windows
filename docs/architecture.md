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
- Terminal panes render through a cached WebView2/xterm bridge with a WPF text fallback. The bridge displays current pane text and keeps `PaneState.LastScreenText` as the automation/read-screen source.
- Browser panes render through a cached WebView2 bridge with a WPF fallback. `PaneState.Url` is the current browser navigation source, and `surface.open_url` converts or navigates the active pane.
- Browser automation uses the active browser pane and WebView2 directly: `surface.eval_js`, `surface.click_selector`, `surface.fill_selector`, `surface.browser_type_text`, `surface.browser_press_key`, and `surface.capture_screenshot`. These are local trust-boundary APIs; JavaScript executes in the active browser pane, click/type/press use WebView2 input automation where practical, and screenshots write to local paths.
- App shortcuts are matched inside the focused WPF shell from `%LOCALAPPDATA%/AgentMux/shortcuts.json`, with hard-coded defaults used when the file or individual entries are missing or invalid. They are app-window bindings, not global hotkeys or IPC commands.

## Packaging

CI publishes a lightweight framework-dependent Windows artifact after the Windows smoke gates pass. The package includes the WPF app at the root, the CLI under `cli\`, runtime config, dependency files, WebView2/xterm assets, README, and license. It intentionally avoids an installer and self-contained runtime bundle until release readiness is higher.

## IPC

The local API uses one request per named-pipe connection with a JSON-RPC-like envelope:

```json
{"id":"...","method":"workspace.list","params":{}}
```

Responses are JSON:

```json
{"id":"...","ok":true,"result":{},"error":null}
```

The default pipe is per-user named and intended for the current user session only.

## Verification Boundary

macOS can verify shared library tests, Windows-targeted builds, and documentation. Hosted Windows CI verifies WPF composition, shortcut dispatch, WebView2 runtime smoke with PNG artifacts, ConPTY output/input smoke, Windows builds, and framework-dependent package creation. A true manual Windows desktop smoke is still required for physical keyboard behavior, clipboard behavior, and release readiness.
