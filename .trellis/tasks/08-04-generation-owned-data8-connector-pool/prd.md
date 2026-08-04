# Generation-owned Data8 connector pool

## Goal

完成 P3：建立 SDK-free 的 Data8 Connector Pool、Lease 與 Router，讓每個 Profile Generation 擁有明確且可回收的連線資源；所有借出、歸還、故障淘汰、容量准入與世代排空都可獨立測試。

## Scope

- 新增 net10 `SpeechMessage.Dynamics.Connectors.Data8` 專案。
- 在 Abstractions 定義不引用 CRM SDK 的 `IConnectorClient`、`IConnectorLease`、`IConnectorPool` 與 `IConnectorRouter`。
- Data8 Pool 以 `(ProfileAlias, GenerationId)` 作為不可變隔離鍵；不同組織、不同 Connector 或不同世代不得共用可變連線狀態。
- Data8 Pool 透過既有 `IOrganizationAdmissionManager` 取得組織容量 Permit，不建立第二套容量預算。
- 健康 Lease 歸還原世代 Pool；故障、取消、逾時或 Dispose 後 Lease 必須淘汰並釋放其 Client。
- Pool Drain 必須先拒絕新 Lease，等待既有 Lease 歸還，最後釋放閒置與故障資源；所有非同步清理必須可重入且 deterministic。
- Router 僅依不可變 `ResolvedProfile.ConnectorKind` 路由；Request 不得指定 Connector、endpoint、credential 或 OrganizationId。
- 保留 `ToolUtility` legacy pool 與 `Package01FeeReadsEnabled=false`。

## Non-goals

- 不實作 P4 Embedded、P5 DedicatedGateway、P6 Official Worker。
- 不修改 Web API、IFD、D365APP01 或真機 WhoAmI 診斷路線。
- 不移除 Data8，不重構既有 ToolUtility legacy pool。
- 不在 Pool 契約公開 `Microsoft.Xrm.Sdk`、`IOrganizationService`、WCF channel、credential 或 token 型別。

## Required lifecycle invariants

1. 每個 Pool、Lease、Client、Permit 都只有一個明確 owner。
2. `DisposeAsync` 必須具 idempotency；建構失敗必須 rollback 已建立資源。
3. Lease 的 `DisposeAsync` 必須 exactly-once；任何例外仍需在 finally 釋放 Permit。
4. Drain 時不允許新借出；既有 Lease 完成後才可釋放 idle resources。
5. Profile、Organization、credential、token、session 與 request mutable state 不得進入共享 Pool key 或 Client。

## Acceptance criteria

- [ ] `SpeechMessage.Dynamics.Connectors.Data8.csproj` 為 net10，僅依賴 Abstractions、ControlPlane 與 Data8 client；不被產品直接參考。
- [ ] 契約編譯期不含 CRM SDK 型別；Router 對不相容 Connector fail closed，且不 fallback。
- [ ] 測試先寫且先觀察 RED，再以最小實作轉 GREEN。
- [ ] 測試覆蓋：健康歸還、故障淘汰、取消／逾時釋放 Permit、Generation drain、跨 Profile 隔離、同 Organization 容量共用、Dispose idempotency、soak 無單調資源成長。
- [ ] 既有 P1／P2 測試與 ToolUtility legacy 行為不退步。
- [ ] 所有新增或修改 `.cs` 為 UTF-8 without BOM、CRLF、最終 CRLF，具完整繁體中文 XML 與生命週期註解。
- [ ] P3 測試、完整 Dynamics 測試與 Release build 結果被記錄；任何既有不穩定測試不得被放寬或忽略。
