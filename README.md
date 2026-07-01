# AgentMux Windows

AgentMux Windows is a lightweight Windows terminal workspace for running coding agents side by side.

It is inspired by the terminal/workspace workflow category popularized by cmux, but it is an independent Windows implementation and is not affiliated with cmux, Manaflow, or the macOS cmux project.

## Goals

- Native Windows desktop app, no Electron app shell.
- ConPTY-backed terminal panes.
- Workspace sidebar, surface tabs, split panes, and keyboard-first navigation.
- Agent-aware notifications through OSC sequences and a CLI.
- Local named-pipe API for automation.
- Session/layout restore.
- Public, reproducible Windows builds.

## Current Status

Pre-alpha scaffold.

This repository currently contains the public-safe foundation: project structure, core models, OSC notification parsing, named-pipe JSON-RPC contracts, CLI skeleton, a minimal WPF shell, tests, and CI. The full ConPTY host and terminal renderer are the next implementation sprint.

## Tech Stack

- C# / .NET 9
- WPF for the Windows shell
- ConPTY for terminal hosting
- WebView2-hosted terminal renderer planned for terminal correctness without Electron
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

## CLI Preview

The CLI talks to a running AgentMux instance over a named pipe:

```powershell
agentmux ping
agentmux status
agentmux notify --title "Codex" --body "Waiting for input"
agentmux workspace list
agentmux workspace create --title "API"
agentmux split right
agentmux send "npm test"
agentmux read-screen --lines 50
```

## Attribution

AgentMux Windows studies public workflow ideas from:

- cmux: https://github.com/manaflow-ai/cmux
- mkurman/cmux-windows: https://github.com/mkurman/cmux-windows
- wmux: https://github.com/amirlehmam/wmux

No cmux source code, assets, screenshots, branding, or documentation are included.

## License

MIT. See [LICENSE](LICENSE).
