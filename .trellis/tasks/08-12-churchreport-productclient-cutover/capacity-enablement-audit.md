# P7.4 Package01 Feature Gate Capacity Enablement Audit

## 結論

**NO-GO：`DynamicsAccess:Package01FeeReadsEnabled` 必須維持 `false`。**

這是針對「實際開啟 ChurchReport Package01 consumer 流量」的 deployment gate 結論，
不是對 Package01 DTO、ProductClient、Data8 connector 或已完成的本機 contract test
的否定。本檔只使用 repository 來源進行唯讀稽核；沒有修改設定、送出 CE request、
建立 fixture、切換流量或啟動 P7.5/P8。

## 必要的兩條證明路徑

P7.4 design 要求在任何 capability gate 開啟前，必須有且只有下列其中一條完整證據：

1. legacy ToolUtility 與 Gateway/Data8 對同一個 canonical Organization 使用相同的、
   durable 的 admission/host-slot authority；或
2. deployment/runtime owner 提供並實際驗證 drain-first、non-overlap runbook，證明
   legacy 已停止接收該 Organization 流量後，Gateway path 才開始接收。

目前兩條都未成立。

## 路徑一：沒有共用 durable admission authority

1. `SpeechMessageProducts.ChurchReport/appsettings.json` 與
   `appsettings.Development.json` 的 `Package01FeeReadsEnabled` 都明確為 `false`。
   這只證明目前尚未切流，不是 enablement 證明。
2. `DonationDynamicsAccessBootstrap.EnsureGatewayOnly` 要求 flag=true 時只能使用
   `DedicatedGateway` 或 `CentralGateway`；它有正確的 request/profile ownership，卻
   不會把 legacy ToolUtility 納入 admission manager。
3. `SpeechMessage.Dynamics.Connectors.Data8/Data8ProfileRuntime.cs` 在 Embedded 與
   Dedicated Data8 runtime 建立 `new InMemoryRuntimeHostSlotCoordinator()`。該 coordinator
   僅能保護自己的行程，不能成為 legacy/Gateway 的 durable shared authority。
4. Gateway 的 `Program.cs` 僅在 **非 Dedicated** 的 runtime branch 註冊
   `AddSqlRuntimeHostSlotCoordinator`。目前 ChurchReport production-like setting 是
   `DedicatedGateway`，而 Dedicated branch 組成 `Data8ProfileRuntime`；因此不可把
   Central/Official-Worker SQL coordinator 的 unit/integration tests 當成此 ProductClient
   路徑已取得 durable admission 的證據。
5. `ToolUtility/Factory/ToolUtilityFactory.cs` 與
   `ToolUtility/ConnectionOperations/CrmConnectionPool.cs` 顯示 legacy 使用 process-wide
   singleton 和自己的 `SemaphoreSlim` pool。搜尋 legacy ToolUtility／ChurchReport 路徑沒有
   `IOrganizationAdmissionManager`、`IRuntimeHostSlotCoordinator` 或 canonical Organization
   host-slot lease 接線。
6. `DonationPaymentManager` 與 `DonationDedicationFeeFormService` 仍持有 ToolUtility，
   即使之後某一筆 fee read 使用 typed branch，既有 contact/fee、payment 及其它 legacy
   工作仍可能同時連到同一 Organization。沒有共用 authority 時，不可假定兩者不重疊。

`SqlRuntimeHostSlotCoordinator` 本身是 `IsDurable=true` 的可用基礎元件；
`SqlRuntimeHostSlotCoordinatorTests` 與 cross-process tests 證明其設計能力，卻沒有證明
ChurchReport legacy ToolUtility 已使用同一個 namespace、canonical Organization binding、
epoch/fencing 和 admission permit。元件存在不等於部署接線完成。

## 路徑二：沒有已驗證 drain-first、non-overlap runbook

已搜尋 P7.4 task、Gateway deployment guide、connection-management specification、
ChurchReport setting 和 deployment scripts，未找到可由 deployment owner 執行並留下證據的
Package01 drain-first runbook。現有文件只有把它列為必要前置條件，沒有記錄：

- 停止 legacy 新 request 的 deployment-owned 動作與驗證方式；
- 已在途 ToolUtility operation 的 bounded drain/timeout 與 completion evidence；
- 對同一 canonical Organization 的 Gateway readiness/admission evidence；
- flag 開啟、受控 smoke、觀測窗與單一 capability rollback 的順序；
- rollback 後對 listener、pool、permit、lease、process、handle 的基線確認。

因此不能用「目前 flag=false」或「Gateway 可通過 isolated test」推論兩條路徑不會同時承接
Organization 流量。

## 恢復 enablement 的精確條件

以下任一路徑完成前，不得改動 Package01 feature flag：

### A. 共用 durable authority

1. 把 legacy ToolUtility 與 Package01 Gateway/Data8 path 都接到同一 deployment-owned
   durable coordinator，並使用相同 canonical Organization binding、lease namespace、
   admission epoch/configuration digest 與 aggregate capacity plan。
2. 在不含 credential、endpoint、CRM ID 或使用者資料的 controlled test 中，以兩個實際
   host/path 交錯送出 bounded synthetic operation；證明 aggregate capacity 不會超限、
   fencing/renewal loss fail closed、drain 後 permit/slot/process 資源回到基線。
3. 以新的 P7.4 evidence record 記錄 deployment binding、read-back category、rollback owner
   與測試結果；不可將原本的 Data8 coordinator unit test 重新標示為 cutover evidence。

### B. 驗證過的 drain-first non-overlap

1. 由 deployment/runtime owner 撰寫並在隔離環境實際演練 runbook：先封閉 legacy 新工作、
   等待既有工作確定 drain，再確認沒有 legacy listener/active operation，最後才開啟單一
   Package01 gate。
2. 受控 smoke 只改 deployment-owned gate；不在 request 中更換 ConnectorKind、ProfileAlias、
   CE version 或 endpoint。
3. rollback 必須先關閉該 capability gate、drain typed requests、確認資源基線，才恢復 legacy
   intake；timeout、ambiguous、read-back mismatch 或 cleanup uncertainty 一律 no-go，絕不重試。

## 本地可繼續工作

- 維持 P7.4 already-migrated-disabled read consumers 的 contract、cancellation、A/B isolation
  與 lifecycle tests。
- 依 authoritative 70-row matrix 為 temporary-legacy rows 建立其真正的 owner task；不可為了
  P7.5 zero-reference 目標重建 SDK entity 或把 write workflow 偽裝成 read cutover。
- P7.2 write/action/function family 可獨立完成本機設計、DTO、idempotency、read-back、
  reconciliation、rollback、fresh-fixture/CE evidence plan；舊 Slice C cycle 不可重試或復用。

本 no-go 只封閉 P7.4 enablement/traffic switch。它不封閉安全的本機 implementation、quality
verification、task record 或不依賴 gate 的後續 P7 capability child。
