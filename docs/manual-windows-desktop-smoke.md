# Manual Windows Desktop Smoke

This runbook is the real desktop proof gate for AgentMux Windows. Hosted GitHub Actions smoke tests prove build, WPF composition, WebView2 runtime basics, ConPTY IO, package layout, and artifact checksums. They do not prove a human can see and use the app on a Windows desktop with a physical keyboard.

Run this on a normal Windows desktop session before calling a build release-ready.

## What This Proves

- The WPF window is visible and usable.
- Terminal panes show live ConPTY output.
- Physical keyboard input reaches the active terminal pane.
- Split-pane focus, zoom, close, and custom shortcut paths behave in the desktop shell.
- Browser panes visibly navigate and respond to preview automation.
- OSC agent notifications become local unread notification state without showing raw escape text.
- Native Windows toast preview can be observed when Windows accepts local toasts for the app.
- CLI/RPC commands can control the running desktop app.

## What This Does Not Prove

- Cross-origin frame automation.
- Full Playwright-compatible browser semantics.
- Native Windows toast visibility across every Windows notification setting, Focus Assist/Do Not Disturb state, elevation mode, or enterprise policy.
- Global hotkeys.
- Installer, signing, or self-contained runtime packaging.
- Full process-tree cleanup, Windows Job Object containment, or live terminal process restore.
- Security of private prompts, credentials, or browser session data.

## Prerequisites

- Windows 10 1809 or newer, or Windows 11.
- .NET 9 Desktop Runtime.
- Microsoft Edge WebView2 Runtime.
- A normal interactive desktop session, not only a hosted CI runner.
- A physical keyboard or the actual keyboard device used by the target user.
- No administrator privileges are required for this smoke.

## Get A Package

Use one of these paths:

- Release ZIP: download `agentmux-windows-<version>-framework-dependent.zip` and the sibling `.sha256` file from a GitHub prerelease or release workflow artifact.
- CI package: download the `agentmux-windows-package` artifact from a green CI run.
- Local build: build on Windows, then run `.\tools\package-windows.ps1 -OutputPath .\artifacts\agentmux-windows-package -Configuration Release -NoBuild`.

Extract the package to a short local path such as:

```powershell
C:\AgentMuxSmoke\agentmux-windows-package
```

Avoid running from synced folders or paths containing secrets if you plan to share evidence.

## Optional User Install

To install the extracted package for the current user before the smoke:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\tools\install-user.ps1
```

The helper verifies package checksums, copies the extracted package to `%LOCALAPPDATA%\Programs\AgentMux`, creates `agentmux.cmd` and `agentmux-app.cmd` shims, and updates only the current user's PATH unless `-SkipPathUpdate` is used. It does not install runtimes, write to Program Files, create services, or replace the manual visible desktop smoke.

Packages include `EVIDENCE.json` beside `PACKAGE.json` and `SHA256SUMS.txt`. Treat it as audit metadata for the package commit/run, hosted smoke gates, related CI artifact names, and known proof boundaries. It is not a signature, publisher-authenticity proof, or a substitute for the manual checks below.

From a repo checkout, pass the package explicitly:

```powershell
.\tools\install-user.ps1 -PackagePath C:\AgentMuxSmoke\agentmux-windows-package -InstallRoot "$env:LOCALAPPDATA\Programs\AgentMux"
```

Open a new terminal after a PATH update. Package checksums verify local package integrity; they do not prove publisher authenticity by themselves.

## Run The Helper

From a repo checkout:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\tools\manual-desktop-smoke.ps1 -PackagePath C:\AgentMuxSmoke\agentmux-windows-package
```

From an extracted package that includes this helper:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\tools\manual-desktop-smoke.ps1
```

For a release ZIP:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\tools\manual-desktop-smoke.ps1 -PackagePath C:\Downloads\agentmux-windows-v0.1.0-framework-dependent.zip
```

The helper creates a timestamped evidence folder, verifies package checksums when available, requires a sibling `.sha256` when `-PackagePath` points to a release ZIP, captures environment, package metadata, evidence-manifest content, and CLI help output, launches `AgentMux.exe` unless `-SkipLaunch` is set, and writes `manual-checklist.md`.

If you already launched the app manually:

```powershell
.\tools\manual-desktop-smoke.ps1 -PackagePath C:\AgentMuxSmoke\agentmux-windows-package -SkipLaunch
```

If you want the helper to run local named-pipe RPC checks after launch:

```powershell
.\tools\manual-desktop-smoke.ps1 -PackagePath C:\AgentMuxSmoke\agentmux-windows-package -RunRpcChecks
```

## Verify The Evidence Packet

