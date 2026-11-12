# P3-09 實機驗收設計

## 邊界

協調者只在目前 P3 worktree 與核定測試 VM `D365-9.1` 操作。遠端認證從本機 DPAPI Clixml 在記憶體載入，再以 PowerShell Direct 傳遞；不得將 credential 寫到檔案、命令列、log 或證據。Channel secret/token 從本機 User Secrets 取得，只在必要程序記憶體使用。

VM 同時承載 CRM，故任何主機變更前先確認既存 `appcmd` 備份並記錄 CRM site、app pool、HTTPS binding、憑證指紋、SQL 資料庫清單與 CRM 健康回應。部署採獨立 app pool、目錄、site 及只含測試 hostname 的 HTTPS/SNI binding。不得改既有 CRM site/binding/pool/certificate。使用者已明確核定以 `linewebhook-test.speechmessage.com.tw`＋SNI＋既有萬用憑證新增 binding，並依即時、可逆的驗證程序確認與 CRM 共存：goodshepherdback 維持 HTTP 302 且憑證指紋不變、CRM site 與 CRMAppPool 維持 Started、測試 hostname 改由新站回應。任何一項不符先移除新 binding；若 CRM 仍異常，使用既有備份還原並重新核對。此決策不再擴大成部署前停止條件。

測試機密由使用者核定放在新網站專用 AppPool 環境變數；值只從本機 User Secrets 讀取並以 PowerShell Direct 記憶體參數傳遞。執行前唯讀確認 applicationHost.config ACL 僅允許 Administrators、SYSTEM、WAS 等必要系統主體讀取。正式環境做法只記入「計畫修正提案」，本任務不替正式環境選型。

## 資料與資源生命週期

平台事件去重資料只能放新建、專用測試資料庫，不能使用 CRM 資料庫。建立前唯讀確認 SQL Server/建庫權限，測試資料以明確標記與測試 Channel 隔離；驗收後依計畫清除，查詢表列數及資料庫清單確認殘留為 0。若測試 DB 仍需保留供測試站運作，僅保留無測試列的結構並在回報說明，不將它誤稱為零資料庫。

IIS 設定備份是唯一 IIS 回復點；操作紀錄記下 `appcmd restore backup` 的明確回復步驟。只在已確認 Hosting Bundle 缺少時安裝。安裝造成 IIS restart 時，使用安裝前後 `goodshepherdback` HTTPS 健康回應與 site/pool 狀態記錄影響時間。不得以 `iisreset` 作一般部署步驟。

## 驗證順序

先完成唯讀身份/DNS/TLS/IIS/SQL/runtime 檢查，再確認 CRM baseline 與 IIS backup。之後依序處理 Hosting Bundle、隔離資料庫、獨立 IIS site/app pool、部署及站內設定；每一步先記錄前置、動作、結果與回復方式，再執行下一步。依 04 P3-09 原定步驟驗收中文會友、偽語系、同事件重送與錯誤簽章。LINE 模擬 reply token 的預期錯誤不得誤報為 Webhook 失敗。

清理後再次核對測試資料為零、CRM site/binding/app pool/certificate 與 SQL CRM 資料庫未被修改、秘密未進 repo/log/evidence。失敗步驟保留為失敗或前置不足，不將其折算為離線通過。

Reply 送出紀錄由專用設定開關控制且預設關閉，只在測試站啟用。沿用既有 ILogger，使用固定類別與 EventId，欄位僅限 webhook event ID、訊息類型、遮罩 userId 後的回覆文字、語系及 LINE API 回應狀態碼；寫入前遮罩 LINE userId，並驗證 token、reply token、Channel secret 及未遮罩 userId 均不會進入紀錄。此觀察管道不新增持久化儲存或背景資源。
