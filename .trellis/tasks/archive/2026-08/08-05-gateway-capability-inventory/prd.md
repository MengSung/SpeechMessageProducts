# P7.0 Gateway Capability Inventory 與 Coverage Gate

## 目標與使用者價值

建立 ChurchReport 目前 D365 存取需求的可追溯能力盤點與離線 coverage gate 設計，讓後續 P7.1～P7.5 能以小範圍、強型別、可驗證且可回滾的 capability 逐步遷移，而不是把 ToolUtility 方法、CRM SDK 型別或任意 FetchXML 暴露成遠端 API。

本子任務目前只保存 P7 Parent 內部的規劃、現況盤點與 task-local 初步 inventory；不建立 Gateway operation、不切換網站流量，也不改動產品程式或設定。P7.0 不是 P6 的前置條件，必須在 P5 結案、P6 Official Worker Router 擴充點完成並正式封存後，才由 P7 Parent 啟動；不再等待 Official Worker 真機 READY。Lenovo Legion 是後續 P7.0～P7.5 的本機執行與 evidence host；雲端部署屬 P8。

## 已確認事實

- Phase 0 權威矩陣仍有 70 筆 `normalizedCallSites`；其中 35 read、23 write、4 action、2 function、5 connection-runtime、1 metadata；54 筆為 `mapped-pending-evidence`，16 筆為 `temporary-legacy`。
- 70 筆對 CE 8.2 與 CE 9.1 的證據均為 `metadata-only`，所有 smoke evidence 均為 `not-started`；不得把 registry、unit test 或本機設定當成真機證據。
- `Package01OperationRegistry` 實際宣告 9 個 operation，僅對應 9 筆 Phase 0 rows。Data8 executor 目前只實作 `runtime.health.whoami`；官方 Worker 已有兩個 identity operation 與 `fee.dedication.retrieve.by.contact.date.range` 共 3 個 allowlisted operation。P6.1 已完成 Official Worker Router／Pool／Lease 離線接入；Official Worker 尚未取得 CE 8.2／9.1 real evidence，必須標為 `evidence-pending`。protocol allowlist、Router implementation、consumer selection 與真機 evidence 必須分開記錄。
- ProductClient 公開 6 個 Package01 fee/read 方法；ChurchReport 的 `Package01FeeReadsEnabled` 在 base 與 Development 設定均為 `false`，因此尚未有 Gateway consumer 啟用。
- P5 `dedicated-gateway-alignment` 已於 2026-08-05 完成驗收、提交並封存；P6 Official Worker child task 已於 2026-08-07 完成 P6.1 離線 quality／spec／commit／archive，Official Worker live compatibility 維持 `evidence-pending`。P7.0 已在此封存後啟動，不等待 Official Worker live evidence。
- ChurchReport 專案仍含 ToolUtility、Dataverse 與 CRM SDK 的 production dependency；這是 P7.5 的移除 gate，不是本輪要移除的內容。
- 使用者確認 `sunnyvalechback` 是與正式系統分離的 CE 9.1 公司研發 Organization，可建立 test member 而不影響正式資料。P7.2 可將它作為 CE 9.1 test-owned fixture environment；每個 operation family 仍需唯一 fixture owner 與 cleanup/reconciliation。

## 範圍

1. 以 `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json` 為 70-row 來源，保存 task-local、可追溯的初步 capability grouping。
2. 定義每筆 call site 最終 coverage matrix 必備欄位、operation 命名與 owner 規則，以及 registry、executor、consumer、真機 evidence 四種獨立狀態。
3. 判定 Data8、Official Worker、CE 8.2／9.1 證據與 P6 的相依關係，並設計完全離線、確定性的 coverage validator。
4. 為 P7.1 read、P7.2 write/action/function、P7.3 special resource、P7.4 cutover、P7.5 removal 設定可獨立驗收與回滾邊界。
5. 定義 P7.5 的輸出如何成為 P8.0 cloud readiness 的 immutable deployment input；P7.0 不建立或啟動 P8。

## 非目標

- P7.0 已在 P6 正式封存後進入 `in_progress`；本子任務不建立或啟動 P7.1～P7.5 child task，也不重跑 P5 或 P6。
- 不修改 `.cs`、`.cshtml`、`.csproj`、產品設定、Operation Registry、Data8／Official Worker executor 或 ProductClient。
- 不啟用 feature flag、不對 CE 8.2／9.1 發出呼叫、不執行 read/write/action/function 或資料遷移。
- 不提交、archive、push 或建立 PR；本輪結束時保留規劃供使用者審閱。
- 不建立雲端 host、service identity、TLS、DNS、monitoring 或 Central Gateway deployment；這些是 P8.0～P8.4 的獨立範圍。

## 依賴與順序

