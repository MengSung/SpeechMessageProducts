# P6 Official Worker Router 接入與 CE 整合驗證 PRD

> 狀態：Phase 2 `in_progress`。P6.1 離線品質閘門已通過；目前停在 P6.2 Lenovo Legion deployment readiness／CE evidence。

## 目標與使用者價值

把既有、版本隔離的 Official CRM 8.2／9.1 Worker 資產接入目前已由 Data8 使用的
`IConnectorRouter`、`IConnectorPool` 與 `IConnectorLease` 邊界。如此 Connector 的選擇固定由
deployment-owned profile 決定，產品端與請求端都不能選擇 SDK、CE 版本、endpoint、credential 或
fallback；同時保留 Data8 為永久合法的 ConnectorKind。

P6 的可交付結果是「Official Worker 已成為可由 Router 選取、具世代與 lease 生命週期的第二種
Connector」，並以離線測試證明隔離、drain、dispose 與無洩漏契約。在取得額外明確授權後，才以
受控、read-only 的 CE 8.2／9.1 operation matrix 取得真機證據；離線測試或部署設定不得被宣稱為
CE 相容證據。

Lenovo Legion 是 P6 的 authoritative local development、Gateway／Worker execution 與 evidence host。P6 不部署雲端，也不把 Lenovo 的 Windows identity、credential target 或 profile overlay 當成未來雲端設定；雲端 host/service identity/TLS/monitoring/rollback 由 P8 重新建立並驗證。

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
- P6.1 Router／Pool／Lease、離線 lifecycle 與正式 quality check 已通過；task 已是 `in_progress`，不得再寫成只完成 planning。
- Lenovo Legion 上的 `-InventoryOnly` readiness probe 已在目標 Windows identity 下成功執行；CE 8.2 與 CE 9.1 profile 都只回報 `profile-input-required`。這證明 probe、manifest 與 worker artifact inventory 可用，但尚未證明 profile、secret 或 CE 連線可用。
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
8. **受控 CE 證據**：只有使用者另行書面授權時，才能對事先核准的一個 CE 8.2 profile 與一個 CE 9.1
   profile 執行 allowlisted、read-only operation。執行時只使用 deployment-owned secret provider；不得
   在 source、設定範例、log、test output 或 Trellis artifact 寫入或複製 credential、token、cookie、
    connection string 或其他 secret。
9. **本機部署邊界**：P6.2 profile overlay、worker-local credential target 與執行 identity 都屬 Lenovo Legion 開發環境。任何未來雲端值不得從本機 artifact 直接複製；P8 必須以雲端 deployment owner 重新解析與核准。
10. **前置 G0 gate**：P6／P7 長跑前必須先通過 scoped Git/text baseline、P6 readiness=`go` 與 P7.2 safe-write evidence authority。若任一項缺失，先產生 consolidated PowerShell/operator handoff，不啟動 P7 或宣稱可無人值守完成。
11. **P6／P7 分工**：P6 證明 ConnectorKind／CE version routing、Official Worker process/IPC、Pool/Lease/admission、credential boundary 與 cleanup；P6 不實作或驗證 ChurchReport 的 write/action/function 業務語意。後者由 P7.2 使用 test-owned fixture 驗證。

## 非目標與嚴格限制

- 本次路線文件重校不重新執行 `task.py start`；既有 P6 保持 `in_progress` 並從 P6.2 續作。
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
- [ ] P6 task metadata 說明其高複雜度、高風險、P6.1 已完成與 P6.2 local-readiness 邊界；task status 保持 `in_progress` 直到 CE 8.2／9.1 evidence 與結案 gate 通過。
- [ ] 本輪 diff 僅限 Trellis task 文件與 metadata；不含產品程式、設定或流量變更。

## 尚待使用者決定的事項

1. **Local profile input**：由 deployment owner 提供 CE 8.2／9.1 各自的 ProfileAlias、CE version、ConnectorKind、ServiceUri／Organization mapping 與 Windows Credential Manager target；不得把密碼貼入 task 文件或命令列。
2. **CE 真機 window**：P6 離線品質閘門已通過後，是否提供一次獨立、read-only 的 CE 8.2 與 CE 9.1
   驗證授權，以及各自的已核准 ProfileAlias／執行時段／讀取範圍。建議：僅使用上述三個 allowlisted
   operation，先 health/connection，再執行一個有明確資料最小化條件的 fee read；不進行寫入或流量切換。
3. **P6 結案門檻**：建議維持「離線實作與品質檢查 + 明確核准的 CE 8.2/9.1 read-only evidence」兩者皆
   通過才結案；在此之前 P7 Parent 與 P7.0 一律不得啟動。
4. **整合 Goal**：若使用者採用 P6／P7 單一 `/goal` 提示詞，該提示詞可同時構成 P6.2 read-only CE window 與後續 P7 activation 的預先授權；仍不得繞過缺少 profile／secret／安全 fixture 的 No-Go。
