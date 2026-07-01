# Security Policy

## Supported Versions

This project is pre-alpha. Security fixes target the default branch until tagged releases exist.

## Reporting A Vulnerability

Open a private security advisory on GitHub when available, or create an issue with minimal reproduction details and no secrets.

## Security Boundaries

AgentMux Windows launches local shells and exposes a local automation API. The intended default boundary is the current Windows user session only. Any local process that can access the user's AgentMux named pipe should be treated as able to automate the active AgentMux window.

Security-sensitive areas:

- named-pipe API authorization,
- shell command execution,
- transcript and scrollback storage,
- notification and status text,
- environment variables,
- browser session data,
- browser console messages, browser network/download metadata, and downloaded files under the user's local app data directory.
- manual desktop-smoke evidence, including screenshots, terminal output, local paths, URLs, notification text, helper metadata, and copied CLI output.

Browser preview boundaries:

- Terminal/agent notifications are local and in-memory, capped at the most recent 200 events. Terminal OSC 9/99/777 notification payloads and `agentmux notify` bodies may contain task names, prompts, file paths, or private output. AgentMux strips OSC notification sequences from visible terminal text and does not serialize notification bodies into the session snapshot, but `notifications list` returns bodies unredacted to same-user local callers. CLI stdout can still persist notification contents in terminal scrollback, shell transcripts, redirected files, or logs.
- The browser wait-for-selector helper is local, active-pane scoped, and one-shot. It caps caller-provided timeouts at 30 seconds, does not persist selector state, has no telemetry, and follows the same same-origin frame boundary as selector actions. CLI stdout can still persist selector/frame names and page-state results in terminal scrollback, shell transcripts, redirected files, or logs.
- The browser console log is local and in-memory. Console messages may contain tokens, personal data, API responses, localStorage-derived values, auth callbacks, or private document text. They are returned unredacted to same-user local callers, capped at 4,096 characters each with a truncation marker, and not stored in the session snapshot by the browser pane. CLI stdout can still persist messages in terminal scrollback, shell transcripts, redirected files, or logs.
- The browser network log is local and in-memory. It avoids headers, cookies, request/response bodies, and post data, but URLs can still contain sensitive query data.
- Response-body retrieval is explicit and request-id scoped through the active browser pane. Returned bodies may contain tokens, personal data, session data, API responses, or private document contents. They are returned unredacted to the local caller, capped at 1,000,000 characters with a truncation marker, and not stored in the network log or session snapshot by the browser pane. CLI stdout can still persist in terminal scrollback, shell transcripts, redirected files, or logs.
- HAR metadata export is an explicit path-scoped, metadata-only HAR-like preview for the active browser pane's currently retained in-memory network events. It writes to a caller-provided filesystem path and persists until the caller deletes it. It omits headers, cookies, post data, response bodies, downloaded file contents, tracing data, interception state, replay data, and telemetry, but full URLs may still contain tokens, account ids, query params, auth callbacks, or other secrets. The destination path may be a repository, synced folder, backup location, or shared folder.
- The browser download log is local and in-memory. It includes URLs and local result paths, routes downloaded files under `%LOCALAPPDATA%\AgentMux\Downloads`, and `downloads-clear` clears only metadata, not downloaded files.
- AgentMux does not scan downloads, quarantine files, auto-open downloads, upload downloaded files, or provide a download policy engine in this pre-alpha preview.

Manual desktop-smoke evidence boundaries:

- The manual proof helper creates local files only under the selected evidence folder, defaults to the user's temp directory, does not upload evidence, does not read browser history or credentials, does not require administrator privileges, and may launch AgentMux unless `-SkipLaunch` is used.
- Manual evidence may still contain local paths, URLs, terminal output, notification text, screenshots, browser console messages, network/download metadata, response bodies, or HAR metadata if the tester chooses to collect those outputs. Use disposable terminal commands and public or local demo pages only.
- Remove tokens, passwords, SSH keys, API keys, private URLs, account ids, credentials, proprietary output, browser cookies, and private prompts before sharing evidence.
- Hosted CI evidence is not a substitute for physical keyboard and visible Windows desktop proof.

Do not paste tokens, passwords, SSH keys, or API keys into issue reports.
