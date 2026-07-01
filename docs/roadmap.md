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
- Focus movement.
- Keyboard shortcuts.
- Each terminal pane now owns an independent ConPTY session started lazily.

## Sprint 3 - CLI/API

- `ping`, `status`, `tree`.
- Workspace create/list/select.
- Split pane. Preview implemented.
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

## Sprint 6 - Browser Parity

- WebView2 browser pane.
- Open/navigate.
- Snapshot/click/fill/eval.
- Screenshot.

Browser parity starts after the terminal-agent workflow is stable.
