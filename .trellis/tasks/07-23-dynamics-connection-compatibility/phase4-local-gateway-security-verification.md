# Phase 4 Local Gateway Security Foundation — Task 5 驗證紀錄

## 結論狀態

`DONE_WITH_CONCERNS`

Task 5 的單機 LocalDB provisioning 與 live durable coordinator contract 已完成實證；這份結果只證明同一個 Windows 使用者、同一台 Development 工作站與固定 `(localdb)\MSSQLLocalDB` 的 SQL 原子行為。它不代表 Central Gateway、多主機、跨服務帳號、網路分割、HA／failover 或 production 容量協調已通過。

## 責任、信任邊界與生命週期

- `docs/scripts/Provision-DynamicsControlPlaneLocalDb.ps1` 是 schema 建立的唯一人工 owner。Gateway startup 與 live test 都只執行 `VerifySchemaAsync`，缺少 database/object 時 fail closed，不會自行修復錯誤部署。
- Script 只接受 instance `MSSQLLocalDB`、server `(localdb)\MSSQLLocalDB`、database `SpeechMessageDynamicsControlPlane` 與 checked-in `eng/dynamics-control-plane-schema.sql`。它不建立 login、user、role，也不授與權限。
- Named mutex 序列化同一 Windows session 的 provisioning；所有 native process 都同步等待並檢查 `$LASTEXITCODE`。失敗時只保留最多 4096 字元的實際 native diagnostic，成功時不轉送 raw output；Script 不建立 timer、背景工作、暫存 SQL 檔、共享 connection 或未觀察 Task，mutex 在 `finally` 確定釋放。
- Live test 的 connection string 只由 process environment 暫時擁有，不寫入 source、log 或 assertion。未提供環境變數時 xUnit 明確回報 skipped，而不是 silent return 成功。
- Live test 每次使用隨機 namespace，並在 contract 結束後以參數化 SQL 刪除自己建立的 lease／epoch rows；connection 與 command 都由 `await using` 唯一擁有。Cleanup 與 `ActiveDatabaseOperations` sentinel 各自執行，單一失敗以 `ExceptionDispatchInfo` 保留原 stack，多重失敗聚合回報，避免 cleanup 遮蔽原始 SQL／assertion root cause，也防止 LocalDB control-plane 資料無界成長。
- Provisioning 是低頻人工操作，因此選擇多次短生命週期 `sqlcmd`、完整 schema 驗證與清楚失敗點；不以常駐連線、平行 DDL 或略過驗證換取速度。

## RED 證據

### 修改前基線

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~SqlRuntimeHostSlotCoordinatorTests" --no-restore --logger "console;verbosity=minimal"
```

結果：`4 passed, 0 failed, 0 skipped`。原本的 live test 在缺少 `SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION` 時直接 `return`，因此這個綠燈不構成 live SQL 證據。

### 新契約測試 RED

同一命令在只修改 `SqlRuntimeHostSlotCoordinatorTests.cs`、尚未建立 script 時執行。

結果：`1 failed, 4 passed, 1 skipped, total 6`。

唯一失敗：

```text
SqlRuntimeHostSlotCoordinatorTests.Localdb_provisioning_script_is_explicit_idempotent_and_least_privilege
Expected File.Exists(scriptPath) to be true ... but found False.
```

這是預期 RED：缺少人工 provisioning script。Live contract 同時明確顯示 `[SKIP]`，證明缺環境變數不再被報成 live success。

## Provisioning 實證

### 命令

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Provision-DynamicsControlPlaneLocalDb.ps1
```

### Windows PowerShell 相容性修正紀錄

前兩次嘗試都在任何 SQL 動作前失敗，沒有建立或修改 database：

1. Windows PowerShell 5.1 以 Big5 解析 UTF-8 without BOM script 時，繁體中文單行註解可能讓後續 ASCII token 被錯誤配對。把繁體中文安全說明改成獨立 `<# ... #>` block comments 後，`System.Management.Automation.Language.Parser.ParseFile` 回到 `0` error。
2. Windows PowerShell 5.1 在 `param(...)` 預設值綁定階段尚未可靠提供 `$PSScriptRoot`。改為完成參數綁定後才計算預設 schema path，避免依賴 caller current directory。

