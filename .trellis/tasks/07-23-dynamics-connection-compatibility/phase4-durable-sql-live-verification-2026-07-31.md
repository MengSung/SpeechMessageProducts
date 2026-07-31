# Phase 4 耐久 SQL 控制平面實測證據（2026-07-31）

## 範圍與安全邊界

本次驗證的目標是 `SqlRuntimeHostSlotCoordinator` 的耐久控制平面行為；SQL
只保存跨主機的容量准入、fencing token、AdmissionEpoch 與 slot quarantine
狀態。它**不是** Dynamics CRM 業務資料庫，也不是使用者 Session、Cookie、JWT、
Access Token、密碼、憑證、CRM 回應內容或每使用者連線的儲存位置。

所有鍵值均為非機密、具版本的結構化控制平面鍵。測試中的工作負載、namespace
與 epoch 前綴皆為短暫的 `contract-*`／`epoch-contract-*` 值；測試結束後必須
不存在殘留資料列。

## 已驗證的 LocalDB 目標

- 只使用目前 Windows 使用者擁有的 `MSSQLLocalDB` 執行個體。
- 僅佈建專用資料庫 `SpeechMessageDynamicsControlPlane`；
  `Provision-DynamicsControlPlaneLocalDb.ps1` 會拒絕 CRM 資料庫名稱（例如
  `MSCRM_CONFIG`、`Jesus_MSCRM`）。
- 使用 Windows 整合驗證，沒有密碼，也沒有將連線字串寫入 repository、測試輸出
  或長期環境變數。
- 已佈建的 schema 指紋：
  `0D30A65241AADD6AFCABEA7368871272E68368658A9BDCF71EF0C105CCFF347F`。

這個界線讓 Gateway／Local Gateway／日後經核准的 Embedded host 可以共享「安全
容量」權威，但不會把 CRM 資料、使用者身分或憑證帶進 SQL 控制平面。

## 實測結果

以僅限目前 process 的 `SPEECHMESSAGE_DYNAMICS_SQL_TEST_CONNECTION` 提供專用
LocalDB 連線後，執行：

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --filter FullyQualifiedName~SqlRuntimeHostSlotCoordinatorTests `
  --no-restore
```

結果：`7 passed, 0 failed, 0 skipped`。

七項整合測試以真實 SQL transaction 驗證下列不變量：

1. 原子 slot 上限：競爭中的 host 不能共同超過設定的安全容量。
2. AdmissionEpoch／組態漂移 fencing：舊 epoch 或不相容組態不能重新取得准入。
3. stale renew 與 stale release 安全：過期或已被 fencing 的 lease 不能復活或誤釋放
   新 owner 的容量。
4. namespace 隔離：不同核准 namespace 的 lease 不會互相讀取、覆寫或消耗容量。
5. 到期 quarantine：失效 slot 不會在最大 outbound-work 壽命與結算邊際前被重複使用。
6. coordinator operation 計數：測試 drain 後回到基準值，沒有背景 SQL 操作、lease
   或管理物件持續保留。

## 清理與資源生命週期證據

測試完成後已以 SQL 查詢確認：

- `RuntimeHostSlotLease` 中 `contract-*` 前綴的資料列為 `0`；
- `RuntimeHostAdmissionEpoch` 中 `epoch-contract-*` 前綴的資料列為 `0`；
- LocalDB 為這次測試啟動的 `sqlservr` process 已在後續觀察中自然停止。

沒有使用 force-kill。LocalDB 的停止 API 曾回傳與 LocalDB registry 有關的假性錯誤，
但 process 與測試資料列的後驗檢查皆為乾淨；因此不以破壞性手段掩蓋清理問題。
連線變數僅供該測試 process 使用，驗證後不保留為跨工作階段狀態。

## 本次程式設定邊界補強

TDD 已加入並驗證兩類 fail-closed regression：

1. `SqlRuntimeHostSlotCoordinatorOptions.Validate()` 只接受 Windows 整合驗證，且即使
   `Integrated Security=true` 也拒絕 `User ID`／`Password` 欄位，避免 coordinator 的
   長生命週期設定字串保留 SQL 帳密。
