# 階段3前置缺項清單（附錄K）

2026-11-09 13:20 Asia/Taipei (UTC+08:00)；開工後30分鐘內唯讀複核。這是前置證據，不是P3-09驗收。使用者DMZ欄位仍是`<FQDN或IP>`／`<https://...>`模板，沒有實際端點。

| 項目 | 哪包需要 | 實際核對及狀態 | 誰做什麼 |
|---|---|---|---|
| TestChannelSecret／TestChannelToken／TokenKey（完整鍵名皆Deployment:Organizations:goodshepherdback:開頭） | 03／09 | 三鍵exists=true、format=true；值未輸出。沿用secret，不重新發行 | 已備，不需使用者操作 |
| 測試Messaging Channel ID | 09 | 非秘密1646814154；沿用同日P2-12 Verified=true紀錄，未使用token打API | 實機前核對 |
| DMZ具名主機／公開HTTPS／憑證到期 | 09 | **缺**實際端點；13:16 Hyper-V唯讀CIM成功只回主機系統，不能據此判定VM不存在或猜DMZ | 提供非秘密FQDN/IP及URL；若須對外DNS／防火牆／購證依§9-1由本人處理 |
| DMZ遠端連入／部署IIS／重啟權限 | 09 | **未驗證**：無具名端點，未嘗試遠端，故無本輪遠端錯誤碼 | 端點確認後Codex以既有身分作一次有界唯讀查詢 |
| 測試主機SQL／建庫權限 | 09 | **未驗證**：無可靠端點，不以LocalDB取代實機 | 端點確認後Codex唯讀查權限 |
| 本機MSSQLLocalDB | 04 | **通過唯讀前置**：info exit0、SQL查詢exit0、17.0.4025.3、IS_SRVROLEMEMBER(sysadmin)=1；未建庫，查後stop還原原Stopped基線 | 04直接用本機做真SQL整合測試與清理，不等遠端 |

**尚缺三類**：DMZ／HTTPS／憑證、遠端權限、測試主機SQL。請僅回非秘密端點，可貼單行：`Write-Output 'DMZ=<實際FQDN或IP>; HTTPS=<實際公開URL或未設定>; SQL=<已知端點或未知>'`。不貼帳密、token、連線字串。無需等待回覆，離線包照常進行；P3-09前全部到位才做。

**耐久證據**：本任務ignored `evidence/phase3-preflight-local-readonly.log`、`phase3-secret-key-booleans.json`；P3-00原始證據已搬至`evidence/p300-evidence/`及`evidence/p300-preflight-evidence/`，原manifest位元組SHA不變；遷移manifest SHA256 `883adcc00afe28ece3f072af9a32f4b9618c725a0357f5289ef300d32147e88e`。TEMP刪除被執行政策拒絕，沒有繞過；耐久副本已驗證可用。

## 2026-11-10 P3-09 前置複核

2026-11-10 11:26 Asia/Taipei (UTC+08:00)：使用者提供具名 VM、公開 HTTPS 與操作授權後，已以唯讀方式重查；本節取代上方「缺實際端點」的歷史狀態，不刪除原始紀錄。

