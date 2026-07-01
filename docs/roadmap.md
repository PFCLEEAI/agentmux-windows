# Roadmap

## Sprint 0 - Public Scaffold

- Public-safe README, license, notice, security, contributing docs.
- .NET solution with core, CLI, Windows app, ConPTY abstraction, and tests.
- CI that runs core tests everywhere and Windows build on Windows runners.

## Sprint 1 - ConPTY Terminal Core

- Launch a shell through ConPTY. Passing in hosted Windows smoke for process output and stdin echo.
- Input/output pump. Passing in hosted Windows smoke.
- Resize support. Preview implemented through WPF pane-size propagation, explicit `surface.resize_terminal`, and hosted ConPTY resize smoke.
- Process cleanup and timeout handling. Preview implemented for the direct ConPTY child: disposal closes input, waits briefly for child exit, and attempts to kill the direct child if it is still running after a bounded timeout. Full process-tree cleanup and Windows Job Object containment remain future work.
- Headless Windows smoke tests. Passing as a blocking GitHub Actions gate; real Windows desktop smoke still required before release-ready status.

## Sprint 2 - Workspace UI

- Workspace sidebar. Implemented in WPF shell.
- Workspace switching. Preview implemented with `workspace.list`, `workspace.create`, `workspace.select`, compact workspace DTOs, index/id selection, sidebar selected-state updates, render switching, and hosted app-smoke coverage.
- Surface tabs. Preview implemented with WPF active-workspace tabs, `+ Surface`, `surface.list`, `surface.create`, `surface.select`, and hosted app-smoke coverage.
- Split pane layout. Implemented as recursive WPF panes with persisted split ratios.
- WebView2/xterm renderer bridge. Preview implemented with WPF fallback.
- Focus movement. Preview implemented with `surface.focus_pane`, `agentmux focus`, `Ctrl+Tab`, and `Ctrl+Alt+Arrow`.
- Keyboard shortcuts. Basic pane cycling, directional pane focus, pane zoom/close shortcuts, and JSON shortcut remapping are implemented; a full settings UI remains future work.
- Each terminal pane now owns an independent ConPTY session started lazily.

## Sprint 3 - CLI/API

- `ping`, `status`, `tree`.
- Workspace create/list/select. Preview implemented with compact list DTOs, create-and-select behavior, zero-based index or id selection, CLI aliases, and hosted app-smoke coverage.
- Surface list/create/select. Preview implemented for active-workspace surfaces through RPC, CLI, and WPF tabs.
- Split pane. Preview implemented.
- Focus pane. Preview implemented.
- Zoom and close pane. Preview implemented.
- Send text. Preview implemented.
- Send key. Preview implemented with Enter/Tab/Escape/navigation/function keys, generic Ctrl letter/symbol keys, Alt/Meta-prefixed terminal keys, RPC/CLI coverage, and hosted raw-byte ConPTY smoke; trusted physical input remains future work.
- Resize terminal. Preview implemented for the active terminal pane.
- Read screen. Preview implemented for active terminal `LastScreenText`, with optional bounded `--lines` tail output and hosted app-smoke coverage.

## Sprint 4 - Notifications

- OSC 9/99/777 parsing from terminal output. Preview implemented with chunk-safe OSC extraction before terminal text is stored/rendered.
- CLI `notify`. Preview implemented.
- Pane ring and sidebar unread state. Preview implemented as in-memory workspace unread counts and pane unread markers.
- Notification list/clear and jump latest unread. Preview implemented through `notifications.list`, `notifications.clear`, `notifications.jump_latest`, and `agentmux notifications ...`.
- Lightweight in-app notification center. Preview implemented with unread count, recent notification list, jump latest unread, clear unread, and read-only open/close behavior.
- Native Windows toast integration, filters/search, rich rendering, and persistent notification history remain future work.

## Sprint 5 - Persistence

