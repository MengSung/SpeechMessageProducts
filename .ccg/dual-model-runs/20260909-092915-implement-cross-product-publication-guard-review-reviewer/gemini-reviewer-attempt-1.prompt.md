ROLE_FILE: C:\Users\Administrator\.claude\.ccg\prompts\gemini\reviewer.md
<TASK>
# CCG reviewer Task: implement-cross-product-publication-guard-review

## Repository
D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.8.FixDuplicateName.Worktree

## Request
# 審查任務：跨產品資料發布防重複與網路時序防護

請審查目前工作樹相對 HEAD 的全部程式變更。這是 ASP.NET Core / .NET 10 ChurchReport 專案，並包含 DevExtreme Razor/JavaScript 前端。

## 必須判定的核心契約

1. 資料列身份只能使用權威資料庫唯一 ID；本功能使用 `PresentRecordId`。不得依姓名、電話、顯示內容、索引、時間或臨時 Guid 去重。
2. 同名但不同 ID 的合法資料必須全部保留；同一 consumer collection 內相同非空 ID 或空 ID 必須 fail closed，不可靜默刪除、合併或覆寫。
3. Session、快取與 Controller 交付給 Razor/DataSourceLoader 的集合不得暴露仍可被其他請求修改的活物件圖；快取命中也必須 detached 並對實際交付集合重新驗證。
4. 任何 Session、使用者、租戶、權杖、credential、CRM client 或 mutable request state 不得跨請求、跨使用者或跨租戶洩漏。
5. 不得有 Memory/Resource Leakage：特別檢查 background task、timer、callback、Promise、transport、WeakMap、DOM/component closure、CancellationToken registration、stream/connection/disposable 的 owner、界限與 deterministic cleanup。
6. 慢網路、代理重送、回應亂序、重複 refresh、重複 mount 時，只有最新 generation 可發布；abort 不可作為唯一正確性依據；pending work 必須有界。
7. 競態修改必須在同一 instance synchronization root 下完成，避免 check-then-add race 與 partial publication。
8. 所有新增或修改的 `.cs` / `.cshtml` 區域必須有完整、深入、可維護的繁體中文註解，尤其交代信任邊界、競態、Session isolation、資源 owner/lifetime/cleanup、失敗策略與效能取捨。
9. 請檢查新測試是否真正覆蓋：同名不同 ID、相同 ID fail closed、cache hit、並行新增、跨 Session owner、舊回應、重複 refresh、dispose/drain、產品清冊契約。
10. 請注意完整測試套件已知有既有 Payment naming/source-inspection 失敗；不要把與本次 diff 無關的既有失敗誤判成本次回歸，但若 diff 會影響它們仍須指出。

## 已執行的本機證據

- 相關 .NET 測試：22/22 通過。
- JavaScript coordinator 測試：5/5 通過。
- ChurchReport Release build：0 warnings, 0 errors。

## 輸出格式

請依 Critical / Warning / Info 分級；每項列出精確檔案、位置、可重現情境、違反的契約與具體修正方式。若沒有 Critical 或 Warning，請明確寫出，並列出仍無法由靜態審查證明的殘餘風險。不要直接修改檔案。


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