`P5 Dedicated Gateway 驗收與結案` → `P6 Official Worker Router 擴充點離線完成並封存（live evidence pending）` → `P7 Parent ChurchReport 完全 Gateway 化` → `P7.0 inventory/coverage gate` → `P7.1 read` → `P7.2 write/action/function` → `P7.3 special resource` → `P7.4 Embedded+Data8／DedicatedGateway+Data8 per-capability cutover` → `P7.5 ToolUtility/CRM SDK removal gate` → `獨立 P8.0～P8.4 CentralGateway+Data8 ChurchReport 雲端部署`。

P6 是所有 P7 工作的拓樸前置條件：必須先有已接入 Router 的 connector 選擇、profile isolation、
admission 與 lifecycle contract。P7 的實證則按 capability、ConnectorKind 與 CE version 在各 child
取得，不要求未被 ChurchReport deployment 選用的 Official Worker 先通過真機證據。P7.0 可以保留規劃
文件；P6 已正式封存，啟動後必須讓每個 capability 明確標示 `Data8-only`、
`Official-worker-required`、`both-required`、`unsupported` 或 `evidence-pending`。

## 功能與品質需求

1. 初步 inventory 必須完整涵蓋 70 個 source call-site ID，並以 source matrix hash 固定其證據版本。
2. 最終 row schema 必須包含 call site、來源 symbol、現有行為、業務 use case、capability family、operation type/ID、ProductClient/DTO owner、四種 coverage 狀態、connector/CE 支援、consumer gate、authorization/profile/workload、rollout/rollback、temporary legacy、removal gate 與資源生命週期風險。
3. Capability 必須按業務 use case 分組為 platform-shared、跨產品 domain 或 `churchreport.*`；禁止 generic CRUD、任意 entity、任意 QueryBase 或任意 FetchXML capability。
4. 每一個未來 Gateway request 的 identity、profile、connector、credential、endpoint、token 與 organization 必須由伺服器／部署 profile 擁有；產品 request 不得控制或保存它們。
5. 跨 request／user／profile／organization 的 mutable state、SDK client、connection、lease、permit、stream、paging cookie、timer、task、handle 與 cancellation registration 都必須有唯一且有限的 owner、drain/dispose 路徑與驗證證據。
6. Coverage validator 必須完全離線，不讀取 D365、credential、token、cookie、connection string 或 secret，並對相同輸入產生相同結果。
7. P7.0 support matrix 必須逐 capability 決定 CE 8.2／9.1 的 `required`、`unsupported` 或 `evidence-pending`；ChurchReport 第一產品的 CE 9.1 寫入 evidence 不得因沒有無條件要求的 CE 8.2 write sandbox 而被阻塞。只有標為 CE 8.2 `required` 的 capability 才需相應安全 fixture/evidence。
8. P7 的 Lenovo runtime 必須同時保留可設定的 `Embedded + Data8` 與
   `DedicatedGateway + Data8`；ConnectionMode 與 ConnectorKind 不得混為二選一。P8 handoff 必須要求
   Central Gateway composition 保留 Data8，第一個 ChurchReport cloud deployment 採
   `CentralGateway + Data8`，且沒有 request-time connector fallback。

## 驗收條件

- [x] task-local JSON 初步 inventory 的 source hash、70 個 ID 與 12 個初步 capability group 可被離線檢查。
- [x] `design.md` 清楚區分 Registry declared、Executor implemented、Consumer enabled、Real CE evidence 四種狀態。
- [x] `design.md` 記錄 9/1/3/0 的現況覆蓋數（registry/Data8/official-worker/consumer-or-real-CE）。
- [x] 每個初步 group 都有 P6 判定、P7 child-task 落點與 rollback/removal 原則。
- [x] validator 規格可阻擋未分類 row、缺 owner/DTO/connector/CE 狀態、未授權 generic CRUD/FetchXML、未受 owner 管理的 temporary legacy，以及 P7.5 尚存 production SDK dependency。
- [x] coverage 數字由 source-derived manifest 取得，並明確分開 Official Worker protocol/adapter allowlist、Official Worker Router integration、consumer enablement 與 CE evidence；不得以手寫數字混淆這些層次。
- [x] `implement.md` 定義先驗證、後建立 machine-readable matrix/validator 的順序、檔案界線、驗證命令、UTF-8/CRLF、`git diff --check`、rollback 與使用者審閱 gate。
- [ ] P7.0 的 Phase 2 diff 只包含 task-local inventory、deterministic validator、validator tests、
      evidence／reference-scan artifacts 與 task metadata；不修改產品 runtime、專案引用、feature flag
      或 ChurchReport 流量。品質 gate 通過後可依整合 Goal 建立 task-owned commit 並 archive；不得 push。
- [ ] P7.5 handoff 明確要求 ChurchReport production zero-reference 與完整本機 evidence，P8 只接收已封存 artifact，不把雲端部署工作倒灌進 P7.0。
