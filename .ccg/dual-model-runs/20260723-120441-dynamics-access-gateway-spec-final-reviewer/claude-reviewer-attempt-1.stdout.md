已完整讀取四份規劃文件（prd.md、design.md、implement.md、SPEC 摘要文件）。這是一份規劃/架構文件審查，未涉及任何 production 程式碼修改。以下是審查結果。

## Review: Dynamics Access Gateway Architecture SPEC（規劃文件審查）

### Critical 🔴
無。四份文件在互斥選項比較、零容忍隔離模型、SDK 移除範圍認定上未發現會構成發布阻斷或違反使用者硬性要求的矛盾。

### Warning 🟡

- **design.md §5（第 118–149 行）／ 2026-07-23-dynamics-access-gateway-design.md「Architecture」表格**
  「操作註冊表（operation registry）具權威性…服務不暴露通用查詢端點」的宣告，與同一節列出的代表性端點 `GET /v1/organizations/{alias}/records/{entitySet}/{id}` 及 `POST /v1/organizations/{alias}/operations/{operationName}` 之間存在未解決的落差。這兩個端點直接把呼叫端提供的 `{entitySet}` / `{operationName}` 當作路徑參數，本質上仍是「呼叫端指定 Dynamics 概念名稱、伺服器端再檢查」的模式，並未明確說明這些值是否也如 `member.get`／`list.addMembers` 等 capability 名稱一樣，來自封閉的每別名（per-alias）註冊表白名單，還是允許任意合法 Dynamics schema 實體名稱字串。
  - 為何重要：§9.1 明確要求「呼叫端只能請求 capability/邏輯別名，而非實體 schema」，但若 `{entitySet}`/`{operationName}` 未被綁定到與其他 capability 相同的封閉註冊表，即成為變相的通用查詢/操作代理入口，與 PRD「不得暴露不受限的 CRM 代理」及本節自身宣告的「無通用查詢端點」直接衝突。
  - 建議修正：在 design.md §5 明確加一句，說明 `{entitySet}` 與 `{operationName}` 的合法值集合就是操作註冊表中列舉的別名（與 `member.get` 等 capability 同一張表），伺服器僅接受註冊表中存在的字面值，不接受任意 Dynamics logical/schema name。

- **design.md §7.2.2（第 335–352 行）／implement.md Phase 3 第 5 點（134–138 行）**
  ReplicaSlotLease 機制描述了「coordinator 無法連線時，既有已取得租約的 replica 只能以保守的固定本地配額繼續運作，直到緊急租約寬限期（grace period）到期」，但**未定義寬限期到期後、coordinator 仍未恢復時該 replica 的行為**。文字沒有明確指出到期後 replica 必須轉為 NotReady／停止接受新請求，還是可以無限期沿用寬限前的保守配額繼續服務。
  - 為何重要：這正好是 review 問題 3 所問的「stale runtime mutation / unsafe unbounded state」風險點——如果留白，實作者可能選擇「到期後照舊運行」，導致 coordinator 長時間中斷時，聚合並行量的硬性上限（`AggregateMaxInFlight`）事實上失去強制力，違反 §7.2.1「失敗時必須回退到保守分配，而非無限流量」的零容忍精神。
  - 建議修正：在 §7.2.2 明確加一條 fail-closed 規則，例如「寬限期屆滿且 coordinator 仍不可用時，該 replica 必須轉為 NotReady 並停止接受新請求，直到重新取得租約」。

### Info 🟢

- **design.md §6.1 JSON 範例（第 172–181 行）**
  `LocalMaxInFlight = floor(AggregateMaxInFlight / MaximumGatewayReplicas)` 公式在 `AggregateMaxInFlight < MaximumGatewayReplicas` 時會取整為 0，等同該 profile 完全無法服務任何請求，但規格未提及設定驗證階段（Phase 2 strict options binding）應拒絕此類配置。建議在 Phase 2 的選項驗證清單中明文加入「計算所得 `LocalMaxInFlight` 必須 ≥ 1，否則設定驗證失敗」。

