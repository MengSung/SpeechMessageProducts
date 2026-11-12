# P3-09 Webhook 實機驗收

## 需求

依實作計畫 3.5 的 04「P3-09 實機驗收：Webhook」第 1～7 步及 00 §11，完成測試 LINE Webhook 在核定測試主機上的實機驗收。只使用測試 Channel 與測試組織；不得修改 Dynamics CRM 既有網站、繫結、應用程式集區、憑證或 CRM 資料庫。所有秘密只從本機 DPAPI/User Secrets 載入記憶體，不輸出、不記錄、不複製。

## 驗收條件

- 測試主機身分、IIS/HTTP.sys、憑證、DNS/TLS、SQL 與 Hosting Bundle 前置均已唯讀核實並留存不含秘密的證據。Hosting Bundle 10.0.14、ANCM v2 與兩份暫存安裝檔清除已於 2026-11-10 11:39–11:45 核實；摘要見 `evidence/hosting-bundle-install.md`。
- IIS 先建立 appcmd 備份，僅建立獨立 Webhook 應用程式集區與網站；CRM 原設定與服務在操作前後可比較且不變。
- 附錄 A.8 的測試資料只放入獨立平台測試資料庫；CRM 資料庫不變。
- 依 04 P3-09 第 1～7 步驗收，逐項記錄成功、失敗或前置不足；不得以離線測試替代實機結果。
- P3-06 三項 Warning 逐項核實；可觀察範圍以真實環境能提供的決定性證據為準，無法確認者明列未確認，不得虛稱通過。
- 測試 Channel Webhook URL 指向核定 URL；測試資料、暫存 DB/檔案及本次擁有的資源清理後為 0。
- 測試秘密以新網站專用 AppPool 環境變數提供；值只從本機 User Secrets 讀取並經 PowerShell Direct 記憶體參數傳遞，不落地。
- 僅新增 `linewebhook-test.speechmessage.com.tw` 主機名＋SNI＋既有萬用憑證。加綁後立即驗證 goodshepherdback HTTP 302 且憑證不變、CRM site 與 CRMAppPool 為 Started、測試 hostname 由新站回應；任一不符即移除新 binding，CRM 仍異常才還原既有 IIS 備份。
- Reply 送出紀錄預設關閉、僅測試站開啟；使用既有 ILogger 及固定類別/EventId，記錄事件 ID、訊息類型、遮罩 userId 後的文字、語系與 LINE API 回應狀態碼，不得記錄任何 token 或 secret。
- 提交驗收步驟、一頁操作紀錄、一頁 P3-09 回報及操作手冊；階段審查 Critical/High 為 0，或明確記錄外部審查不可用與本機自審。
- 更新實作進度頁並推送目前 P3 分支，以 `git ls-remote` 核對 SHA；不推 main、不開 PR、不開始階段 4。

## 固定環境與核定

- 主機：Hyper-V VM `D365-9.1`，回報電腦名稱 `D365APP01`，內部位址 `192.168.50.20`。
- 測試網址：`https://linewebhook-test.speechmessage.com.tw`；Webhook 路徑為 `/api/line/webhook/1646814154`。
- SQL：測試主機預設執行個體 `MSSQLSERVER`；僅建立獨立測試資料庫，不碰任何 CRM 資料庫。
- 網際網路 DNS 已查得 `220.134.52.83`，公網 HTTPS/TLS 已回應且 TLS 驗證成功。
- 本機目前 PowerShell 主體為系統管理員；VM 認證僅由核定的 DPAPI Clixml 匯入並供 PowerShell Direct 使用。

## 已知前置狀態（2026-11-10）

- IIS 唯讀清單只有 `Default Web Site` 與 `Microsoft Dynamics CRM`；CRM HTTPS binding 為 `*:443:`，沒有 hostname/SNI。萬用憑證 `CN=*.speechmessage.com.tw` 到期日為 2027-05-06。
- SQL 唯讀查詢確認執行個體 `MSSQLSERVER`、`IS_SRVROLEMEMBER('sysadmin')=1`、可建立資料庫；未建立或修改資料庫。
- 2026-11-10 使用者完成 .NET 10.0.14 Hosting Bundle 安裝與暫存檔刪除；Codex 唯讀核實 ANCM v2、.NET／ASP.NET Core 10.0.14、CRM site／CRMAppPool Started、goodshepherdback HTTP 302、兩份安裝檔不存在。證據摘要見 `evidence/hosting-bundle-install.md`。
- 外部兩個主機名皆解析到 `220.134.52.83`，TLS 驗證成功；Hosting Bundle 安裝後 goodshepherdback 仍回既有 CRM 前端的 HTTP 302；測試 hostname 的部署路由仍待驗收。
- `goodshepherdback` 的安裝前後健康檢查與 IIS backup 已有紀錄；建立新站前仍需取得當次 CRM/IIS 基線。
- Gemini 自癒分析後端回應 403/503 且沒有可用輸出；Claude 有分析輸出；`dualModelComplete=false`、`quotaBlocked=false`。不得記為雙模型完成。

## 範圍界線

本任務只完成 P3-09 與階段 3 收尾。Hosting Bundle 已完成；SNI 依使用者核定的 hostname 綁定與即時驗證／回復程序執行，不重開此決策。平台 DB 必須與 CRM DB 隔離；即時檢查不符時按核定程序回復並如實記錄，不以猜測或離線測試替代實機驗收。
