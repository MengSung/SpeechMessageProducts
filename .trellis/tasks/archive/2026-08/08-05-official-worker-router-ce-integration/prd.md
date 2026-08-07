# P6 Official Worker Router 接入與 CE 整合驗證 PRD

> 狀態：Phase 2 `in_progress`。P6.1 離線品質閘門已通過；依
> [2026-08-07 範圍重校](./scope-rebaseline-2026-08-07.md)，P6.1 構成原始 P6
> 擴充點交付，P6.2 Official Worker 真機相容性改列非阻塞的已知限制。

## 目標與使用者價值

把既有、版本隔離的 Official CRM 8.2／9.1 Worker 資產接入目前已由 Data8 使用的
`IConnectorRouter`、`IConnectorPool` 與 `IConnectorLease` 邊界。如此 Connector 的選擇固定由
deployment-owned profile 決定，產品端與請求端都不能選擇 SDK、CE 版本、endpoint、credential 或
fallback；同時保留 Data8 為永久合法的 ConnectorKind。

P6 的可交付結果是「Official Worker 已成為可由 Router 選取、具世代與 lease 生命週期的第二種
Connector」，並以離線測試證明隔離、drain、dispose 與無洩漏契約。這個原始交付由 P6.1 完成。
Official Worker 的真實 CE 8.2／9.1 相容性必須保留為獨立 evidence 狀態；離線測試或部署設定不得被
宣稱為 CE 相容證據，也不再阻塞以 Data8 完成 ChurchReport 本機遷移。

Lenovo Legion 是 P6 的 authoritative local development、Gateway／Worker execution 與 evidence host。P6 不部署雲端，也不把 Lenovo 的 Windows identity、credential target 或 profile overlay 當成未來雲端設定；雲端 host/service identity/TLS/monitoring/rollback 由 P8 重新建立並驗證。

P6 的必要驗收里程碑是 **P6.1 離線 Router 接入**：完成程式、離線 lifecycle／soak 證據與品質閘門。
後續曾加入的 **P6.2 CE read-only 整合驗證**已留下 readiness 與啟動診斷資產，但它是未完成的
Official Worker live-compatibility follow-up，不再屬 P6 結案或 P7.0 啟動的必要條件。若未來部署選擇
Official Worker，必須以獨立 task 恢復 READY 與受控真機證據；現況不得冒充成功。

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
- P6.1 Router／Pool／Lease、離線 lifecycle 與正式 quality check 已通過；task 已是 `in_progress`，不得再寫成只完成 planning。
- Lenovo Legion 上的早期 `-InventoryOnly` readiness probe 曾對 CE 8.2 與 CE 9.1 回報
  `profile-input-required`；後續 operator handoff 已完成非敏感 profile input、Credential
  Manager reference 與 offline identity-chain/readiness 驗證，現況 readiness=`go`。兩者都只
  證明部署材料與本機解析鏈可用，**不**證明 Official Worker 已通過 READY 或 CE operation。
- 使用者已確認 Lenovo 可連 CE 8.2／9.1 且驗證形態為 IFD。本機實測 identity 為 `LENOVO-LEGION\Administrator`、`AuthenticationType=CloudAP`、`PartOfDomain=false`；因此本機 IFD profile 必須使用大小寫敏感的 `Ifd` 與 `WindowsCredentialReference`＋HTTPS `homeRealm`，不得嘗試只允許 Active Directory 的 `HostIdentity`。
- Windows Credential Manager 是 per-user store；credential target 必須由實際執行 Worker 的同一 Windows user 建立。Organization／IFD facts 與 credential target 需要 operator 協助；Worker 絕對路徑可由 manifest 推導，`worker-profile.xml`／Gateway overlay 由部署工具產生，不要求使用者手工建立。
- 使用者確認 `sunnyvalechback` 是與正式系統分離的 CE 9.1 公司研發 Organization，可建立測試會員而不影響正式資料。P6 只用它取得 Data8／Official Worker 的 allowlisted read-only identity、connection 與 resource evidence；測試會員的業務寫入驗證保留給 P7.2。

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
8. **Official Worker 真機證據狀態**：P6 結案必須明確記錄 Official Worker 對 CE 8.2／9.1 尚未通過
   READY／read-only operation，不得把 readiness=`go`、互動式瀏覽器登入或離線 harness 冒充真機相容。
   未來若 deployment 明確選用 Official Worker，另立 task 後仍只可對核准 profile 執行 allowlisted、
   read-only operation，並只使用 deployment-owned secret provider；不得在 source、設定範例、log、
   test output 或 Trellis artifact 寫入或複製 credential、token、cookie、connection string 或其他 secret。
