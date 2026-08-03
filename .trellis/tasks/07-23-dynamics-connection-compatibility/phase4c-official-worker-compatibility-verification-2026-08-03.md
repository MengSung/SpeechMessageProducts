# Phase 4C 官方 NuGet Worker 相容性 Harness 驗證紀錄（2026-08-03）

## 範圍與結論

本次完成官方 NuGet Worker 部署相容性 harness 的本機 deterministic gate，並修正
`ValidateOnly` 先前未驗證 Gateway HTTPS target 的 fail-open 缺口。此增量不使用
direct Web API、D365APP01 管理通道、IFD 精靈、AD FS 診斷或 Data8 fallback。

本文件僅記錄已取得的本機證據，**不宣告 Phase 4C、Phase 4、Phase 5 或 Phase 6
已完成**。`Package01FeeReadsEnabled` 維持 `false`；Data8、Embedded 與
`PowerPlatform.Dataverse.Client` 均未移除。

## 修改的安全契約

- `Invoke-DynamicsOfficialWorkerCompatibility.ps1` 的兩個明確模式現在都會先驗證
  Gateway target 必須是無 user-info、query、fragment 的 absolute HTTPS base URI。
  因此 `ValidateOnly` 不會認可一個之後 Live mode 無法安全執行的 deployment target。
- `ValidateOnly` 仍只讀取有上限的 manifest、Gateway overlay 與 worker-profile XML，
  驗證 selected profile 的 worker kind、CE version、package lock、generation、
  organization identity、XML identity union 與 executable SHA-256；不會建立網路、
  worker process、connection、cookie 或 session。
- Live mode 仍只允許固定 `runtime.health.whoami` operation。它使用無 cookie、無
  proxy、無 redirect、無 decompression 的單一 `HttpClientHandler`，所有 handler、
  client、request、response、stream、CTS 與 byte buffer 都有單一 owner，並在
  `finally` 確定 Dispose 或清零。
- 結果只輸出 sanitized scalar evidence；不輸出 Gateway／CRM endpoint、GUID、
  credential reference、token、cookie、connection string 或 CRM body。

## 執行證據

| 驗證 | 結果 | 證據 |
| --- | --- | --- |
| Harness regression | 通過 | `Invoke-DynamicsOfficialWorkerCompatibility.Tests.ps1`：全部通過；涵蓋非 HTTPS target、duplicate JSON property、artifact tamper、worker kind/package lock drift 與無 opt-in 模式。 |
| Worker deployment script | 通過 | `New-DynamicsOfficialWorkerDeployment.Tests.ps1`：全部通過。 |
| Worker publication | 通過（桌面權限） | `Publish-DynamicsOfficialWorkers.ps1` 重新產出 CE 8.2／9.1 artifact；兩個 executable 均與本次新 manifest 的 SHA-256 相符，且 publish output 未含 `worker-profile.xml`。同腳本測試於受限 sandbox 子行程 publish 失敗，但在既有桌面權限重跑通過；此差異不代表產品或 manifest 合約失敗。 |
| Worker source/project boundary | 通過 | `eng/Verify-DynamicsWorkerBoundary.ps1`：0 findings。 |
| SQL durable coordinator live gate | 通過 | 2026-08-03 以專用 `SpeechMessageDynamicsControlPlane` LocalDB 執行 `FullyQualifiedName~Live_sql_`：8 passed、0 failed、0 skipped。測試後 process-level selector 已清除，跨行程 test worker 為 0。 |
| Dynamics Release 測試 | 通過 | 2026-08-03 以受控 LocalDB selector 重跑完整套件：418 passed、0 failed、0 skipped。selector 僅存在於測試程序，finally 已清除。 |
| CE 8.2 worker Release 測試 | 通過 | 15 passed、0 failed。 |
| CE 9.1 worker Release 測試 | 通過 | 15 passed、0 failed。 |
| Release solution build | 通過 | `SpeechMessageProducts.sln`：0 warnings、0 errors。建置需在既有使用者權限下讀取 Windows SDK discovery directory。 |
| ChurchReport project-root 開機 | 通過（限定範圍） | 2026-08-03 以既有桌面權限和 Development 設定執行 `dotnet run --configuration Release --no-build --no-launch-profile -- --urls http://localhost:5081`；一次未登入 `GET /Login` 回應 200，並帶有 `Cache-Control: no-store, no-cache, must-revalidate, max-age=0`、`Pragma: no-cache` 與 antiforgery cookie。未提交表單、未呼叫 CRM。 |

## 本機瀏覽器與 Gateway 結果

- Codex in-app browser 已連線，但它的 URL policy 阻擋 localhost page reload，回報
  `Browser use URL policy`。沒有繞過、改用其他 browser surface，或略過安全政策。
  因此本輪 browser DOM/JavaScript assertion 沒有被記為通過；CLI 的 `/Login` 200
  只是本機網站啟動的獨立證據。
- 直接從 `bin\Release\net10.0` 執行 ChurchReport DLL 時，output directory 不含
  `appsettings.json`，導致 `Security:EnforceGlobalAuthorization` 回到預設值並使
  `/Login` redirect loop。這不是產品程式回歸；改以 project-root 的 `dotnet run`
  載入版本控制的 Development 設定後，`/Login` 回應 200。
- 本機 Gateway 以 Development profile 啟動時，LocalDB 回報無法自動建立 instance，
  Gateway 按 durable coordinator 規格 fail-closed，沒有 listener、in-memory
  substitute 或 CRM fallback。這是目前執行 sandbox 的 LocalDB 環境限制，不能當作
  Gateway／worker 生命週期或 CE 相容性成功證據。
- 本輪建立的 ChurchReport 和 Gateway 驗證程序都已停止；5080、5081 與 7244 listener
  已確認為零。臨時 isolated Data Protection directory 已移除，避免 key、cookie 或
  session 測試資料留存。

## 仍未關閉的 Gate

1. 取得權威的 CE 8.2 與 CE 9.1 approved/non-production profile identity、
   secret references、authentication mode、organization unique name/GUID 與最終
   Gateway/worker stable publish paths；不得猜測或由舊 IFD 設定推導。
2. 以最終網站 -> Gateway -> pinned official worker -> Organization Service 路徑，
   逐一完成 identity、read projection、paging、metadata、approved action、test-owned
   write/rollback、recycle、isolation 與資源 baseline operation matrix。
3. 取得真實 CE 8.2 與 CE 9.1 各自的相容性證據；任一版本的成功不可外推到另一版本。
4. Phase 4C 完成後才評估 Phase 5 單一 ChurchReport consumer migration；Phase 6 的
   Data8/WebApi/legacy SDK removal 仍不得提前執行。
