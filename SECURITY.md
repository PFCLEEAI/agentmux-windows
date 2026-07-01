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
- browser session data once browser surfaces are implemented.

Do not paste tokens, passwords, SSH keys, or API keys into issue reports.
