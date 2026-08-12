# P7 尚餘能力重新基準化 — 架構分析報告

（依 `.trellis/tasks/08-12-p7-remaining-work-rebaseline`、`.ccg/tasks/p7-remaining-work-rebaseline` 及封存 P7.0/P7.1/P7.2 evidence 之實際原始碼與 JSON 逐一核對，非僅依 PRD 敘述推論。）

## Critical

1. **「已確認事實」清單遺漏一整個具真實 CE go evidence 的封存 task，使 authoritative matrix 有系統性漏報風險。**
   `git log --grep="P7.2 Slice"` 顯示 P7.2 實際有 **Slice A、Slice B**，封存於 `.trellis/tasks/archive/2026-08/08-07-churchreport-write-action-function-migrations/`（對應 `.ccg/tasks/archive/2026-08/p7-2-churchreport-write-action-function-migrations`）。其中：
   - `p7.2-slice-a-live-evidence.json`：`memberinfo.contact.update.basic.info`，CE 9.1／Data8／profile `sunnyvalechback`，`execution.outcome=go`、`operationExecuted=true`（2026-08-08）。
   - `p7.2-slice-b-live-evidence.json`：`memberinfo.contact.update.line.profile`、`memberinfo.contact.count.ungrouped.commitment`，兩者 `execution.outcome=go`。

   這 3 個 operation 在封存 P7.0 `coverage-matrix.json` 中原本標記 `not-implemented`／`candidate`，但現行原始碼已有對應 registry／Data8 executor／typed ProductClient（`Package02ContactProfileClient`、`Package02ContactBasicInfoUpdateClient`）與 ChurchReport 內建 disabled-by-default flag（`DynamicsAccess:Package02ContactBasicInfoUpdatesEnabled`、`...ContactProfileOperationsEnabled`，見 `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`）。

   然而本 child 的 PRD「已確認事實」、design.md「輸入與信任邊界」表、implement.md 的閱讀範圍全部只提到 P7.0／P7.1／P7.2（Slice C、D–H），**完全未列出這個 archive 目錄**，design.md step 4 的硬編分類規則也只涵蓋 P7.1 六個 read 與 P7.2 D–H 十三個 operation。若 analyzer 按目前輸入清單執行，這 3 row 會被判為 `evidence-pending`／`not-executed`，與事實不符 — 方向上是低估（不會虛報 CE 成功），但會直接違反驗收條件「matrix 不將…未驗證 execution 誤列」的對稱要求（此處是遺漏而非誤列，但同樣使 matrix 失去「authoritative」資格），並會讓 P7.4 排程把已具真實寫入證據的 capability 錯置到與 D–H（完全未執行）同等甚至更低的優先序。

2. **`consumer` schema 只有 3 個 enum 值，無法表達「ProductClient/executor 已完成、有真實 CE evidence，但尚未接上任何 ChurchReport controller」的中間態，也無法區分「local-only 完全不在 product assembly」與「曾經 wired 但用 flag 關閉」。**
   - Slice A/B 的 `TryCreatePackage02ContactBasicInfoClient` 原始碼註解明確寫「此 helper 只提供尚未接入 controller 的 composition support，不會自行啟用 ChurchReport 流量；正式 consumer cutover 屬 P7.4」— 這既不是 `migrated-enabled`，也不完全等同 P7.1 那種「controller 已接、只是 flag 關閉」的 `migrated-disabled`。
   - P7.2 D–H 的 13 個 operation（`P72ContinuationLocalOnlyCatalog` 等）**只存在於 `SpeechMessage.Dynamics.Abstractions`／`.Tests`，ChurchReport production assembly 內零引用**，這是比 D–H 文件敘述的「executor/consumer=false」更弱的狀態（連 production 呼叫點都不存在）。若 analyzer 僅依 `CeExecutorEnabled=false`／`ConsumerEnabled=false` 兩個布林值判斷，容易與「controller 已接但用 flag 關閉」的真實 `migrated-disabled`（如 P7.1）混淆，產生虛假的進度印象。
   → 建議 schema 新增至少兩個值：`client-ready-not-wired`（executor/productClient 完成、production 尚無呼叫點）與明確要求 `migrated-*` 只能在 **ChurchReport production assembly** 內找到直接呼叫時才可標示。

3. **P8 啟動順序目前只受 design.md 文字約束，沒有 machine-checkable gate。** design.md／implement.md 明確寫「P7.5 handoff 之後才建立 P8」，但 matrix schema 本身未定義一個可被 validator 機械檢查的「全 70 row 皆非 temporary-legacy 且 zero-reference」布林 gate 欄位；若僅靠人工檢視 matrix JSON 判斷是否可以開 P8，等同把安全邊界寄放在人工紀律而非 schema invariant，與「不可變規則」章節的其他欄位（獨立、有界、enum）風格不一致。

## Warning

1. **Slice C 的 `no-go-closed` 是以 slice/family 粒度封閉，但 Slice C 實際橫跨多個 Data8 operation class**（`Package02Data8ListManagementOperations`／`ContactProfileOperations`／`ContactBasicInfoWriteOperations`，對應「固定名單管理五大 operation」），而唯一一次 fresh `ExecuteFixture` no-go（`write-not-committed`）只發生在其中特定操作。design.md step 4「D–H 固定標為 not-executed／local-only」是安全的保守做法，但若同一套邏輯把 Slice C 全 family 用單一 slice 級別聚合覆蓋 row 級別證據，未來 child 可能誤判「Slice C 全部都試過且失敗」而低估已有的唯讀 preflight/provision `go` 證據。建議 CE evidence 欄位保留「哪個具體 operation 執行過哪次 cycle」的可追溯 metadata，不要讓 slice 聚合掩蓋 row 粒度。

