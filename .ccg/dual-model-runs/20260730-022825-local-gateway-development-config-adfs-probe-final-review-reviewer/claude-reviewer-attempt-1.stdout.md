# 最終審查報告

**結果：PASS**

## Critical
無。以下為本次獨立驗證涵蓋的關鍵不變量，均未發現任何 release blocker：

- **LocalDB 隔離與帳密缺失**：`SpeechMessage.Dynamics.Gateway/appsettings.Development.json:7` 的 `DynamicsControlPlane` 連線字串使用 `Integrated Security=true`、`(localdb)\MSSQLLocalDB`、專用 `SpeechMessageDynamicsControlPlane` database，`Max Pool Size=32`、`Connect Timeout=5`，未含任何 SQL 帳密。`SpeechMessage.Dynamics.Gateway/Program.cs:125-140` 顯示此連線字串是唯一被 `AddSqlRuntimeHostSlotCoordinator` 消費的來源；`SqlRuntimeHostSlotCoordinator.EnsureSchemaAsync`（`SpeechMessage.Dynamics.WebApi/Capacity/SqlRuntimeHostSlotCoordinator.cs:236-260`）只驗證 schema 是否存在，缺漏時以 `THROW 51002` fail closed，未見任何 `CREATE TABLE`/自動 DDL 呼叫路徑。
- **Development CRM 目標 fail-closed**：Gateway base profile（`appsettings.json:53-54`）指向正式組織端點；Development override（`appsettings.Development.json:42-43`）改為 checked-in 不可路由 `.invalid` 位址，符合 ASP.NET Core 標準 precedence 覆寫語意，且 `SecretReference`/`ClientId`/`AuthorityUri` 等欄位全數清空，未見 fallback 至其他 profile 的程式路徑。
- **ChurchReport Local Gateway 對齊且 Package 1 關閉**：`SpeechMessageProducts.ChurchReport/appsettings.Development.json` 明確設定 `Package01FeeReadsEnabled=false`、`ExecutionMode=Gateway`、`ProfileAlias=crm82`、HTTPS loopback `Endpoint`、`ApiPrefix=/v1`；base 設定（`appsettings.json:559-563`）為 `Embedded`/`false`，符合標準 precedence。`DonationDynamicsAccessBootstrap.TryCreatePackage01Client`（`SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs:108-114`）在 flag 為 false 時於建立任何 executor/HTTP pool 之前即 `return null`；`DynamicsGatewayPreflightHostedService.StartAsync`（`SpeechMessageProducts.ChurchReport/Services/DynamicsGatewayPreflightHostedService.cs:83-92`）同樣先檢查 flag 再决定是否解析 executor，flag=false 時嚴格 no-op，未建立 ProductClient/HTTP handler/token cache/timer。
- **退役腳本 fail-closed**：`docs/scripts/Invoke-AdfsTokenProbe.ps1` 已整檔清空舊邏輯，`param()` 不接受任何參數，執行即 `throw` 固定 ASCII 訊息並導向 `/diagnostics/adfs-authorize`，無檔案寫入、無 `Invoke-RestMethod`、無帳密讀取路徑。對應測試 `Legacy_adfs_token_probe_is_retired_without_password_or_result_output_paths`（`SpeechMessage.Dynamics.Tests/AdfsOAuthTokenProviderTests.cs`）與 `Gateway_development_configuration_uses_dedicated_localdb_and_fail_closed_crm_target`（`SpeechMessage.Dynamics.Tests/SqlRuntimeHostSlotCoordinatorTests.cs`）、`Development_configuration_selects_local_gateway_while_package01_reads_remain_disabled`（`ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs`）皆已由我獨立以 `dotnet test --filter` 重跑，全數通過。

## Warning
- `SpeechMessage.Dynamics.Gateway/appsettings.Development.json` 中既有（本次未變更）的 `DynamicsGateway:WorkloadBindings` 由 base 的 JSON array（index `0` = `IIS APPPOOL\ChurchReport`）與 Development 的 object key `"1"` 合併，兩者在 .NET configuration 展平後是**相加**而非**取代**。因此在本機執行時，設定層面仍同時保留正式 IIS APPPOOL binding（index 0）與本機 Windows 使用者 binding（index 1）。由於本機環境不存在該 APPPOOL identity，目前不構成可利用的越權風險，但如未來 base/Dev 兩份 workload binding 語意持續用「新增 index」而非「覆寫」，需注意這不是嚴格意義上的 override，僅為既有結構、非本次 diff 引入，故列為 Warning 而非 Critical。

## Info
- 所有六個 primary 變更檔案（`appsettings.Development.json` ×2、`Invoke-AdfsTokenProbe.ps1`、三份測試檔）均獨立驗證為 UTF-8 without BOM、CRLF-only、以 CRLF 結尾；`git diff --check` 無空白錯誤。
- 新增程式碼掃描未發現帳密/token 字面值外洩；僅出現在斷言（assertion）字串或註解描述中（例如 `password 參數`、`Password.Should().BeNullOrEmpty()`），未印出實際敏感值。
- `Embedded`/`Data8` 依賴確認保留：solution 仍含 `PowerPlatform.Dataverse.Client` 專案，其組件中繼資料確認底層套件為 `Data8.PowerPlatform.Dataverse.Client`（`MarkMpn,Data8 Ltd`），未被移除。
- 本次 diff 中 `SpeechMessage.Dynamics.Gateway/Program.cs` 的 request-body/JSON 深度限制與 `ChurchReport.MemberInfo.Tests/DonationDynamicsAccessBootstrapLifecycleTests.cs` 的 process-host 生命週期大規模改寫，經比對 `.ccg/dual-model-runs` 歷史記錄，屬於先前已個別審查過的切片（gateway-http-canonical-queue-bounds、churchreport-local-gateway-session-lifecycle），非本輪宣告範圍的新增內容，故未重複列為本切片缺陷，僅確認其未破壞本切片的 6 項不變量。

## 剩餘驗證差距（非本切片缺陷）
1. 真實 Local Gateway/ChurchReport 併發啟動的瀏覽器端 E2E（依 evidence 已執行，但屬單次快照，非本審查可重放驗證）。
2. CE 8.2/9.1 正式 WhoAmI、AD FS interactive 授權碼、Operation Matrix 對正式（非 `.invalid`）端點的驗證，仍待 Phase 5 gate。
3. 跨程序容量、fault/soak/performance 基準，仍待後續 Phase 6 gate。
4. OData 絕對 URL（`@odata.context`／`@odata.nextLink`）投影政策，需在啟用真實 Package 1 前於伺服器端確認消費/改寫，避免對產品端洩漏內部 CRM URL。

## 關鍵確認
- **Package 1**：`DynamicsAccess:Package01FeeReadsEnabled` 在 base 與 Development 設定中均為 `false`；程式碼路徑（`TryCreatePackage01Client`、`DynamicsGatewayPreflightHostedService`）均在流量觸發前以此旗標 fail closed。
- **Embedded/Data8**：`PowerPlatform.Dataverse.Client`（底層 `Data8.PowerPlatform.Dataverse.Client`）專案與參考均完整保留，未被移除或停用。

---
