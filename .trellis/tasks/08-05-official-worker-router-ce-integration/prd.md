# P6 Official Worker Router 接入與 CE 整合驗證 PRD

> 狀態：Phase 1 規劃中。此文件不是 `task.py start`、產品流量切換或 CE 真機呼叫的授權。

## 目標與使用者價值

把既有、版本隔離的 Official CRM 8.2／9.1 Worker 資產接入目前已由 Data8 使用的
`IConnectorRouter`、`IConnectorPool` 與 `IConnectorLease` 邊界。如此 Connector 的選擇固定由
deployment-owned profile 決定，產品端與請求端都不能選擇 SDK、CE 版本、endpoint、credential 或
fallback；同時保留 Data8 為永久合法的 ConnectorKind。

P6 的可交付結果是「Official Worker 已成為可由 Router 選取、具世代與 lease 生命週期的第二種
Connector」，並以離線測試證明隔離、drain、dispose 與無洩漏契約。在取得額外明確授權後，才以
受控、read-only 的 CE 8.2／9.1 operation matrix 取得真機證據；離線測試或部署設定不得被宣稱為
CE 相容證據。

P6 分成兩個不改變順序的驗收里程碑：**P6.1 離線 Router 接入**完成程式、離線 lifecycle／soak 證據與
品質閘門；**P6.2 CE read-only 整合驗證**只在另行授權的視窗取得 CE 8.2／9.1 evidence。P6.1 通過不等於
P6 結案，P6.2 未完成時 task 仍維持 `in_progress`，P7 Parent 與 P7.0 不得啟動。

## 已確認事實

- P5 `dedicated-gateway-alignment` 已完成、提交並封存。其 Dedicated 分支只使用
  `Data8ProfileRuntime`；非 Dedicated Gateway 分支則載入 `DynamicsProfileDefinition` 並註冊
  `AddSpeechMessageDynamicsOfficialWorkers(...)`。
- `docs/dynamics-connection-management-spec.md` 及
  `docs/dynamics-connection-management-plan.md` 是本任務的權威來源。若
  `.trellis/spec/backend/dynamics-gateway-hosting-version-routing.md` 的歷史文字與它們衝突，
  以前兩者為準。
- `ConnectorKind.Data8` 是永久合法選項，並支援 CE 8.2、CE 9.1；
  `OfficialCrm82Worker` 只相容 CE 8.2，`OfficialCrm91Worker` 只相容 CE 9.1。ConnectorKind 與
  ConnectionMode 為獨立維度，產品端只能選擇 ConnectionMode、ProfileAlias 與必要的 Gateway
  endpoint。
- Data8 已有 `(ProfileAlias, GenerationId)` 隔離的 Pool、Lease、Router 與 generation registry。
  Pool 是 lease/client/admission cleanup 的既有參考實作；同一 Organization 的容量由 canonical
  Organization admission owner 共享，但不可共用 client 或任何 profile mutable state。
- Official Worker 已有版本固定的 net48 process、bounded length-prefixed nonce-bound IPC、process/pipe
  lifecycle、profile generation、drain、dispose、admission registry 與 focused tests。現況則以
  `ProfileRoutedOperationExecutor` 直接註冊為 `IDynamicsOperationExecutor`，尚未是
  `IConnectorPool`／`IConnectorLease`／`IConnectorRouter` 的正式 Connector 實作。
- Official Worker 目前已有 allowlisted `runtime.health.whoami`、
  `runtime.pool.validate.connection` 及
  `fee.dedication.retrieve.by.contact.date.range` bounded read contract；它們不是任意 CRM SDK、
  generic CRUD、FetchXML 或 product cutover 授權。
- ChurchReport 的 Gateway consumer flag 仍為 false，且 ChurchReport 仍有 ToolUtility/CRM SDK
  production dependency。這些屬 P7 Parent/P7.5，不在 P6 變更。

## 工作評估

本 task 為 **L（高複雜度）／高風險**：它跨越 Gateway composition、profile routing、兩個跨程序
Connector、CE version compatibility、admission、process/pipe/stream/handle lifecycle 與 credential
trust boundary。任何 session、profile、credential、permit、process、timer、task 或 handle 洩漏，以及
任何 fallback 或錯誤 Connector routing，皆為 release blocker。

## 功能需求

1. **第一級 Router Connector**：Official CRM 8.2 與 9.1 Worker 必須以 deployment-owned
   `ConnectorKind` 接入統一 Router。Router 只讀 `ResolvedProfile` 的 ConnectorKind、ProfileAlias 與
   GenerationId；request 不得指定或覆寫 connector、CE version、OrganizationId、endpoint、credential
   或 SDK package。
