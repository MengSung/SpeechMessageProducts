# P6.2 Lenovo Operator Handoff（保留為未來支線）

> **狀態：retired from the P6/P7 critical path（2026-08-07）。**
> 本文件不再要求操作者為 P6 結案或 P7 啟動執行 PowerShell，也不授權重新啟動
> Official Worker。它只保存未來另立 Official Worker deployment task 時可重用的
> operator 輸入契約與歷史結果。

## 已完成的歷史輸入

- Lenovo Legion 的執行身分、兩個 profile input 與同身分 Credential Manager reference
  已通過 deployment readiness=`go`。
- CE 8.2 與 CE 9.1 的互動式 Organization 首頁都能載入；這只證明瀏覽器授權，
  不等於 SDK username/password token exchange 成功。
- 最新 bounded startup 結果為兩個 Worker 都在 READY 前以 exit code 20 結束，
  `readyFrameObserved=false`、`operationExecuted=false`、`featureFlagChanged=false`。
- 沒有 CE operation、ChurchReport 流量切換、feature flag 變更或殘留 process／pipe／listener。

這些結果只能將 Official Worker 標為 `evidence-pending`；不得宣稱 CE 8.2／9.1
Official Worker 相容成功，也不得外推為 Data8、Embedded 或 Dedicated Gateway 失敗。

## 未來 task 才可使用的輸入契約

若未來 deployment owner 明確選用 Official Worker，新的 Trellis task 必須重新核准：

1. CE 8.2 與 CE 9.1 各自的 canonical HTTPS Organization root、organization name、
   Organization ID 與 IFD home realm。
2. 同一 Windows execution identity 可解析的 Credential Manager target。密碼只能留在
   Windows Credential Manager／核准 secret provider；不得進入命令列、profile JSON、log、
   Trellis artifact 或聊天。
3. Immutable `ProfileAlias`／`GenerationId`、manifest package lock 與 Worker executable
   hash。Worker 絕對路徑由 manifest 推導，不由操作者手寫。
4. 明確 allowlist：先做 `runtime.health.whoami` 與
   `runtime.pool.validate.connection`；不得執行 generic CRUD、任意 FetchXML、write、
   Action 或 Function。業務語意與 test-owned fixture 屬 P7.2，不屬 P6。

## 未來 task 的建議執行順序

1. 讀取 manifest，執行 `Test-DynamicsOfficialWorkerDeploymentReadiness.ps1 -Json`。
2. 由部署工具產生與 executable 相鄰的 `worker-profile.xml`／Gateway overlay；不覆寫
   未確認的既有 generation。
3. 先以離線 `ValidateOnly` 驗證 manifest、package lock、generation 與 hash。
4. 在明確授權的 maintenance window 啟動 bounded READY bridge；只有兩個 profile 都
   發布 `worker-reported-ready` 才可進入 allowlisted read-only matrix。
5. 任一 profile 失敗即停止、drain、清理 process／pipe／stream／timer／registration，
   輸出 sanitized JSON，且不改用另一個 Connector、profile 或 CE version。

## 不可執行的舊命令

本文件舊版的 profile 產生、Credential Manager 設定、瀏覽器確認與 startup bridge 命令
已完成且不應在目前 P6 重做。若需要恢復，請先建立新的 Official Worker task，再由該
task 產生新的 handoff；不要直接複製歷史命令到目前 P7 Data8 主線。

## 目前路線

P6 以離線 Official Worker Router／Pool／Lease 擴充點結案；P7 在 Lenovo Legion 以設定
選取 `Embedded + Data8` 或 `DedicatedGateway + Data8`；P8.0～P8.4 另行處理第一個
ChurchReport 的 `CentralGateway + Data8` 雲端部署。所有 Connector 都維持 deployment-owned、
fail-closed、無 request-time fallback。
