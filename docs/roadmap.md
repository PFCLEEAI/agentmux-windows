# Roadmap

## Sprint 0 - Public Scaffold

- Public-safe README, license, notice, security, contributing docs.
- .NET solution with core, CLI, Windows app, ConPTY abstraction, and tests.
- CI that runs core tests everywhere and Windows build on Windows runners.

## Sprint 1 - ConPTY Terminal Core

- Launch PowerShell, CMD, and WSL through ConPTY.
- Input/output pump.
- Resize support.
- Process cleanup and timeout handling.
- Headless Windows smoke tests.

## Sprint 2 - Workspace UI

- Workspace sidebar.
- Surface tabs.
- Split pane layout.
- Focus movement.
- Keyboard shortcuts.

## Sprint 3 - CLI/API

- `ping`, `status`, `tree`.
- Workspace create/list/select.
- Split pane.
- Send text/key.
- Read screen.

## Sprint 4 - Notifications

- OSC 9/99/777 parsing from terminal output.
- CLI `notify`.
- Pane ring and sidebar unread state.
- Notification panel and jump latest unread.

## Sprint 5 - Persistence

- JSON session snapshot.
- Layout restore.
- Working directory restore.
- Scrollback best effort.

## Sprint 6 - Browser Parity

- WebView2 browser pane.
- Open/navigate.
- Snapshot/click/fill/eval.
- Screenshot.

Browser parity starts after the terminal-agent workflow is stable.
