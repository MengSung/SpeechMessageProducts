# P3-06 事件分派與處理器移植審查摘要

## 審查狀態

- 審查日期：2026-11-10 Asia/Taipei (UTC+08:00)
- 審查入口：`docs/scripts/Start-CcgDualModelRun.ps1 -Role reviewer`
- 最新 run：`20261110-004955-p3-06-event-dispatch-rerun-reviewer`
- `dualModelComplete=false`；Claude 有可用輸出，Gemini 於兩次自癒嘗試分別回報 HTTP 403／503，沒有可用輸出；`quotaBlocked=false`，因此不稱雙模型完成，也不稱成功的單模型 fallback。
- 本摘要只保留可驗證結論，不把外審原文納入版控。

## 分級結果

- Critical：0
- High：0
- Warning：3
- Info：2

### Warning

1. `WebhookHttpAdapter` 本身沒有獨立固定 timeout，現行安全上限由 P3-05 每事件 30 秒 cancellation 提供；P3-09 實機及後續修改須驗證 queue deadline 確實包住所有 HTTP I/O。
2. `WebhookPostbackHandler` 以已驗證 actor 與正式 signer ID 拒絕未授權簽核，但沒有從 appointment 另取教會／組織欄位；若資料模型允許 signer 跨教會指向 appointment，需在 P3-09／後續契約補權威範圍檢查。
3. 音訊 annotation 以固定正式 ID Upsert 實現重送冪等；離線替身已驗證 parent／ID／故障邊界，D365 對既有 annotation 的 Upsert 行為仍須 P3-09 實機確認。

### Info

1. Follow 對既有個人 Contact 的狀態沿指定舊行為寫入 `new_line_status=100000003`；沒有在本包改變業務語義，實機僅確認結果。
2. `ReplyPending`／`Deferred` 只記錄具名結果並由 queue 完成事件；P3-08 必須接通 Reply／letter，不能把本包結果解讀成已回覆。

## 已驗證

- 真 LINE SDK parser 解析後分派至具名 handler；群組／聊天室缺 `userId` 在 scope resolver 前 fail-closed。
- 課程簽到改由 actor 的 `new_stor_lessons` 關聯及正式課程 lookup 驗證，不做未限教會的全域名稱查詢。
- 音訊 Bearer 逐 request 設定、固定官方 URL、MIME／大小上限、串流取消與 annotation parent 驗證均有測試。
- GoodShepherd Core Release net48／net10 各 70/70；LineWebhook 測試 103/103；Host Architecture 測試 56/56；相關進度頁測試 28/28。
- 一次完整 AspNetCore 測試曾出現既有 queue handoff 的 timer count 時序抖動；該案例獨立重跑 20/20 通過，完整測試重跑 69/69 通過，未將無關 queue 修補混入 P3-06。

## 殘餘邊界

P3-06 沒有實機要求；本包未連 D365、LINE、DMZ 或 SQL。P3-09 才能驗證實機部署、Webhook、重送、真實 annotation／CRM 行為及殘留 0。
