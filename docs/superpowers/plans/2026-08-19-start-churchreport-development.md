# ChurchReport Development Startup Script Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add one PowerShell script that builds ChurchReport, starts the compiled application, waits for its local endpoint, and opens the site in the default browser.

**Architecture:** The script resolves paths relative to its own location, configures UTF-8 for the current PowerShell process, builds the project synchronously, then starts `dotnet run --no-build` as a child process. A bounded TCP readiness probe gates browser launch, and a `finally` block terminates the complete server process tree when the script exits.

**Tech Stack:** Windows PowerShell / PowerShell 7, .NET SDK, ASP.NET Core, `Start-Process`, `System.Net.Sockets.TcpClient`.

---

### Task 1: Create the development startup script

**Files:**
- Create: `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1`

- [ ] **Step 1: Add script parameters and repository-relative path resolution**

  Define optional `Configuration`, `Url`, `StartupTimeoutSeconds`, and `SkipBrowser` parameters. Resolve the project file from `$PSScriptRoot` so the script works regardless of the caller's current directory, and fail fast if `dotnet` or the project file is unavailable.

- [ ] **Step 2: Configure UTF-8 and build before launching**

  Set console input/output and `$OutputEncoding` to UTF-8 without a BOM, set the child-process environment to `Development`, invoke `dotnet build`, and stop with a nonzero exit code when compilation fails.

- [ ] **Step 3: Start, probe, and clean up the server**

  Start `dotnet run --no-launch-profile --no-build` without hiding its output. Poll the URL's TCP port until it is reachable or the configured timeout expires. Open the URL only after readiness succeeds. Always terminate the started process tree in `finally` so Ctrl+C, startup failure, and normal exit do not leave a server process behind.

### Task 2: Verify the script

**Files:**
- Verify: `SpeechMessageProducts.ChurchReport/Scripts/Start-ChurchReportDevelopment.ps1`

- [ ] **Step 1: Parse-check the PowerShell file**

  Run `powershell.exe -NoProfile -Command "& { [System.Management.Automation.Language.Parser]::ParseFile(...); if ($errors.Count -gt 0) { exit 1 } }"` against the new file and require zero parser errors.

- [ ] **Step 2: Run the build path**

  Execute the script with `-SkipBrowser` and a bounded timeout, confirm the project builds and the application reaches `http://localhost:5000`, then stop it with Ctrl+C and confirm no ChurchReport development process remains.

- [ ] **Step 3: Inspect the final diff**

  Run `git diff --check` and `git status --short`; confirm only the requested startup script and this implementation plan are present.
