# P7.0 Gateway Capability Inventory 與 Coverage Gate

## 目標與使用者價值

建立 ChurchReport 目前 D365 存取需求的可追溯能力盤點與離線 coverage gate 設計，讓後續 P7.1～P7.5 能以小範圍、強型別、可驗證且可回滾的 capability 逐步遷移，而不是把 ToolUtility 方法、CRM SDK 型別或任意 FetchXML 暴露成遠端 API。

本子任務只完成 P7 Parent 內部的規劃、現況盤點與 task-local 初步 inventory；不建立 Gateway operation、不切換網站流量，也不改動產品程式或設定。P7.0 不是 P6 的前置條件，必須在 P5 結案、P6 Router 接入與 CE 8.2／9.1 整合驗證完成後，才由 P7 Parent 啟動。

## 已確認事實

- Phase 0 權威矩陣仍有 70 筆 `normalizedCallSites`；其中 35 read、23 write、4 action、2 function、5 connection-runtime、1 metadata；54 筆為 `mapped-pending-evidence`，16 筆為 `temporary-legacy`。
- 70 筆對 CE 8.2 與 CE 9.1 的證據均為 `metadata-only`，所有 smoke evidence 均為 `not-started`；不得把 registry、unit test 或本機設定當成真機證據。
- `Package01OperationRegistry` 實際宣告 9 個 operation，僅對應 9 筆 Phase 0 rows。Data8 executor 目前只實作 `runtime.health.whoami`；官方 Worker 已有兩個 identity operation 與 `fee.dedication.retrieve.by.contact.date.range` 共 3 個 allowlisted operation，但 P6 尚未接入 Router。
- ProductClient 公開 6 個 Package01 fee/read 方法；ChurchReport 的 `Package01FeeReadsEnabled` 在 base 與 Development 設定均為 `false`，因此尚未有 Gateway consumer 啟用。
- P5 `dedicated-gateway-alignment` 已於 2026-08-05 完成驗收、提交並封存；P6 Official Worker child task 已建立且維持 `planning`，故不得把 P6 描述為已完成或啟動。
- ChurchReport 專案仍含 ToolUtility、Dataverse 與 CRM SDK 的 production dependency；這是 P7.5 的移除 gate，不是本輪要移除的內容。

## 範圍

1. 以 `.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json` 為 70-row 來源，保存 task-local、可追溯的初步 capability grouping。
2. 定義每筆 call site 最終 coverage matrix 必備欄位、operation 命名與 owner 規則，以及 registry、executor、consumer、真機 evidence 四種獨立狀態。
3. 判定 Data8、Official Worker、CE 8.2／9.1 證據與 P6 的相依關係，並設計完全離線、確定性的 coverage validator。
4. 為 P7.1 read、P7.2 write/action/function、P7.3 special resource、P7.4 cutover、P7.5 removal 設定可獨立驗收與回滾邊界。

## 非目標

- P6 完成前，P7.0 維持 `planning`；不執行 `task.py start`、不建立 P7.1～P7.5 child task，也不執行 P5 或 P6。
- 不修改 `.cs`、`.cshtml`、`.csproj`、產品設定、Operation Registry、Data8／Official Worker executor 或 ProductClient。
- 不啟用 feature flag、不對 CE 8.2／9.1 發出呼叫、不執行 read/write/action/function 或資料遷移。
- 不提交、archive、push 或建立 PR；本輪結束時保留規劃供使用者審閱。

## 依賴與順序

`P5 Dedicated Gateway 驗收與結案` → `P6 Official Worker 接入 Router 與 CE 8.2/9.1 受控跨模式真機驗證` → `P7 Parent ChurchReport 完全 Gateway 化` → `P7.0 inventory/coverage gate` → `P7.1～P7.3 capability slices` → `P7.4 per-capability cutover` → `P7.5 ToolUtility/CRM SDK removal gate`。

P6 是所有 P7 工作的拓樸與實證前置條件：即使某一 capability 選擇永久支援的 Data8 connector，也必須先有已接入 Router 的 connector 選擇、profile isolation 與 CE evidence gate，才可啟用 P7 Parent 或 consumer。P7.0 可以保留規劃文件，但不得在 P6 完成前啟動、執行或被宣稱為 P6 gate。

## 功能與品質需求

1. 初步 inventory 必須完整涵蓋 70 個 source call-site ID，並以 source matrix hash 固定其證據版本。
2. 最終 row schema 必須包含 call site、來源 symbol、現有行為、業務 use case、capability family、operation type/ID、ProductClient/DTO owner、四種 coverage 狀態、connector/CE 支援、consumer gate、authorization/profile/workload、rollout/rollback、temporary legacy、removal gate 與資源生命週期風險。
3. Capability 必須按業務 use case 分組為 platform-shared、跨產品 domain 或 `churchreport.*`；禁止 generic CRUD、任意 entity、任意 QueryBase 或任意 FetchXML capability。
4. 每一個未來 Gateway request 的 identity、profile、connector、credential、endpoint、token 與 organization 必須由伺服器／部署 profile 擁有；產品 request 不得控制或保存它們。
5. 跨 request／user／profile／organization 的 mutable state、SDK client、connection、lease、permit、stream、paging cookie、timer、task、handle 與 cancellation registration 都必須有唯一且有限的 owner、drain/dispose 路徑與驗證證據。
6. Coverage validator 必須完全離線，不讀取 D365、credential、token、cookie、connection string 或 secret，並對相同輸入產生相同結果。

## 驗收條件

- [ ] task-local JSON 初步 inventory 的 source hash、70 個 ID 與 12 個初步 capability group 可被離線檢查。
- [ ] `design.md` 清楚區分 Registry declared、Executor implemented、Consumer enabled、Real CE evidence 四種狀態。
- [ ] `design.md` 記錄 9/1/3/0 的現況覆蓋數（registry/Data8/official-worker/consumer-or-real-CE）。
- [ ] 每個初步 group 都有 P6 判定、P7 child-task 落點與 rollback/removal 原則。
- [ ] validator 規格可阻擋未分類 row、缺 owner/DTO/connector/CE 狀態、未授權 generic CRUD/FetchXML、未受 owner 管理的 temporary legacy，以及 P7.5 尚存 production SDK dependency。
- [ ] coverage 數字由 source-derived manifest 取得，並明確分開 Official Worker protocol/adapter allowlist、Official Worker Router integration、consumer enablement 與 CE evidence；不得以手寫數字混淆這些層次。
- [ ] `implement.md` 定義先驗證、後建立 machine-readable matrix/validator 的順序、檔案界線、驗證命令、UTF-8/CRLF、`git diff --check`、rollback 與使用者審閱 gate。
- [ ] 本輪 diff 不含產品程式、專案檔或產品設定變更；不啟動 implementation、commit、archive 或 push。