2. `SqlRuntimeHostSlotCoordinator` 在建構時將已驗證的 connection string、command timeout
   與 quarantine 複製為 immutable snapshot；其後原始 options singleton 的 mutation 不能
   改寫既有 coordinator 的 SQL 路由或 lease 安全界限。
3. `InMemoryRuntimeHostSlotCoordinator` 改用 typed structural `SlotKey`，不再把 namespace
   與 host 以分隔字元串接；含 `|` 的兩組不同合法值不會互相更新 fencing token 或釋放對方
   的容量。

三個 regression 都先觀察到預期 failure，再以最小修正轉為 pass；它們不需要 CRM、
使用者 Session 或真實 SQL 帳密。

## 後續 canonical binding hardening（本機契約驗證）

後續檢查發現：舊 schema 雖以 `LeaseNamespaceId` 保護 epoch/slot，但兩個獨立
process 若錯設成不同 namespace，仍可能分別為同一實體 Dynamics Organization 取得完整
host-slot 預算。本次已將這個跨程序缺口收斂為下列 fail-closed 契約：

1. `RuntimeHostSlotLeaseRequest` 必須含有已驗證的 canonical Organization GUID 與
   normalized HTTPS base URI；SQL 的舊 namespace-only acquire overload 在任何 connection、
   transaction 或 background owner 建立前拒絕。
2. `RuntimeHostOrganizationBinding` 以 BIN2 string semantics 長期一對一繫結 namespace、
   GUID 與 URI。slot release 不會刪除 production binding；同一 Organization 以另一個
   namespace acquire 會回 SQL `51005`。
3. epoch 對 binding 加上 foreign key。舊 epoch 不能從 configuration digest 自動推回
   Organization；migration 發現未繫結資料列時回 SQL `51006`，要求先 drain/受控處理，
   而不是以猜測繼續 rollout。
4. LocalDB test cleanup 僅刪本次隨機建立的測試資料，並以 `slot lease -> epoch -> binding`
   的 FK 相依順序清除；這避免 opt-in 驗證自身留下無界 durable rows。

本次本機驗證命令與結果：

```powershell
dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --filter FullyQualifiedName~SqlRuntimeHostSlotCoordinatorTests `
  --no-restore --nologo
# 14 passed, 1 skipped (僅 opt-in LocalDB live contract)

dotnet test .\SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj `
  --no-restore --nologo
# 264 passed, 1 skipped (同一個 LocalDB live contract)

dotnet build .\SpeechMessageProducts.sln --configuration Release --no-restore --nologo
# 0 warnings, 0 errors
```

兩個 embedded/check-in schema migration block 已逐行比對一致。因目前
`MSSQLLocalDB` registry configuration 仍無法讀取，這些是完整的本機 unit／release
證據，但**不是**新 canonical binding schema 的 fresh live SQL transaction 證據；在
LocalDB 修復後，必須重跑 opt-in live contract，才可把該小項標為已重驗證。此修補沒有
啟用任何 ChurchReport consumer traffic，`Package01FeeReadsEnabled=false` 保持不變。

## 同日再驗證的 LocalDB runtime 狀態

在加入「只允許整合驗證、拒絕 SQL 帳密欄位」的 regression test 後，嘗試以相同的
專用 LocalDB 重新執行 live SQL contract。這次 host 的 LocalDB automatic instance
建立失敗；Application Event Log 顯示 `SQLLocalDB 17.0` Event ID `528`，ODBC `IM003`
與 system error `126`。原生 `sqllocaldb start MSSQLLocalDB` 可以啟動 `sqlservr` process，
但 `sqllocaldb info` 與 SqlClient 連線仍無法讀取 LocalDB instance registry。

此現象發生在 coordinator 開啟連線之前，且完整非 live Dynamics 測試仍通過，因此它是
目前 Windows LocalDB/ODBC runtime 的外部維護問題，不是本次 coordinator 邏輯的
regression。沒有修改 Windows 安全設定、SQL schema、CRM、DNS 或使用 force-kill 來繞過。
本次為診斷而啟動的 `sqlservr` process 在原生停止命令回報 registry error 後，經短暫
觀察已回到 `0` 個 process，未留下測試 owner 的常駐資源。
先前已完成的 `7 passed, 0 failed, 0 skipped` 實測結果仍保留為當時的 transaction
證據；在 LocalDB runtime 修復前，本機無法重複的只有這一個 opt-in live SQL check。

