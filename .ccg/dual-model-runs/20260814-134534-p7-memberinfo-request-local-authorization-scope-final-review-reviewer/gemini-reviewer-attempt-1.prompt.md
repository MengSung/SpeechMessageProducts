ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p7-memberinfo-request-local-authorization-scope-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# P7 MemberInfo target authorization scope final review

請審查目前工作樹中本 child 的所有變更，尤其是：

- `SpeechMessageProducts.ChurchReport/Security/MemberInfoTargetAuthorizationScope.cs`
- `SpeechMessageProducts.ChurchReport/Properties/AssemblyInfo.cs`
- `ChurchReport.MemberInfo.Tests/Security/MemberInfoTargetAuthorizationScopeTests.cs`

驗證 server-derived target evidence 是否真的被限制在 ChurchReport assembly 內，
public API 是否無法偽造 evidence，subject A/B isolation、bounded immutable IDs、
fail-closed 行為、無 Session／CRM／cache／I/O／retry／resource leakage，以及測試是否
覆蓋關鍵契約。請只回報 Critical／Warning／Info，勿修改檔案；不要要求 CE、feature gate、
traffic、P7.5 或 P8 操作。若沒有可用輸出，明確標示 review incomplete。


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