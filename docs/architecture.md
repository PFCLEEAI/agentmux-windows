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
    +-- WebView2 terminal renderer bridge
```

## MVP Data Model

- Window contains workspaces.
- Workspace contains surfaces.
- Surface contains a split tree.
- Split leaves contain panes.
- Terminal panes own independent ConPTY sessions, started lazily when a pane becomes active or receives input.
- Terminal panes render through a cached WebView2 bridge with a WPF text fallback. The bridge displays current pane text and keeps `PaneState.LastScreenText` as the automation/read-screen source.
- Browser panes are planned for a later sprint.

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

macOS can verify shared library tests, Windows-targeted builds, and documentation. Hosted Windows CI verifies WPF composition, ConPTY output/input smoke, and Windows builds. A visible Windows desktop smoke is still required for WebView2 runtime behavior, keyboard behavior, clipboard behavior, and release readiness.