## 這份證據代表什麼、尚未代表什麼

這是 Phase 4 的「耐久 SQL transaction／fencing／清理」實證，證明 Local Gateway
控制平面不需要 CRM SQL，也不依賴 in-memory coordinator 來取得跨 host 的容量安全。
它尚**不是**正式多副本部署、HPA/IaC 上限或真實 CE 流量的最終證明；那些仍需依
Phase 4 的 multi-owner、fault、soak 與真機 smoke gates 分別完成。

CE 9.1 的真機 lane 仍獨立受 D365APP01 CRMWeb 的 server-side
`UriFormatException`／HTTP 500 阻塞。該狀況不應促使我們改 DNS、hosts、Kerberos、
WinRM、ADFS、IIS 或 CRM database；待 CRM 管理者以受支援的設定修正後，再重新執行
connector-owned `WhoAmI` smoke。

## 2026-07-31 Phase 4 封閉回應邊界驗證

本次增量把 Dynamics Web API 上游 OData JSON 的生命週期收回 connector request scope：
`OperationExecutionResult.Data` 僅能帶出封閉的 `OperationResponseData`，其可見分支只有
`WhoAmI`、Package 1 fee records 與 stor-lesson records。原始 `JsonElement`、`object` payload、
CRM host/API root、`@odata` annotation、continuation、credential、token 與 session 不可跨到
Gateway、ProductClient、queue 或 cache。每一頁的 request、response、stream、ArrayPool buffer、
timeout CTS、visited continuation set 與 aggregation 都仍由單一 request scope 擁有，失敗、取消、
retry、跨 root/cycle/limit continuation 都丟棄 partial result 並確定釋放。

新鮮驗證證據（2026-07-31）：

- `dotnet test SpeechMessage.Dynamics.Tests\SpeechMessage.Dynamics.Tests.csproj --filter "FullyQualifiedName~DynamicsWebApiClientTests|FullyQualifiedName~GatewayProductClientTests|FullyQualifiedName~Package01FeeReadClientTests|FullyQualifiedName~Package01OperationRegistryTests|FullyQualifiedName~OperationRegistryAgreementTests|FullyQualifiedName~GatewayKestrelNegotiateTests|FullyQualifiedName~GatewayRequestBodyBoundaryTests|FullyQualifiedName~GatewayWorkloadBoundaryTests|FullyQualifiedName~MultiProfileRuntimeTests|FullyQualifiedName~OperationDispatchPreparerTests|FullyQualifiedName~Phase4IsolationSoakTests" --no-restore --configuration Debug`：171 passed、0 failed、0 skipped。
- `dotnet build SpeechMessageProducts.sln --configuration Release --no-restore`：0 warnings、0 errors。
- `dotnet test SpeechMessageProducts.sln --configuration Release --no-restore --no-build`：874 passed、0 failed、1 skipped；唯一 skipped 是明確 opt-in 的 live SQL coordinator contract，並非測試失敗。
- 已對本次變更的 C# 檔驗證 UTF-8 without BOM、CRLF-only 與 final CRLF；`git diff --check` 無輸出。
- 生產 response path 掃描沒有殘留 `OperationExecutionResult.Success(new { ... })`、`object? Data` 或以 `JsonElement` 指派 response data；ProductClient 對 `/api/data/` 的唯一命中仍是設定驗證器的拒絕規則。
- `SpeechMessageProducts.ChurchReport/appsettings.json` 與 `appsettings.Development.json` 的 `Package01FeeReadsEnabled` 仍為 `false`；本次契約強化沒有打開 consumer traffic。

本機 Phase 4 封閉契約、資源釋放、registry agreement 與 Release 回歸已完成；真機 CE 9.1 證據仍只受
`sunnyvalechback.speechmessage.com.tw` 的 D365APP01 CRMWeb HTTP 500 / `UriFormatException` 獨立阻塞。
在 CRM 管理端以受支援的 Claims/IFD 設定流程修正前，不能將 HostIdentity、ADFS 或任何 product migration
標記為已通過，也不能以修改 DNS、hosts、Kerberos、WinRM、IIS 或 CRM database 作為猜測式修復。
