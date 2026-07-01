# Security Policy

## Supported Versions

This project is pre-alpha. Security fixes target the default branch until tagged releases exist.

## Reporting A Vulnerability

Open a private security advisory on GitHub when available, or create an issue with minimal reproduction details and no secrets.

## Security Boundaries

AgentMux Windows launches local shells and exposes a local automation API. The intended default boundary is the current Windows user session only.

Security-sensitive areas:

- named-pipe API authorization,
- shell command execution,
- transcript and scrollback storage,
- notification and status text,
- environment variables,
- browser session data,
- browser network/download metadata and downloaded files under the user's local app data directory.

Browser preview boundaries:

- The browser network log is local and in-memory. It avoids headers, cookies, request/response bodies, and post data, but URLs can still contain sensitive query data.
- The browser download log is local and in-memory. It includes URLs and local result paths, routes downloaded files under `%LOCALAPPDATA%\AgentMux\Downloads`, and `downloads-clear` clears only metadata, not downloaded files.
- AgentMux does not scan downloads, quarantine files, auto-open downloads, upload downloaded files, or provide a download policy engine in this pre-alpha preview.

Do not paste tokens, passwords, SSH keys, or API keys into issue reports.
