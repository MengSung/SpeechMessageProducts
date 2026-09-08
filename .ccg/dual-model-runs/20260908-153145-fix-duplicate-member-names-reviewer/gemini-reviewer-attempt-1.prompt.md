ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: fix-duplicate-member-names

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.8.FixDuplicateName.Worktree

## Request
# 審查任務

請審查目前 working tree 中「修正小組回報頁面重複會友姓名」的所有未提交變更。

## 變更重點

- `ListManager` 使用 instance-owned gate、完整 `(account, credential, loginType, date, listId, weeklyReportId)` scope key、operation-local candidate、完成後發布與 detached read。
- `DownloadIntegrateData` 只在所有子階段完成後設定 `LoadFlag`。
- SmallGroup/NewPerson/Chart API 不再把 Session 可變集合直接交給 DataSourceLoader。
- LINE 登入移除同一 InMemoryContext 的 Task.Run/Task.WhenAll，並使用 server-side ActiveListId，而非 LINE user id 當小組 id。
- 日期切換合併為同一 ListManager gate 內的重建與整合快照發布。
- `AGENTS.md` 與 `.trellis/spec/backend/duplicate-row-publication-contract.md` 新增所有產品線的 duplicate-row、Session leakage、memory/resource leakage 永久規範。
- 新增 5 個併發、同名、exact key、scope、retry、detached mutation regression tests。

## 審查要求

請實際讀取 `git diff` 與受影響的完整程式，分 Critical / Warning / Info 回報：

1. 是否仍可能發生跨使用者/跨產品 Session leakage、credential 或 authorization scope 串用。
2. 是否仍可能發布半完成資料、重複 stable row key、或錯誤刪除合法同名資料。
3. gate/lock、同步 CRM I/O、Task、取消、cache eviction、GC、connection、stream、tracer 等資源是否有洩漏或 deadlock。
4. 日期/小組/登入世代變更是否能正確失效舊快照，失敗後是否可重試。
5. 測試是否真的能抓到錯誤，而不是只驗證 mock 或實作文字。
6. C#／Razor 文件、UTF-8 without BOM、CRLF、範圍與可維護性。

禁止建議以 FullName、電話或單獨 ContactId 去重；合法同名會友必須保留。


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