ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: p72-weekly-attendance-write-family-final-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# CCG reviewer task: P7.2 weekly attendance write family final review

請僅審查目前工作樹中下列 task-owned 文件與既有本機出席契約的關係；不得修改檔案、執行 CE、啟用 feature gate、切換流量、推送或建立 PR。

## 範圍

- `.trellis/tasks/08-14-p72-weekly-attendance-write-family/`
- `SpeechMessage.Dynamics.Abstractions/Operations/P72AttendanceWeeklyReportDecision.cs`
- `SpeechMessage.Dynamics.Abstractions/Operations/P72AttendanceUpsertLocalDecision.cs`
- `SpeechMessage.Dynamics.Abstractions/Operations/P72AttendanceLocalPlanBuilder.cs`
- 對應的 `SpeechMessage.Dynamics.Tests/P72Attendance*Tests.cs`

## 已驗證事實

- 歷史 P7.2 Slice C CE cycle 是 `write-not-committed` no-go，exact cleanup 已完成；舊 nonce、ledger、fixture、descriptor 均不可重試或復用。
- 本 child 沒有 CE preflight、fixture、Create、Update、Assign、Delete、Associate、Disassociate、feature gate、流量或 cleanup 操作。
- 此 child 的 32 個 targeted local tests 通過；solution Release build 為 0 warnings、0 errors。
- QR attendance 的 production caller 將 browser/route input 放入 process-wide `InMemoryContext` 後才進 CRM I/O，缺少可證明的 request-local、server-derived authorization boundary。
- 因此本 child 結論必須是 local design no-go，而非 CE 環境、Full-Text Search、P7 全域、P7.5 或 P8 的阻塞。

## 審查問題

1. task 文件是否正確維持證據階層，沒有把 local reducer/plan 當作 CE、consumer、traffic、P7.5 或 P8 evidence？
2. local no-go 是否由可驗證的跨使用者／跨 profile isolation 與 mutation graph 根因支持？
3. zero-active、exactly-one-active、duplicate/unavailable weekly-report 契約是否被準確表達，沒有誤要求全組織唯一週報？
4. 是否有 Critical、Warning 或 Info；每項都需對應到具體檔案與可驗證根據。

輸出繁體中文，分 Critical / Warning / Info。若沒有問題，明確寫「無 Critical／Warning」。


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