9. **本機部署邊界**：P6.2 profile overlay、worker-local credential target 與執行 identity 都屬 Lenovo Legion 開發環境。任何未來雲端值不得從本機 artifact 直接複製；P8 必須以雲端 deployment owner 重新解析與核准。
10. **G0 已完成的部署事實**：scoped Git/text baseline 與 P6 readiness=`go` 已取得，證明本機 manifest、
    profile shape 與 Credential Manager reference 可供後續部署驗證使用；它不等於 CE 相容成功。
    `sunnyvalechback` 的 CE 9.1 環境級 test-member 可行性保留給 P7.0／P7.2，但不要求在 P7.0 matrix
    產生前猜測所有 operation-family fixture，也不把該事實視為任意寫入授權。
11. **P6／P7 分工**：P6 證明 ConnectorKind／CE version routing、Official Worker process/IPC、Pool/Lease/admission、credential boundary 與 cleanup；P6 不實作或驗證 ChurchReport 的 write/action/function 業務語意。後者由 P7.2 使用 test-owned fixture 驗證。

## 本輪文件重校限制與未來執行邊界

以下限制只描述 2026-08-06 產生、校正與驗證 P6／P7 整合計畫的文件工作，
不是未來使用者提交整合 `/goal` 後的 P6 Phase 2 永久禁令。整合 Goal 啟動後，
以 `docs/superpowers/plans/2026-08-06-p6-p7-integrated-execution.md` 的明確授權、
本 PRD 的安全契約及當前 Trellis gate 共同決定可執行範圍；任何 profile、secret、
安全 fixture 或真機 evidence 缺口仍必須 fail closed。

- 本次路線文件重校不重新執行 `task.py start`；既有 P6 保持 `in_progress`，直到完成重校後的
  quality／spec-update／commit／archive 閘門，不再從 P6.2 真機診斷續作。
- 不修改產品程式、產品設定、feature flag、ChurchReport 流量、Operation Registry、ProductClient，
  也不移除 ToolUtility 或 CRM SDK dependency。
- 不執行 CE、WhoAmI、資料查詢、寫入、Action、Function、部署、IIS、SQL、DNS、ADFS、IFD 或 Web API
  真機操作。
- 不建立或啟動 P7.0、P7.1～P7.5；P7.0 可維持既有 planning 文件，但不是 P6 前置條件。
- 不部署或切換雲端 Central Gateway；該工作屬 P8.0～P8.4。
- 不執行 Gemini、Claude、外部 CCG、commit、archive、push 或建立 PR。

## 驗收條件

- [ ] `design.md` 將 Data8 與 Official Worker 放在同一 Router/Pool/Lease 選擇模型中，且明確保留
      Data8 為永久合法 ConnectorKind。
- [ ] `design.md` 定義 CE 8.2／9.1 compatibility matrix、profile/generation isolation、單一 admission
      owner、process/pipe/runtime/lease ownership 與 drain/dispose 順序。
- [ ] `design.md` 明確列出 fail-closed／no-fallback、secret/SDK/IPC 邊界、離線與真機 evidence 的差異。
- [ ] `implement.md` 提供按風險排序的實作、測試、rollback 與驗證順序，並把 CE read-only run 留在
      使用者另行核准之後。
- [ ] P6 task metadata 說明其高複雜度、高風險、P6.1 已完成，以及 P6.2 readiness=`go` 但
      Official Worker live compatibility 未驗證；task status 保持 `in_progress` 到重校後的
      quality／spec-update／commit／archive 閘門通過。
- [ ] 本輪文件重校 diff 僅限 Trellis task 文件與 metadata；不含產品程式、設定或流量變更。
      這項驗收只證明規劃基線乾淨，不禁止後續整合 Goal 依 gate 執行 P6 Phase 2、
      task-owned commit 與 archive。

## 尚待使用者決定的事項

1. **P6 結案**：沒有剩餘 operator input。先重跑 P6 離線品質與文件一致性 gate；通過後如實記錄
   Official Worker live compatibility 未驗證，完成 task-owned commit／archive。
2. **P7 本機模式**：P7 必須讓 ChurchReport 可由設定選取 `Embedded + Data8` 或
   `DedicatedGateway + Data8`。Dedicated Gateway 與 Data8 是不同維度，不得被文件或程式寫成二選一。
3. **未來 Official Worker live task**：只有 deployment owner 明確選用 Official Worker 時才另立 task，
   以既有本機 profile/readiness 資產續做 READY 與 allowlisted read-only evidence；不得倒退重開 P6，
   也不得因該 task 未建立而阻塞 Data8 主線。
