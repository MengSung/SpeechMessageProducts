ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\claude\analyzer.md
<TASK>
# CCG analyzer Task: churchreport-trace-remediation-f1-analysis

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.6.DesignNewArchitector.Worktree

## Request
# F1 背景上傳狀態隔離：雙模型分析請求

請分析目前工作樹中的 F1 實作，不要修改檔案。輸出必須分成：

1. 目前資料流與共享可變狀態風險（特別是 SaveIntegrate 的 Task.Run、三組 Members 集合、背景清理與前景列舉）。
2. 依 `.trellis/tasks/08-22-churchreport-trace-findings-remediation/implement.md` F1 要求提出可落地的最小安全設計，包含檔案邊界、鎖範圍、深拷貝欄位、回寫順序、取消／例外／Dispose 與跨使用者隔離。
3. 列出全 repo `m_SmallGroupData.Members`、`m_NewPersonFollowUpData.Members`、`m_AllMemeberData.Members` 的所有使用點；若無法完整列舉，明確說明缺口。
4. 提出測試優先順序與必要的回歸／競態測試，避免 Session leakage、memory/resource leakage。
5. 指出不可修改的範圍外項目與任何 Critical/Warning 風險。

已知背景：F3 與 F4 已各自提交；F2 已提交為 `3bf57fce`。F1 是本任務最後一階段，不能回退或改動前述提交。請遵守 AGENTS.md 的繁體中文文件、UTF-8/CRLF、隔離與資源生命週期要求。


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