After filling `manual-checklist.md` and adding screenshots or notes to the evidence folder, run:

```powershell
.\tools\verify-manual-desktop-evidence.ps1 -EvidencePath C:\Path\To\Evidence\20260702-034800 -PackagePath C:\AgentMuxSmoke\agentmux-windows-package
```

For a release ZIP, pass the ZIP path:

```powershell
.\tools\verify-manual-desktop-evidence.ps1 -EvidencePath C:\Path\To\Evidence\20260702-034800 -PackagePath C:\Downloads\agentmux-windows-v0.1.0-framework-dependent.zip
```

The verifier writes `MANUAL-EVIDENCE-VERIFICATION.json` by default. It checks required helper files, strict package checksum coverage, `PACKAGE.json`, `EVIDENCE.json`, the manual-gate/release-readiness boundary, checklist completion fields, and expected screenshot or note filenames. It does not inspect screenshot pixels, perform OCR, redact evidence, or prove physical keyboard input by itself. A human reviewer still needs to inspect the visible Windows desktop evidence before marking the manual gate as passed.

CI and Release workflows run the verifier only in preflight mode against `manual-desktop-smoke.ps1 -SkipLaunch` output:

```powershell
.\tools\verify-manual-desktop-evidence.ps1 -EvidencePath C:\Path\To\HelperOutput -PackagePath C:\AgentMuxSmoke\agentmux-windows-package -PreflightOnly
```

`-PreflightOnly` checks helper output shape and package integrity; it intentionally skips manual screenshots, `Result: PASS`, and physical-keyboard evidence.

## Manual Checks

Record screenshots or short screen clips for each pass. Keep secrets out of the terminal and browser before capturing evidence.

### 1. App Launch

Expected:

- `AgentMux.exe` opens a visible WPF window.
- A workspace sidebar is visible.
- At least one terminal pane is visible.
- There is no immediate crash dialog.

Evidence:

- Screenshot: `01-app-launch.png`.
- Helper file: `environment.json`.

### 2. Terminal Output And Physical Typing

Click the terminal pane and type:

```powershell
echo AGENTMUX_DESKTOP_SMOKE
```

Press Enter with the physical keyboard.

Expected:

- The typed command appears in the terminal.
- The terminal shows `AGENTMUX_DESKTOP_SMOKE`.
- Input appears in the active pane, not a browser address field or inactive pane.

Evidence:

- Screenshot: `02-terminal-typed-output.png`.
- CLI output: `.\cli\agentmux.exe read-screen --lines 50` should include the smoke token.

### 3. Split Pane And Focus

Use the UI split buttons or CLI:

```powershell
.\cli\agentmux.exe split right
.\cli\agentmux.exe split down
```

Then use physical keyboard shortcuts:

- `Ctrl+Tab`
- `Ctrl+Shift+Tab`
- `Ctrl+Alt+Left`
- `Ctrl+Alt+Right`
- `Ctrl+Alt+Up`
- `Ctrl+Alt+Down`

Expected:

- The split layout is visible.
- The active pane marker moves between panes.
- Focus movement follows the visible pane geometry where a neighbor exists.

Evidence:

- Screenshot: `03-split-focus.png`.
- Notes in `manual-checklist.md` for any shortcut that has no neighbor in the current layout.

### 4. Zoom And Close

Use:

- `Ctrl+Shift+Z` to toggle zoom on the active pane.
- `Ctrl+Shift+X` to close a pane when a sibling remains.

Expected:

- Zoom shows one pane without destroying the split layout.
- Toggling zoom again restores the previous split layout.
- Close removes only the active pane and leaves the remaining sibling visible.

Evidence:

- Screenshot: `04-zoom.png`.
- Screenshot: `05-close-pane.png`.

### 5. Browser Pane And Preview Automation

The helper writes a local demo page named `browser-smoke.html` into the evidence folder and prints its file URI in `manual-checklist.md`. Open that page in a browser pane:

```powershell
.\cli\agentmux.exe browser open file:///C:/Path/To/Evidence/browser-smoke.html
```

Then run:

```powershell
.\cli\agentmux.exe browser wait-for-selector h1 --timeout-ms 10000
.\cli\agentmux.exe browser wait-load --state load --timeout-ms 10000
.\cli\agentmux.exe browser eval "document.title"
.\cli\agentmux.exe browser text --selector h1
.\cli\agentmux.exe browser fill "#prompt" "agentmux desktop smoke"
.\cli\agentmux.exe browser click "#submit"
.\cli\agentmux.exe browser type "#prompt" " typed"
.\cli\agentmux.exe browser press Enter --selector "#prompt"
.\cli\agentmux.exe browser screenshot .\manual-browser.png
```

