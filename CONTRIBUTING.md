# Contributing

AgentMux Windows is early. Keep changes small and evidence-backed.

## Development Rules

- Keep the app lightweight. Do not add Electron.
- Keep terminal hosting and UI boundaries separate.
- Do not copy source, assets, screenshots, or documentation from GPL cmux.
- Add tests for parser, IPC, persistence, and command behavior.
- Treat Windows runtime behavior as unproven until a Windows runner or real Windows machine verifies it.

## Local Checks

```powershell
dotnet test tests/AgentMux.Tests/AgentMux.Tests.csproj
dotnet build AgentMux.sln -c Release
```

The full app build requires Windows.
