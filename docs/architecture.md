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
    +-- ConPTY terminal host
    +-- terminal renderer
```

## MVP Data Model

- Window contains workspaces.
- Workspace contains surfaces.
- Surface contains a split tree.
- Split leaves contain panes.
- Panes represent terminal sessions in MVP and browser surfaces later.

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

macOS can verify shared library tests and documentation. Windows is required for WPF, ConPTY, WebView2, keyboard behavior, clipboard behavior, and release readiness.