| 項目 | 本次實際核對 | 狀態／界線 |
|---|---|---|
| 本機管理員及 PowerShell Direct | 本機身分 `LENOVO-LEGION\Administrator` 且 IsAdministrator=True；由核定 DPAPI Clixml 連 `D365-9.1`，遠端電腦 `D365APP01`、IPv4 `192.168.50.20` | 通過；未連 D365DC01；credential 僅在程序變數內並於命令結束移除 |
| IIS 現況 | 只有 `Default Web Site` 與 `Microsoft Dynamics CRM`；CRM site Started，CRMAppPool Started；CRM HTTPS binding 為 `*:443:`，無 hostname/SNI | 唯讀完成；尚未備份或變更。新增 SNI 前須確認具名 binding 不會改變既有 CRM 路由 |
| 憑證 | CRM binding 使用 `CN=*.speechmessage.com.tw`，thumbprint `6E9E9026A9085E0949F5E460C7055ED658517531`，到期 2027-05-06 | 到期日與萬用名稱覆蓋測試子網域；未更換或修改憑證 |
| DNS／外部 HTTPS | `linewebhook-test.speechmessage.com.tw` 解析 `220.134.52.83`（TTL 86400）；本機 curl 對 goodshepherdback 與測試 hostname 均成功完成 TLS 驗證、回 HTTP 302，remote IP 均為 `220.134.52.83` | DNS/TLS 通過；測試 hostname 尚未路由到 Webhook，302 表示仍落在既有網站，不能算部署驗收 |
| SQL 執行個體及權限 | 遠端唯讀 `sqlcmd -S localhost -E` 成功；執行個體 `MSSQLSERVER`，本機登入 `IS_SRVROLEMEMBER('sysadmin')=1`、`HAS_PERMS_BY_NAME(NULL,NULL,'CREATE ANY DATABASE')=1` | 權限足以建立獨立測試 DB；尚未建立 DB。查詢到 CRM DB 清單但未對其寫入 |
| .NET Hosting Bundle | 只查得 ASP.NET Core runtime 8.0.26；未查得 ASP.NET Core 10 runtime、Hosting Bundle 登錄或 ANCM v2 模組 | 缺少 .NET 10 Hosting Bundle；尚未下載、安裝或重啟。需先保存 IIS/CRM 基線並建立 appcmd backup |
| User Secrets | 在 `SpeechMessage.Dynamics.Deployment.P0` User Secrets 內以程序記憶體檢查：TestChannelSecret 存在且32碼十六進位、TestChannelToken、64-byte Base64 TokenKey、OrganizationUrl、Username、Password、TestLineUserId 均存在；僅回報布林值 | 通過存在性/格式檢查；未輸出或傳遞值、未呼叫 LINE API |
| 自癒分析 | 2026-11-10 11:04 run：Claude 有可用分析，Gemini 因 HTTP 403/503 無輸出；`dualModelComplete=false`、`quotaBlocked=false` | 不視為雙模型完成；相關原始診斷在本機 `.ccg/dual-model-runs/`，不納入提交 |
| IIS 回復點 | 2026-11-10 11:30 前建立 appcmd 備份 `P3-09-pre-202611101130`；備份目錄內含 applicationHost.config 等 IIS 設定 | 通過；操作前 `applicationHost.config` SHA-256 `5C2B6EA883F04D37A8B5059C56A03DF99CAA03E4EA5223F2E48392D42218FE83`；CRM site/pool Started、binding `*:443:`、憑證 thumbprint 與站台數量已記錄 |
| Hosting Bundle | 自 Microsoft 官方頁取得 .NET 10.0.14 Hosting Bundle；下載檔 SHA-512 與官方值相符、Authenticode `Valid`、簽署者為 Microsoft；複製到 VM 暫存後，兩次安裝啟動命令均在執行前被本機工具政策拒絕 | 未安裝、未重啟 IIS。本機 TEMP 與 VM 的安裝檔清理均被工具政策拒絕，目前兩份待移除；未繞過政策。階段 3 不得標完成 |

2026-11-10 11:26 Asia/Taipei (UTC+08:00) 決策：前置網路、遠端及 SQL 權限已到位，可進入有回復點的實機準備；已建立 `appcmd add backup` 並記錄 CRM 原始狀態。因目前 CRM 使用無 hostname 的 `*:443:`，不移除或改寫該 binding，也不更換憑證。官方 Hosting Bundle 缺少且安裝檔完整性驗證通過，但同一安裝啟動操作連續兩次遭本機工具政策拒絕，依 00 §9-7 停止，不改用替代執行器。測試 DB 尚未建立；IIS、SQL、LINE 與 CRM 設定均未寫入。本機 TEMP 與 VM 的暫存安裝檔清理均遭工具政策拒絕，兩份暫存檔待移除。詳細結果與人工續接步驟見 `.trellis/tasks/11-10-p3-09-webhook-live-acceptance/P3-09-執行受阻回報.md`。

## 2026-11-10 Hosting Bundle 後續核對

2026-11-10 11:39–11:43 Asia/Taipei (UTC+08:00)：使用者以系統管理員身分親自安裝 .NET 10 Hosting Bundle 10.0.14 並刪除本機與 VM 暫存安裝檔。使用者提供的不含密碼紀錄 `C:\Users\Administrator\.speechmessage\P3-09-hosting-bundle-install.log` 記載安裝 ExitCode 0、Microsoft 簽章有效、安裝前後 CRM HTTPS 為 HTTP 302、CRM 與 Default Web Site Started、CRMAppPool Started、ANCM v2 True、.NET 與 ASP.NET Core runtime 10.0.14，以及兩份暫存檔移除成功。

2026-11-10 11:43 Asia/Taipei (UTC+08:00)：Codex 以核定 DPAPI Clixml 透過 PowerShell Direct 獨立唯讀複核。`aspnetcorev2.dll` 存在；`dotnet --list-runtimes` 包含 `Microsoft.AspNetCore.App 10.0.14` 與 `Microsoft.NETCore.App 10.0.14`；`Microsoft Dynamics CRM` 與 `CRMAppPool` 均為 Started；VM `C:\Windows\Temp\dotnet-hosting-10.0.14-win.exe` 及本機 `%TEMP%\dotnet-hosting-10.0.14-win.exe` 均不存在。再次對 goodshepherdback HTTPS 發出請求得到 HTTP 302，TLS 驗證成功，符合已知登入導向；不推斷或宣稱安裝中斷時長。IIS／SQL／CRM 設定尚未由本次複核修改。逐筆操作與限制見 `.trellis/tasks/11-10-p3-09-webhook-live-acceptance/evidence/hosting-bundle-install.md`。

2026-11-10 11:45 Asia/Taipei (UTC+08:00) 決策：Hosting Bundle 前置與暫存檔清理已確認，可續作 P3-09；仍須在新增任何 IIS 元件前保留並比較既有 CRM 狀態，SQL 只可新增隔離測試資料庫。