2. **相容性與 fail-closed**：Profile 載入期必須拒絕 Official 8.2 × CE 9.1、Official 9.1 × CE 8.2、
   未登錄 generation 或未知 ConnectorKind。錯誤、worker startup 失敗、IPC protocol failure、逾時與
   cancellation 均不得改用 Data8、另一個 Worker、Embedded、Dedicated、Central 或任何其他 transport。
3. **一次且僅一次的 admission**：每個 operation 只可由一個 connector lease 持有一個 canonical
   Organization admission permit。不得在 Router、ProfileRuntime、Worker supervisor 與 Pool 的任兩層
   重複取得 permit，也不得在 runtime lease、worker slot 或 IPC cleanup 尚未完成前歸還 permit。
4. **Profile／generation 隔離**：每個 Official Worker pool 以 `(ProfileAlias, GenerationId)` 隔離；
   一個 alias 同時最多一個 Active 和一個 Draining generation。Draining generation 拒絕新 lease，
   只在既有 lease 歸零後才清理 process、pipe、stream、timer、registration、handle 與 registration。
5. **資源所有權與清理**：Connector lease 是單一 operation 的 worker runtime lease、worker slot 與
   admission permit 的唯一釋放入口；清理順序必須是先停止／淘汰 faulted worker 並釋放 runtime lease，
   再以 finally 釋放 permit。所有 cleanup failure 必須繼續嘗試後續清理並彙總；不得 fire-and-forget。
6. **IPC 與資料邊界**：Worker IPC 維持有界、typed、versioned、length-prefixed、nonce-bound、
   deadline-bound 的 allowlist contract。IPC、log、metric、exception response 與 task artifacts 都不得
   含 CRM SDK object、任意 FetchXML、endpoint、connection string、credential、token、cookie、raw
   principal、browser session、product session 或完整敏感 payload。
7. **離線品質驗證**：實作前先建立可重現的 contract、fault-injection、generation replacement、
   process/pipe cleanup、permit accounting、profile isolation 與 soak baseline 測試。這些驗證不得連線
   D365 或讀取真實 secret。
8. **受控 CE 證據**：只有使用者另行書面授權時，才能對事先核准的一個 CE 8.2 profile 與一個 CE 9.1
   profile 執行 allowlisted、read-only operation。執行時只使用 deployment-owned secret provider；不得
   在 source、設定範例、log、test output 或 Trellis artifact 寫入或複製 credential、token、cookie、
   connection string 或其他 secret。

## 非目標與嚴格限制

- 本 Phase 1 不執行 `task.py start`；P6 必須維持 `planning`。
- 不修改產品程式、產品設定、feature flag、ChurchReport 流量、Operation Registry、ProductClient，
  也不移除 ToolUtility 或 CRM SDK dependency。
- 不執行 CE、WhoAmI、資料查詢、寫入、Action、Function、部署、IIS、SQL、DNS、ADFS、IFD 或 Web API
  真機操作。
- 不建立或啟動 P7.0、P7.1～P7.5；P7.0 可維持既有 planning 文件，但不是 P6 前置條件。
- 不執行 Gemini、Claude、外部 CCG、commit、archive、push 或建立 PR。

## 驗收條件

- [ ] `design.md` 將 Data8 與 Official Worker 放在同一 Router/Pool/Lease 選擇模型中，且明確保留
      Data8 為永久合法 ConnectorKind。
- [ ] `design.md` 定義 CE 8.2／9.1 compatibility matrix、profile/generation isolation、單一 admission
      owner、process/pipe/runtime/lease ownership 與 drain/dispose 順序。
- [ ] `design.md` 明確列出 fail-closed／no-fallback、secret/SDK/IPC 邊界、離線與真機 evidence 的差異。
- [ ] `implement.md` 提供按風險排序的實作、測試、rollback 與驗證順序，並把 CE read-only run 留在
      使用者另行核准之後。
- [ ] P6 task metadata 說明其高複雜度、高風險與 planning-only 邊界；task status 仍為 `planning`。
- [ ] 本輪 diff 僅限 Trellis task 文件與 metadata；不含產品程式、設定或流量變更。

## 尚待使用者決定的事項

1. **P6 activation**：是否核准完成文件後執行 `task.py start` 並進入離線實作。建議：先核准離線實作，
   不連帶授權 CE 真機操作。
2. **CE 真機 window**：P6 離線品質閘門通過後，是否提供一次獨立、read-only 的 CE 8.2 與 CE 9.1
   驗證授權，以及各自的已核准 ProfileAlias／執行時段／讀取範圍。建議：僅使用上述三個 allowlisted
   operation，先 health/connection，再執行一個有明確資料最小化條件的 fee read；不進行寫入或流量切換。
3. **P6 結案門檻**：建議維持「離線實作與品質檢查 + 明確核准的 CE 8.2/9.1 read-only evidence」兩者皆
   通過才結案；在此之前 P7 Parent 與 P7.0 一律不得啟動。
