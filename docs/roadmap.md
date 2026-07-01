# Roadmap

## Sprint 0 - Public Scaffold

- Public-safe README, license, notice, security, contributing docs.
- .NET solution with core, CLI, Windows app, ConPTY abstraction, and tests.
- CI that runs core tests everywhere and Windows build on Windows runners.

## Sprint 1 - ConPTY Terminal Core

- Launch a shell through ConPTY. Passing in hosted Windows smoke for process output and stdin echo.
- Input/output pump. Passing in hosted Windows smoke.
- Resize support. In progress.
- Process cleanup and timeout handling. In progress.
- Headless Windows smoke tests. Passing as a blocking GitHub Actions gate; real Windows desktop smoke still required before release-ready status.

## Sprint 2 - Workspace UI

- Workspace sidebar. Implemented in WPF shell.
- Surface tabs.
- Split pane layout. Implemented as recursive WPF panes with persisted split ratios.
- WebView2/xterm renderer bridge. Preview implemented with WPF fallback.
- Focus movement. Preview implemented with `surface.focus_pane`, `agentmux focus`, `Ctrl+Tab`, and `Ctrl+Alt+Arrow`.
- Keyboard shortcuts. Basic pane cycling, directional pane focus, pane zoom/close shortcuts, and JSON shortcut remapping are implemented; a full settings UI remains future work.
- Each terminal pane now owns an independent ConPTY session started lazily.

## Sprint 3 - CLI/API

- `ping`, `status`, `tree`.
- Workspace create/list/select.
- Split pane. Preview implemented.
- Focus pane. Preview implemented.
- Zoom and close pane. Preview implemented.
- Send text. Preview implemented.
- Send key.
- Read screen. Preview implemented for active pane text.

## Sprint 4 - Notifications

- OSC 9/99/777 parsing from terminal output.
- CLI `notify`.
- Pane ring and sidebar unread state.
- Notification panel and jump latest unread.

## Sprint 5 - Persistence

- JSON session snapshot.
- Layout restore. Core split tree and active pane id round-trip through JSON snapshot tests.
- Working directory restore.
- Scrollback best effort.

## Sprint 5.5 - Windows UI Smoke

- WPF split-pane composition smoke. Implemented in Windows CI.
- Real visible desktop smoke. Still required before release-ready status.

## Sprint 5.6 - Packaging

- Framework-dependent Windows CI package. Implemented as `agentmux-windows-package` after smoke gates pass.
- Package provenance, checksums, and packaged CLI smoke. Implemented in CI.
- Installer/MSIX/signing. Future work after real desktop QA.

## Sprint 6 - Browser Parity

- WebView2 browser pane. Preview implemented with WPF fallback.
- Open/navigate. Preview implemented through `surface.open_url`, `agentmux open-url`, and the browser pane address bar.
- Eval/click/fill/type/press. Preview implemented through lightweight WebView2 helpers; click/type/press use WebView2 input automation where practical.
- Screenshot. Preview implemented as WebView2 PNG capture to a local path.

Browser automation has hosted WebView2 runtime smoke and CI PNG artifacts. Click/type/press now cover richer pointer/key preview semantics, but full Playwright-style automation such as frames, downloads, tracing, network interception, and trusted physical input remains future work.