- JSON session snapshot. Implemented through `SessionSnapshotStore`.
- App-level layout restore. Implemented for workspaces, active workspace, surfaces, active surface, split layout, active pane, pane titles, browser URLs, and last terminal screen text.
- Corrupt snapshot fallback. Implemented so bad `session.json` starts with a default workspace.
- Working directory restore. Implemented in the saved model for workspaces and panes.
- Last screen text best effort. Preview implemented through `PaneState.LastScreenText`; full xterm scrollback, transcript storage, and live terminal process restore are not implemented.

## Sprint 5.5 - Windows UI Smoke

- WPF split-pane composition smoke. Implemented in Windows CI.
- WPF session restore smoke. Implemented in Windows CI with a temp snapshot store, including multi-surface active-surface restore.
- Manual desktop-smoke proof packet. Implemented as a public runbook plus packaged PowerShell helper for collecting Windows desktop evidence.
- Real visible desktop smoke. Still required before release-ready status.

## Sprint 5.6 - Packaging

- Framework-dependent Windows CI package. Implemented as `agentmux-windows-package` after smoke gates pass.
- Package provenance, checksums, and packaged CLI smoke. Implemented in CI.
- Packaged manual desktop-smoke runbook/helper. Implemented so testers can run the proof packet from an extracted package.
- Shared package script. Implemented so CI artifacts and release assets use the same required-file and CLI-smoke gate.
- Prerelease ZIP workflow. Implemented for `v*` tags with ZIP round-trip smoke and outer SHA-256 checksum.
- Installer/MSIX/signing. Future work after real desktop QA.

## Sprint 6 - Browser Parity

- WebView2 browser pane. Preview implemented with WPF fallback.
- Open/navigate. Preview implemented through `surface.open_url`, `agentmux open-url`, and the browser pane address bar.
- Eval/click/fill/type/press. Preview implemented through lightweight WebView2 helpers; click/type/press use WebView2 input automation where practical, and click/fill/type/press can target same-origin child frames by name or id.
- Screenshot. Preview implemented as WebView2 PNG capture to a local path.
- Frame tree. Preview implemented through WebView2/CDP `Page.getFrameTree` for active-pane inspection.
- Wait for selector. Preview implemented as bounded active-pane polling for visible, attached, or hidden selector states.
- Wait for load. Preview implemented as bounded active-pane polling for `domcontentloaded`, `load`, or preview `network-idle` readiness.
- Console log. Preview implemented through compact in-memory WebView2/CDP Runtime console events for the active browser pane, with capped unredacted messages returned only to same-user local callers.
- Network event log. Preview implemented through compact in-memory WebView2/CDP Network events for the active browser pane.
- Response body lookup. Preview implemented as explicit WebView2/CDP `Network.getResponseBody` retrieval for one recent request id.
- Metadata-only HAR-like export. Preview implemented for currently retained active-pane network metadata, written to a caller-provided filesystem path.
- Download event log. Preview implemented through compact in-memory WebView2 download events for the active browser pane, with files routed to a local AgentMux download folder.

Browser automation has hosted WebView2 runtime smoke and CI PNG artifacts. Click/type/press now cover richer pointer/key preview semantics, same-origin frame-targeted selector actions cover the first actionable frame-parity step, frame-tree inspection exposes the active pane's frame structure, wait-for-selector covers one-shot DOM readiness synchronization, wait-load covers bounded active-pane `domcontentloaded`/`load`/preview `network-idle` readiness, console-log preview captures recent capped JavaScript console messages without session-snapshot persistence, network-log preview captures recent request/response/failure metadata without headers or bodies by default, response-body preview explicitly retrieves one capped request body by request id, metadata-only HAR-like preview exports retained network metadata without headers/cookies/post data/body text, and download-log preview captures completed download metadata plus deterministic local file output. Full Playwright-style automation such as cross-origin frame control, full browser-context load-state/network-idle parity, persistent selector watches, request interception/mutation, automatic response/header/cookie capture, full HAR export, tracing, log streaming, source-map expansion, malware scanning, pause/resume/cancel download control, and trusted physical input remains future work.
