# P5 Dedicated Gateway 對齊 PRD

## 目標與使用者價值

讓 ChurchReport 可在 Visual Studio 2026 以多啟動專案按 F5 啟動自身與一個專屬的 Gateway，並在 `DynamicsAccess:ConnectionMode=DedicatedGateway` 時，透過 `https://localhost:7244/` 使用固定 `ProfileAlias` 執行 Dynamics 已核准操作。

Dedicated Gateway 是與單一產品一起部署、但位於獨立進程的永久模式；它不是 Embedded 的替代品，也不是 Central Gateway 的前置條件。

## 已確認事實

- P3 已交付 `(ProfileAlias, GenerationId)` 隔離的 Data8 pool、router、lease 與 admission 契約。
- P4 已交付 ChurchReport 的 Embedded Data8 路徑，且其安全順序為 RequestGuard → ProfileResolver → Organization Admission → Router → Data8 pool。
- 現有 `GatewayDynamicsOperationExecutor`、Gateway HTTPS API、localhost launch profile、Negotiate 驗證和 product-side `DedicatedGateway` 選項已存在。
- 現有 Gateway Development 設定仍以 Official Worker 與 SQL host coordinator 為主；這不符合單一產品 Dedicated Data8 的 P5 目標。
- `Data8` 是永久合法的 `ConnectorKind`，CE 8.2 與 CE 9.1 均可由 profile 決定使用它；請求不得選擇 connector、endpoint、credential 或 Organization ID。

## 功能需求

1. Dedicated Gateway 必須在單一 ASP.NET Core 進程內，以 HTTPS loopback 接收 ChurchReport 的 Gateway contract；產品僅提供 `ConnectionMode`、`ProfileAlias` 與 Gateway endpoint。
2. Gateway 端必須重用與 Embedded 相同的 ProfileResolver、Organization Admission、Data8 router、Data8 generation-owned pool 和 Data8 executor 契約；不得複製或分叉 pool/lease 的資源釋放規則。
3. Dedicated 模式必須使用單進程 In-Memory coordinator；不得建立、查詢、設定或依賴 SQL coordinator、資料庫、Registry、IIS、DNS、ADFS、IFD、CRMWeb 或 Web API。
4. Dedicated Gateway 必須以 server-owned loopback HTTPS 與 Windows Negotiate principal 做授權；不得接受 client header、request body 或 query 中的 identity、organization、connector、endpoint、credential、token 或 FetchXML 作為路由依據。
5. Gateway 的每個 request 必須經過 Guard、固定 workload/profile authorization、ProfileResolver、admission、router 與 lease；任何失敗必須 fail closed，且不自動改用 Embedded、Central、Official Worker 或其他 connector。
6. ChurchReport Development 設定必須可切換至 `DedicatedGateway` 與 `https://localhost:7244/`；Embedded 設定和其不讀取 `Gateway.Endpoint` 的契約必須保持有效。
7. Visual Studio 開發文件必須說明一次性設定「Multiple startup projects」：Gateway 先啟動、ChurchReport 後啟動；不得以產品在執行時自行啟動/終止 Gateway 進程取代此設定。

## 非目標

- 不執行外部 CE、WhoAmI、部署、憑證、網路或效能量測；這些仍是 P6 後的一次跨模式整合閘門。
- 不實作 Central Gateway、多節點協調、Official Worker、SQL、Web API、IFD、CRMWeb、IIS、DNS 或 ADFS 工作。
- 不移除 Embedded、Data8、既有 ProductClient HTTP contract 或 P3/P4 成果。
- 不把組織、endpoint、connector 或 credential 暴露到 ChurchReport 公開產品設定或請求契約。

## 可驗收條件

- [ ] Dedicated Gateway Data8 runtime 能由有效的 deployment-owned profile/catalog 建立，且與 Embedded 使用同一個 shared runtime/pool lifecycle 實作。
- [ ] Dedicated runtime 只使用 In-Memory admission coordinator；無 SQL coordinator 或資料庫依賴。
- [ ] Gateway 以 `RequestOrigin.DedicatedGateway` 執行 RequestGuard，並保持既有 authorization、request body limit、no-store 和 HTTPS-loopback fail-closed 行為。
- [ ] 產品端 `DedicatedGateway` 只接受合法 absolute HTTPS endpoint，且 profile alias 不可由 request 覆寫。
- [ ] Gateway 與 ChurchReport 的組態皆可設定為 Development localhost Dedicated Gateway，且不包含真實 credential、token、cookie 或外部 endpoint secret。
- [ ] 自動化測試覆蓋 startup fail-closed、Data8 runtime lease/permit/client cleanup、Dedicated origin、HTTP contract、配置隔離與 Gateway/ChurchReport host disposal。
- [ ] Focused tests、完整 Dynamics/ChurchReport tests、Release build、UTF-8 無 BOM／CRLF／final CRLF 位元組檢查與 `git diff --check` 全部通過。

## 外部真機交接

P5 的離線綠燈不表示 CE 真機成功。P6 完成後，才在同一受控環境以 legacy、Embedded、Dedicated 執行相同 read workload，驗證結果一致性、p50/p95/p99、至少 200 次 borrow/use/return、故障淘汰以及 permit/client/timer/task/handle/session 回到基線。
