# 審查報告：global-isolation-memory-leak-guardrails（最終審查）

## 審查範圍確認
已完整讀取以下五個檔案並逐項核對第 1–8 項要求：
- `C:\Users\Administrator\.codex\AGENTS.md`（329 行，全文讀取）
- `.trellis\tasks\07-22-global-isolation-memory-leak-guardrails\prd.md`
- `.trellis\tasks\07-22-global-isolation-memory-leak-guardrails\task.json`
- `.ccg\tasks\global-isolation-memory-leak-guardrails\requirements.md`
- `.ccg\tasks\global-isolation-memory-leak-guardrails\task.json`

並以 `git diff` 核對規劃文件的實際變更範圍。

---

## Critical 🔴
無。

## Warning 🟡

**[requirements.md / prd.md 與 AGENTS.md 生命週期清單不一致]**
- **檔案**：`.ccg\tasks\global-isolation-memory-leak-guardrails\requirements.md:21`、`.trellis\tasks\07-22-global-isolation-memory-leak-guardrails\prd.md:13`
- **問題**：兩份規劃文件僅列出 `subscriptions, timers, background tasks, caches, collections, streams, handles, and disposable resources`（8 項），但實際落地於 `AGENTS.md:325` 的政策文字為 `subscription, timer, background task, cache, collection, stream, handle, cancellation registration, connection, and disposable resource`（10 項，多出 **cancellation registrations** 與 **connections**）。本次審查任務本身第 4 條要求的清單也是完整的 10 項，代表交付內容（AGENTS.md）已正確涵蓋，但作為「可回溯 spec 來源」的 `prd.md` / `requirements.md` 落後於實作。
- **具體失效情境**：若未來有人依據 `prd.md`／`requirements.md` 的驗收標準重建或稽核 AGENTS.md 政策內容（這正是這兩份文件存在的目的），會誤以為「cancellation registrations」與「connections」不在強制生命週期擁有權範圍內，可能在後續編輯時被無意間移除或弱化——而這兩類資源（非同步取消 token、DB/網路連線）恰好是最容易造成洩漏與跨 session 污染的高風險項目。
- **建議修復**：將 `prd.md` 的 Requirements 條目與 `requirements.md` 的 Verification intent 條目補上 `cancellation registrations` 與 `connections`，使其與已交付的 AGENTS.md 文字及本次審查標準完全一致。

## Info 🟢

1. **CCG 管理區塊邊界正確**（第 1 項）：`AGENTS.md:320` 為 `<!-- CCG:END -->`，新政策從 `AGENTS.md:322`「## Global Session Isolation and Resource Lifecycle Guardrails」開始，未出現在 `CCG:START`/`CCG:END` 之間，也未新增或修改任何 `CCG:START`/`CCG:END` 標記，第 1–320 行管理區塊內容逐字未變。**符合。**

2. **跨 session/使用者/租戶洩漏零容忍**（第 2 項）：`AGENTS.md:324` 明確聲明「Treat cross-session, cross-user, and cross-tenant data or state leakage as a zero-tolerance security defect and release blocker.」用詞與 `requirements.md:10`、`prd.md:10` 一致。**符合。**

3. **記憶體/資源洩漏零容忍**（第 3 項）：`AGENTS.md:325` 首句「Treat memory and resource leaks as zero-tolerance correctness and reliability defects and release blockers.」範圍較 requirements.md 原文的「memory leaks」更廣（含 resource leaks），屬於加強而非弱化，不構成缺陷。**符合。**

4. **生命週期擁有權清單**（第 4 項）：如 Warning 所述，`AGENTS.md:325` 本身完整涵蓋全部 10 項並要求「explicit bounded owner and deterministic cleanup path」，AGENTS.md 交付內容本身**符合**本次審查第 4 項；問題僅在規劃文件未同步（見 Warning）。

5. **風險導向驗證要求**（第 5 項）：`AGENTS.md:327`「add targeted isolation tests and lifecycle assertions, then run proportionate stress, soak, or profiling checks that prove resources return to a declared baseline after drain or disposal」完整涵蓋 targeted tests、soak/stress/profiling 及可證明的基線回收，且用詞比 requirements.md 更具可驗證性。**符合。**

6. **效能定位為「最大安全永續效能」**（第 6 項）：`AGENTS.md:328`「Optimize for maximum safe sustained performance. Speed or memory-efficiency changes must never weaken isolation, correctness, deterministic cleanup, verification, or maintainability.」與本次審查第 6 項逐字對應，也與已更新的 `requirements.md:10`、`prd.md:13-14` 一致（兩份規劃文件皆已由舊版「…cleanup, or maintainability」正確更新為「…cleanup, verification, or maintainability」，`git diff` 確認）。**符合。**

7. **用詞精簡、可執行、技術正確、PRD 與 requirements 內部一致**（第 7 項）：`prd.md` 與 `requirements.md` 彼此之間用詞一致（`git diff` 顯示兩者同步更新），政策本身 5 條敘述精簡且可稽核。但兩者與已交付的 AGENTS.md／本審查標準之間存在第 4 項清單落差，詳見上方 Warning。**部分符合（有落差待修正）。**

8. **無不相關的全域設定被更動**（第 8 項）：`AGENTS.md` 全文（329 行）僅第 1–320 行為既有 CCG 管理區塊（內容未變），第 322–328 行為新增政策，未見其他變動；`.ccg`／`.trellis` 兩組 task.json 與規劃文件的 `git diff` 僅涉及本任務的措辭更新（新增 `verification`、`maintainability`）與任務狀態欄位（`currentPhase`/`status`/`branch`），未觸及無關的全域或倉庫設定。註：`C:\Users\Administrator\.codex` 非 git 倉庫，無法用版本控制直接比對歷史版本，此結論基於全文檢視。**符合（無版本歷史可交叉比對，已標註限制）。**

9. **驗收標準勾選狀態**：`prd.md` 中 Acceptance Criteria 全部仍為 `[ ]` 未勾選，`task.json`（.trellis）狀態為 `in_progress`（非 `completed`），與目前處於 review 階段、尚未正式結案的狀態一致，不構成缺陷，僅供追蹤參考。

---

## 總結與結論

政策本體（`AGENTS.md:322-328`）**完整且正確地滿足本次審查第 1、2、3、5、6、8 項要求**，且部分描述（記憶體/資源洩漏範圍、驗證證明基線）優於原始需求文件的最低要求。

但存在 **1 項 Warning**：規劃文件 `prd.md`／`requirements.md` 的生命週期資源清單未同步收錄已交付政策中的 `cancellation registrations` 與 `connections`，形成「規格文件落後於實作」的可維護性風險，若日後以規劃文件為準重建或稽核政策，可能導致這兩類高風險資源的擁有權/清理要求被遺漏。

**最終判定：非 PASS（因存在 1 項 Warning）。** 建議修補 `prd.md` 與 `requirements.md` 的清單後即可視為完全通過，無需再修改 `AGENTS.md`。

---
SESSION_ID: cca282ca-44ad-47dd-8b24-fb47da5355ec