這兩項修正保留 repository 要求的 UTF-8 without BOM、CRLF 與深入繁體中文文件，同時讓計畫指定的 `powershell.exe -File` 命令可直接執行。

### 最終結果與 idempotency

最終命令成功，立即重跑也成功；第二次沒有重建 database 或 schema object。

```text
InstanceName   : MSSQLLocalDB
ServerName     : (localdb)\MSSQLLocalDB
InstanceState  : Running
DatabaseName   : SpeechMessageDynamicsControlPlane
ProductVersion : 17.0.4025.3
SchemaSha256   : 0D30A65241AADD6AFCABEA7368871272E68368658A9BDCF71EF0C105CCFF347F
```

Schema object 驗證：

- `dbo.RuntimeHostSlotLease`
- `dbo.RuntimeHostAdmissionEpoch`
- `dbo.RuntimeHostFencingSequence`

固定 database allowlist 的負向命令：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\docs\scripts\Provision-DynamicsControlPlaneLocalDb.ps1 -DatabaseName MSCRM_CONFIG
```

結果：exit code `1`，PowerShell `ValidateSet` 在 script body／SQL 之前拒絕非 `SpeechMessageDynamicsControlPlane` 目標。

## Live SQL contract GREEN

### 環境設定與命令

連線字串只有非秘密的 LocalDB 形狀；沒有 username、password、token 或 remote endpoint：

```powershell
$env:SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION = "Server=(localdb)\MSSQLLocalDB;Database=SpeechMessageDynamicsControlPlane;Integrated Security=true;Encrypt=false;Pooling=true;Max Pool Size=32;Connect Timeout=5;Application Name=SpeechMessage.Dynamics.Tests"
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~SqlRuntimeHostSlotCoordinatorTests" --no-restore --logger "console;verbosity=minimal"
Remove-Item Env:SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION
```

結果：`6 passed, 0 failed, 0 skipped, total 6`。

### 實際測試名稱

1. `Options_reject_unbounded_or_unsafe_values`
2. `Schema_is_scoped_to_the_standalone_control_plane_database`
3. `Localdb_provisioning_script_is_explicit_idempotent_and_least_privilege`
4. `Gateway_startup_verifies_schema_without_invoking_provisioning_or_schema_creation`
5. `Coordinator_outage_fails_closed_without_retained_connection_or_task`
6. `Live_sql_contract_is_atomic_fenced_quarantined_and_namespace_isolated`

### Live contract 覆蓋

- 真實 `VerifySchemaAsync`，不呼叫 `EnsureSchemaAsync`。
- Admission epoch／configuration digest mismatch 由 SQL error `51003` fail closed。
- 32 個併行 acquire 在 `MaximumRuntimeHosts=2` 時只產生 2 個 lease。
- Renew 成功且 fencing token 單調遞增。
- Stale renew 與 stale release 被拒絕，不會刪除新的 fenced lease。
- 不同 namespace 有獨立 bounded slot。
- Release 後在 quarantine 到期前不可重用；到期後 replacement 可取得更大的 fencing token。
- SQL outage 例外向上傳播，`ActiveDatabaseOperations == 0`。
- Live test 結束後刪除本次隨機 namespace；2026-07-29 最新 live run 後以 `epoch-contract-%`／`contract-%` 查詢得到 `LeaseRows=0`、`EpochRows=0`。歷史遺留的 16 個 lease rows／12 個 epoch rows 已在確認 `HostInstanceId IS NULL` 後從專用 Development LocalDB 清除，無法復原但只屬測試資料。

## 外部審查與修正

- 初次 CCG run `20260729-191836-dynamics-localdb-task5-reviewer` 由 Gemini 與 Claude 都成功完成，`ok=true`、`degradedFallback=false`、`quotaBlocked=false`，無 Critical；完整 artifacts 位於 `C:\Users\Administrator\AppData\Local\Temp\ccg-dynamics-task5-review\20260729-191836-dynamics-localdb-task5-reviewer`。
- Gemini 與 Claude 都指出 cleanup 可能遮蔽原始測試失敗；已改為分別捕捉 contract、durable cleanup 與 lifecycle sentinel 例外，單一例外原樣重拋，多重例外聚合。
- Claude 指出 native output 原先被丟棄而難以診斷；已為每個 `sqllocaldb`／`sqlcmd` 呼叫擷取失敗輸出，以 4096 字元硬上限加入例外，且不輸出連線字串或 credential。
- 初次審查也辨識到 PowerShell／Markdown 為 LF-only；三個受管檔案已統一為 UTF-8 without BOM＋CRLF。
- 修正後 CCG run `20260729-193250-dynamics-localdb-task5-final-review-reviewer` 再次由 Gemini 與 Claude 完整成功，`ok=true`、`degradedFallback=false`、`quotaBlocked=false`；artifacts 位於 `C:\Users\Administrator\AppData\Local\Temp\ccg-dynamics-task5-final-review\20260729-193250-dynamics-localdb-task5-final-review-reviewer`。Gemini 回報無 Critical／Warning；Claude 對程式與實測也回報無 Critical，唯一 Warning 是 artifacts 不在 repository `.ccg`。
- 該 artifact-location Warning 已經實碼與流程核對：兩次 run 都由專案 `Start-CcgDualModelRun.ps1` 自我修復入口產生完整 prompt、health、stdout、stderr 與 summary；使用 external temp 是為了遵守本次「只能建立／修改三個指定檔案」的明確 ownership，不能為保存 review 另外修改 `.ccg`。本文件記錄精確路徑作為可定位證據，並保留此 scope-driven concern；它不是安全、正確性或生命週期缺陷。

## 品質與編碼 Gate

以下項目會在最終外部 review 後重新執行並以 fresh output 關閉：

- [x] Focused unit contract：無 live environment 時 `5 passed, 1 skipped`。
- [x] Focused live contract：`6 passed, 0 skipped`，測試前綴 SQL residue 為 `0 lease / 0 epoch`。
- [x] Provisioning script 連續重跑成功；兩次 schema SHA-256 都是 `0D30A65241AADD6AFCABEA7368871272E68368658A9BDCF71EF0C105CCFF347F`。
- [x] Windows PowerShell 5.1 parser：`0` error。
- [x] 三個受管檔案 strict UTF-8 without BOM、CRLF；無 bare LF／CR。
- [x] C# XML／PowerShell／Markdown 繁體中文責任、信任、owner、concurrency、failure、cleanup、performance 說明。
- [x] `git diff --check`。
- [x] 三個受管檔案 scoped secret／grant scan：`0` hit。
- [x] CCG Gemini＋Claude review：兩個 backend 都完整成功、無 Critical；所有 Warning 已經實碼驗證、修正或以明確 scope 證據處理。

## 尚未關閉的 release blockers

- LocalDB 是 user-scoped、單機 Development 技術；沒有證明 Central Gateway 的 network SQL、跨程序／跨主機 clock、fencing、備援、backup／restore 或服務帳號權限模型。
- 尚未完成 ChurchReport Host configuration ownership、localhost browser E2E、CE 9.1 profile activation、真實認證與 `WhoAmI`。
- Durable audit intent、idempotency ledger、fair dispatch／starvation bound、multi-host capacity、coordinator outage／network partition、fault／soak／performance 仍是獨立 gate。
- CE 8.2／9.1 真實 server matrix、Phase 5 consumer migration 與 Phase 6 Data8／legacy SDK removal 尚未完成。
- 因此本文件只關閉 Local Gateway Security Foundation 的 Task 5，不宣告整體 Phase 4、Central production 或 Dynamics migration 完成。
