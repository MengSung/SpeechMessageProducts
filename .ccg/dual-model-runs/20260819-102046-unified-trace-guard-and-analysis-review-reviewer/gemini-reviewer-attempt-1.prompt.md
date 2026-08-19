ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: unified-trace-guard-and-analysis-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# Advisory review request: unified Trace guard and analyzer

Review the current worktree diff for the unified ChurchReport Trace task.

Scope:

- `DiagnosticTraceOptions` is the single product-level switch and directory.
- Debug may enable it; Release must fail closed even when configuration or environment variables say true.
- `Trace.log`, `dataverse-trace.jsonl`, and `CHURCH_REPORT_TRACE.TXT` must have deterministic ownership and cleanup.
- `FileToolUtilityTracer` and legacy `TraceLogger` must not contaminate the process-global `Trace.Listeners` collection.
- `Analyze-ChurchReportTraces.ps1` must stream large UTF-8 JSONL/Trace.log and Big5 legacy input, use bounded aggregates, avoid raw sensitive data in its report, and return meaningful exit codes.

Review read-only. Do not edit files or call external services. Read the current diff and relevant tests. Focus on concrete Critical/Warning/Info findings, especially:

1. Release compile/runtime fail-closed proof.
2. Stream/writer/listener/task/CTS ownership and disposal races.
3. Cross-request/user/tenant isolation and trace pseudonym handling.
4. PowerShell 5.1/7 compatibility, encoding, append-sharing, parser correctness, bounded memory, and report redaction.
5. Whether tests and smoke commands actually prove the required behavior.

The owner explicitly treats Gemini/Claude output as advisory. A missing quota/backend must not block local verification. Never reproduce secrets or raw trace lines.

Output Traditional Chinese with `Critical`, `Warning`, and `Info` sections. For each finding include file/symbol and a concrete verification or remediation suggestion.


## Required Recovery Behavior
- Run through the self-healing CCG entrypoint, not direct Gemini/Claude commands.
- If Gemini, Claude, or codeagent-wrapper fails before usable output, repair local toolchain issues and retry.
- Preserve prompts, stdout, stderr, health reports, and summary files under .ccg/dual-model-runs.
- If provider quota/session blocks one backend, classify it as degraded fallback only when -AllowSingleModelWhenQuotaBlocked is enabled and the other backend produced usable output.
</TASK>
OUTPUT:
- Use Traditional Chinese when possible.
- Return concrete findings and decisions.
- For reviewer role, classify findings as Critical / Warning / Info.
- If the task cannot be completed, explain the exact blocker.