- **implement.md Preconditions（第 25–27 行）／design.md §6.1**
  秘密提供者（secret provider）平台留給部署階段決定（"deployment secrets, Windows credential facilities/gMSA…或 enterprise secret store"）。這對 CE on-premises 全內網部署（無對外網際網路）而言是合理疑慮：若目標環境無法連線到雲端金鑰保管服務（如 Azure Key Vault），需要及早確認。此點目前已經以「未決基礎設施決策」形式被 implement.md 的 Preconditions 正確地遞延，不構成規格缺陷，僅建議 Phase 0 基線調查時把「Gateway 網路是否能連線到選定的秘密提供者」列為明確查核項。

- **design.md §7.2（Windows credentials 列）**
  NTLM/Negotiate 驗證通常建議搭配 `PreAuthenticate = true` 以避免每條連線的雙向 401 挑戰-回應延遲，這與 §10 訂出的積極延遲目標（Gateway 額外開銷 p95 < 5ms）相關，但由於連線層級（非請求層級）驗證且有連線池重用，實際影響有限，僅供實作階段參考，不需修改規格本身。

### 針對審查問題的具體回應

1. **Gateway 是否有充分理由、Library-only 與 transparent-proxy 是否有具體理由被拒絕？** 充分。design.md §2.2 的三選項比較表對「Library-only」（每產品各自持有憑證/連線池/token cache，重複風險隨規模放大）與「transparent proxy」（暴露任意 URL/查詢/header，授權不可判定）都給出具體、可驗證的拒絕理由，並非空泛主張。
2. **Profile-generation key 隔離是否足夠？** 除上述 Warning 中 `{entitySet}/{operationName}` 的呼叫端輸入邊界需要澄清外，`ProfileRuntimeKey`（profileId + configurationGeneration + apiVersion + normalized origin + authMode + secretVersionFingerprint）本身對 HTTP handler、憑證、token cache、metadata cache、重試/併發狀態、reload 生命週期的隔離覆蓋是充分的。
3. **是否留有跨 profile 路由、秘密外洩、呼叫端指定端點/header/profile escape、保留期外洩、runtime 就地變更、不安全自動重試的路徑？** 除上述兩個 Warning 外未發現其他路徑；cookies/auto-redirect 停用、reload 採 replace-and-drain 而非就地變更、非冪等寫入強制走 idempotency ledger 或直接失敗，均已妥善處理。
4. **CE 8.2/9.1 API 版本與驗證限制描述是否安全？** 安全。design.md §6.3 明確聲明 CE on-premises 不承諾 client-secret/certificate 支援，IFD 模式強制走「已驗證的非密碼服務工作負載授權」可行性關卡，未把 WS-Trust 當隱性後備。
5. **效能與高可用宣告是否有界、可測、與 Dynamics 服務保護相容？** 是。§10 的效能目標明確標註「需以真實伺服器基準驗證後才能訂定/調整」，聚合並行預算與 ReplicaSlotLease 機制直接對應服務保護限制。
6. **遷移範圍、no-SDK 檢查、測試/發布關卡是否足夠具體？** 足夠。design.md §12.1 列出約 200 個 SDK 相關來源檔案、ICrmClient 為 SDK-shaped 介面不可沿用、ToolUtilityFactory 為不相容的靜態單例等具體事實；implement.md 以 `rg` 掃描指令與分階段（Phase 0–6）強制關卡呈現，非空泛承諾。

### Summary
規劃文件整體品質高、內部邏輯自洽，對使用者提出的零容忍隔離、效能、no-SDK 邊界等硬性要求均有具體且可驗證的設計回應。發現的兩項 Warning（操作註冊表與具名路徑參數的一致性、ReplicaSlotLease 寬限期到期後行為未定義）建議在下一版規格中修正澄清，但不構成需要暫停規劃、要求使用者做決策的阻斷性問題——皆可用文字澄清解決，無需新的產品決策。建議：**批准規劃進入下一階段，附帶上述兩項 Warning 的文字修正要求**。

---
SESSION_ID: 198505f5-2ace-4475-9a3e-adf317374998