2. **雙模型分析中 Gemini 產出（`.ccg/dual-model-runs/20260812-182236-.../gemini-architect-attempt-1.stdout.md`）將「`DownloadListManager` 把 operation-scoped `IOrganizationService` 寫回共享 `ToolUtility`」與「eviction callback 誤 Dispose 共享單例」列為現行 Critical**，但依 P7.2 release-candidate.md 第 27 點，**這兩個問題已在 Slice C 修正並有回歸測試覆蓋**（「Slice C 修正將借用的 CRM service 限制於當前操作呼叫鏈…不能回寫 Factory 共用 ToolUtility」）。若最終合併報告不修正這點，後續 child 可能誤以為需要重做已封存的 Slice C，違反 PRD「不重新開啟、修改或重試已封存 P4/P5/P6/P7.0/P7.1/P7.2」的明文範圍限制。這正是雙模型交叉核對本應攔截的落差，需要在合併時標註為「歷史風險，已修復」而非現行 Critical。

3. `ToolUtility.Tests` 對 `net10.0` `ToolUtility` 的 `net8.0` NU1201 相容性問題（P7.2 release-candidate.md 已知限制）在本 child 驗收條件中未特別排除；若 matrix validator 的 CI 步驟涉及 full solution restore/build，需要沿用既有規則「不能降版或跳過隔離測試掩蓋」，否則可能被誤判為 matrix builder 本身失敗。

## Info

1. Package02 目前有三組已存在的 Data8 operation 家族（ListManagement、ContactProfile、ContactBasicInfoWrite），但 P7.4 就緒度不同：僅 ContactProfile 與 ContactBasicInfoWrite 已在 ChurchReport 具備 disabled-by-default production flag/factory；ListManagement 尚無對應 production flag，仍停留在 Slice C 的 test-only 驗證範疇。Matrix 應逐 row 標示，不應合併成單一 "Package02" 分類。
2. design.md step 3「manifest pairing 找不到 production symbol 時應標 not-migrated，不以字串相似度猜測」這條規則本身設計正確 — 只要確實遵守，就能自動把 D–H 13 個 operation 判為 `not-migrated`（因為它們完全不在 ChurchReport assembly 內），值得保留為核心 invariant。

## Recommended matrix invariants

1. **evidence-independence invariant**：`ceEvidence.{ce82,ce91} = succeeded` 只能來自具名、可追溯到特定 sanitized live-evidence 檔（含 `operationId`、`outcome=go`、`operationExecuted=true`）的 row 級證據；不得由 sibling row、slice 聚合值或 unit test pass 推導。
2. **consumer-wiring invariant**：`consumer ∈ {migrated-enabled, migrated-disabled}` 只能在 **ChurchReport production assembly**（非 `Abstractions`／`Tests`）內找到對該 typed ProductClient/consumer symbol 的直接呼叫或 DI 消費路徑時才可標示；否則必須是 `not-migrated` 或新增的 `client-ready-not-wired`。
3. **input-completeness invariant**：matrix builder 必須先列舉 `.trellis/tasks/archive/2026-08/` 下所有含 `p7`／`churchreport-write`／`package0` 關鍵字的 task 目錄，而非只信任 PRD 手寫的「已確認事實」，並在 sanitized summary 中記錄實際掃描到的 archive 目錄數與 checksum，讓遺漏可被日後驗證發現。
4. **slice-to-row granularity invariant**：任何以 slice/family 聚合的歷史 CE 結論（如 Slice C no-go-closed）套用到多個 call site 時，必須保留「哪一 row 的哪一次 `ExecuteFixture` cycle」的可追溯連結，禁止讓 slice 級聚合掩蓋 row 級證據粒度差異。
5. **p75-removal-blocker completeness invariant**：只要 registry／executor／productClient／consumer／CE／rollout／temporaryLegacy 任一欄位不是其「完成」端值，`p75RemovalBlocker` 就不可為 `none`；標 `mixed` 時仍須保留可稽核的 detail 欄位。

## Safe next child boundaries

- 本 child（matrix）僅執行靜態掃描與文件更新；**不得**依本次發現的 Slice A/B 證據去啟動任何新的 CE cycle 或切流。
- 下一個 child 應優先把 matrix 中「已有真實 CE `go` 證據但 consumer 尚未接上 controller」的 row（即 Slice A/B 的 3 個 operation）標為 P7.4 資源投入的優先評估對象 — 僅代表排程優先序，不代表可直接切流。
- P7.3 special-resource child 只能以 matrix 中 `specialResourceRequirement ≠ none` 的 row 為界，不得混入尚未證明 production consumer 存在的 row。
- P7.5 只能在 matrix 對全部 70 row 皆無 `temporary-legacy` 且 ChurchReport zero-reference 掃描全綠後建立；P8 只能在 P7.5 immutable handoff 完成、且 matrix 具備第 3 點所述機械可檢查 gate 欄位為 true 後建立。

---
**降級狀態**：本輪為 Gemini 單模型（已產出 45 秒內可讀輸出，無 provider 阻擋）＋ Claude（本 session）架構分析；未發生 quota/session 阻擋需要 `-AllowSingleModelWhenQuotaBlocked` 降級判定。上述 Critical #1 需在合併報告或下一個 child 的輸入清單中優先修正，否則 authoritative gap matrix 的驗收條件（「matrix 不將…誤列」）會因遺漏而非誤列而失真。

---
SESSION_ID: 4fe5ab8e-87f9-4d8e-8e82-68ecb8b73184
