ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\architect.md
<TASK>
# CCG architect Task: process-boundary-cross-assembly-isolation-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree

## Request
# 任務

審查一個只限本機測試基礎設施的設計：兩個不同 xUnit test assembly 同時執行時，`SpeechMessage.Dynamics.WorkerTestHost` 被 ChurchReport disabled feature boundary test 誤判為自身建立。單獨 ChurchReport test 通過；受控並行時失敗，且失敗 PID 屬於 Dynamics soak test。

設計是用 shared source-link test collection fixture，在兩程序集之間以 `%TEMP%` 固定無內容檔案、`FileShare.None`、bounded polling 建立 class-lifetime interprocess lease。所有會建立 WorkerTestHost 的 Dynamics test class 與要求零 WorkerTestHost 的 ChurchReport class 都加入 collection。fixture dispose 釋放 FileStream；testhost abort 由 OS 釋放 handle。不得降低 ChurchReport 的 process/listener assertion，亦不得改產品程式、CRM、CE、feature flag 或 deployment。

請分析：設計是否能正確避免跨程序集 false positive、是否有 thread/lifecycle/resource-isolation 風險、最小必要測試集合，以及任何 Critical/Warning/Info finding。只輸出去識別化結論。


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