Expected:

- The active pane visibly becomes a browser pane.
- The local `AgentMux Desktop Smoke` page loads without network access.
- The wait command returns success.
- Eval returns the visible page title.
- Text extraction returns the visible heading.
- Fill/click/type/press update the visible page.
- Screenshot file exists and shows the browser content.

Evidence:

- Screenshot: `06-browser-pane.png`.
- File: `manual-browser.png`.
- Helper or terminal output copied into `manual-checklist.md`.

### 6. OSC Notification Preview

In a terminal pane, run this PowerShell command:

```powershell
[Console]::Write("before`e]99;AgentMux Smoke|Desktop notification body`abefore-visible-after")
```

Then run:

```powershell
.\cli\agentmux.exe notifications list --limit 5
.\cli\agentmux.exe notifications jump-latest
.\cli\agentmux.exe notifications clear
```

Expected:

- The raw OSC escape sequence is not displayed as visible terminal text.
- `notifications list` includes `AgentMux Smoke` and `Desktop notification body`.
- `notifications jump-latest` selects the pane that emitted the notification.
- `notifications clear` clears unread state.
- A native Windows toast may appear with the same title/body when local Windows notification settings allow it. This is unredacted local notification content; do not use secrets in this step. If no toast appears, record the Windows notification settings and whether AgentMux was elevated.

Evidence:

- Screenshot: `07-notification-terminal.png`.
- Optional screenshot: `07-native-toast.png` or a note that no Windows toast was visible.
- Redacted CLI output in `manual-checklist.md`.

### 7. CLI/RPC State Checks

Before closing the app, run and capture these CLI/RPC checks:

```powershell
.\cli\agentmux.exe ping
.\cli\agentmux.exe status
.\cli\agentmux.exe workspace list
.\cli\agentmux.exe focus right
.\cli\agentmux.exe pane resize --cols 100 --rows 30
.\cli\agentmux.exe send "Read-Host AGENTMUX_SEND_KEY_SMOKE"
.\cli\agentmux.exe send-key Enter
.\cli\agentmux.exe read-screen --lines 50
```

Expected:

- Each command exits with code 0.
- `status` and `workspace list` return the running app state.
- UI-affecting commands match visible pane behavior.
- `send-key Enter` completes the `Read-Host` prompt in the active terminal.
- `read-screen` includes the latest terminal smoke output.

Evidence:

- CLI output copied to `logs\rpc-ping-status-workspace-focus-resize-sendkey.txt`.

### 8. Session Restore

Close AgentMux. Relaunch `AgentMux.exe`.

Expected:

- Workspaces and split layout restore.
- Browser URLs and pane titles restore where applicable.
- Live terminal processes do not resume; terminal panes start fresh sessions as needed.
- Notification bodies are not restored from the session snapshot.

Evidence:

- Screenshot: `08-session-restore.png`.

## Pass Fail Rubric

Pass only if all required checks are true:

- App launches visibly on Windows.
- Physical keyboard typing reaches a terminal pane.
- Terminal output is visible and current.
- Split/focus/zoom/close behavior works through the desktop shell.
- Browser pane visibly loads and basic preview automation works.
- OSC notifications are captured without raw escape text leaking into visible terminal output.
- CLI/RPC commands work against the running app.
- Evidence folder includes the helper output, screenshots, and filled checklist.

Fail if any of these happen:

- The app does not launch.
- The terminal is blank or physical typing does not reach it.
- A required shortcut never reaches AgentMux while the app has focus.
- Browser pane cannot display a normal page.
- Notification OSC text leaks raw escape payloads into visible terminal output.
- CLI cannot reach the running app.
- Evidence is missing or contains unredacted secrets.

## Evidence Folder

The helper creates files similar to:

```text
agentmux-desktop-smoke-evidence/
  20260702-034800/
    environment.json
    package-metadata.json
    package-checksums.txt
    cli-help.txt
    rpc-ping.txt
    rpc-status.txt
    launch.json
    browser-smoke.html
    manual-checklist.md
    MANUAL-EVIDENCE-VERIFICATION.json
    screenshots/
    logs/
```

Add screenshots and notes into the same folder. Do not upload private terminal history, browser cookies, credentials, tokens, SSH keys, API keys, customer data, private prompts, or browser trace files from private pages.

## Cleanup

- Close AgentMux.
- Delete the extracted package if no longer needed.
- Delete `%LOCALAPPDATA%\AgentMux\Downloads` only if you intentionally want to remove preview browser downloads.
- Delete the evidence folder after sharing or archiving the needed proof.
