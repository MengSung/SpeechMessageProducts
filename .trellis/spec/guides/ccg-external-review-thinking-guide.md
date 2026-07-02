# CCG External Review Thinking Guide

> Use this before running or repairing Gemini/Claude CCG external review. Full runbook: `docs/ccg-gemini-claude-review-troubleshooting.md`.

## Quick Trigger

Read the full runbook when any of these appear:

- `gemini command not found in PATH`
- `claude command not found in PATH`
- `npm.ps1 cannot be loaded`
- Claude says it is not logged in or has no API key
- Gemini hangs, crashes, or reports a Windows libuv assertion
- Gemini hooks report `python not recognized`
- A new worktree causes Gemini trust / approval problems

## Required Health Check

Before spending time debugging reviewer prompts, verify the toolchain:

```powershell
cmd.exe /c "where gemini & where claude & where python & gemini --version & claude --version & python --version"
& "C:\Users\Administrator\.claude\bin\codeagent-wrapper.exe" --version
```

## Stable Reviewer Shape

Use:

```powershell
codeagent-wrapper.exe --lite --backend gemini
codeagent-wrapper.exe --lite --backend claude
```

Do not use Gemini with `--progress` on Windows unless the wrapper/Gemini crash path has been revalidated.

## Mental Model

Treat CCG external review as a multi-layer integration:

1. Windows User PATH
2. npm global shims
3. Codex sandbox / escalated execution
4. `codeagent-wrapper`
5. Gemini / Claude auth and trust state
6. Python hooks under `.gemini/hooks`
7. CCG reviewer prompt templates

Do not assume one passing layer proves the whole chain works.
