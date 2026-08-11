# P7.2 Seed Control-Plane Analysis 審查報告

本報告針對 P7.2 Slice C 本機固定裝置控制面循環（local fixture control-plane loop）的修復方案進行設計分析與架構審查。重點在於解決清理（cleanup）後因 fresh descriptors 被刪除導致下一次 `FreshPreflightProbe` 無法啟動的死鎖問題，並確保 legacy `.bak` 遷移時的安全性與當前使用者隔離性。

---

## 1. UX Analysis (使用者影響評估)

- **操作流暢度與自癒能力**：
  在舊有設計中，Slice C 清理步驟會徹底刪除 per-cycle 的 fresh descriptors。然而，下一次 `FreshPreflightProbe` 啟動時卻錯誤地要求這些已被刪除的 descriptors 必須存在，導致維運人員陷入「無法啟動新循環」的死鎖狀態。引入持久化的 **seed descriptor** 後，操作者可以無縫啟動新的唯讀探測與 provision 循環，大幅提升控制面的自癒能力與操作體驗。
- **安全性與權限隔離**：
  透過將 seed descriptor 綁定至當前 Windows 使用者（current-Windows-user-bound），可防止多用戶共享環境下的配置污染或越權操作。
- **明確的診斷反饋**：
  週報（Weekly Report）的基數分類被明確劃分為 `zero-active`、`exactly-one-active` 與 `duplicate-active`/`unavailable`，且不允許任何自動突變。這為操作者提供了清晰、確定性的狀態反饋，避免了模糊的「not-exactly-one-active」分類帶來的決策困擾。

---

## 2. Design Evaluation (一致性與模式)

- **Seed 與 Fresh Descriptor 的架構分離**：
  - **Seed Descriptor**：持久保存，僅持有驗證過的靜態列表 IDs、任務標記的 baseline leader ID、UTC Sunday key 以及脫敏的部署元數據。
  - **Fresh Descriptor Pair**：生命週期僅限於單次循環，在成功 provision 後發佈，並在 cleanup 階段被安全刪除。
  - 這種分離模式符合「最小權限」與「單一職責」原則，避免了持久配置與臨時狀態的混淆。
- **唯讀遷移候選（Read-Only Migration Candidate）**：
  允許一次性引導（one-time bootstrap）讀取舊的 `.bak` 檔案，但必須將其視為**唯讀遷移候選**，且必須忽略其中過時的 `targetOwnerId` 欄位，絕不能將其作為 owner authority 或 CE 突變目標。這與系統現有的「先讀後判斷、失敗即停」的防錯模式高度一致。

---

## 3. Technical Considerations (控制面與腳本架構影響)

### Critical 1: 探針階段錯誤的 Descriptor 存在性檢查
- **檔案路徑**：`docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1` (第 2494-2499 行)
- **具體程式碼**：
  ```powershell
  if (-not (Test-Path -LiteralPath $SourceFixtureDescriptorPath -PathType Leaf) -or
      -not (Test-Path -LiteralPath $FixtureDescriptorPath -PathType Leaf)) {
      Write-HandoffResult (New-HandoffResult -Outcome 'no-go' -Reason 'fixture-input-required' ...)
  ```
- **影響與理由**：
  在執行 `FreshPreflightProbe` 時，腳本在進入探針分支前就強制要求 `$SourceFixtureDescriptorPath` 與 `$FixtureDescriptorPath` 必須存在。由於 cleanup 階段已將其刪除，導致下一次探針直接失敗。必須修改此處邏輯，使 `FreshPreflightProbe` 僅依賴持久的 **seed descriptor**，而將 fresh descriptors 的檢查移至正式的 provision/cleanup 分支中。

### Critical 2: Legacy `.bak` 中過時 `targetOwnerId` 的安全隔離
- **檔案路徑**：`docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1` (第 501-509 行)
- **影響與理由**：
  測試合約中已明確定義 `targetOwnerId` 必須被拒絕（`fixture-input-invalid`）。在引導遷移過程中，若讀取 `.bak` 檔案，必須在反序列化後**立即丟棄或忽略** `targetOwnerId` 欄位，絕不能將其傳遞給 C# 端的 `P72FreshSliceCFixtureProvisioner` 作為 `baselineOwnerId` 的來源，否則會破壞「不對非任務擁有記錄進行突變」的安全性紅線。

### Critical 3: 當前使用者隔離與 Reparse Point 防護
- **檔案路徑**：`docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`
- **影響與理由**：
  Seed descriptor 必須寫入當前 Windows 使用者名稱（`[Security.Principal.WindowsIdentity]::GetCurrent().Name`），並在讀寫時呼叫 `RejectReparsePoint`。若未進行此隔離，惡意 process 可能透過建立符號連結（symlink/junction）將 seed descriptor 指向系統關鍵檔案，造成越權讀寫或資訊洩漏。

### Warning 1: 不可重試（Non-retryable）的失敗投影
- **檔案路徑**：`docs/scripts/Invoke-Package02Data8ListManagementEvidence.ps1`
- **影響與理由**：
  任何 no-go、逾時、讀回不一致或不確定的清理，都必須投影為 `safeToRetry = $false`，且必須保留 ledger。如果腳本在清理失敗時嘗試自動重試，可能會在 CRM 端產生重複的實體或孤立的關聯，破壞資料一致性。

### Info 1: 測試覆蓋率與特權跳過問題
- **檔案路徑**：`ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureFileLedgerTests.cs` (第 988 行)
- **影響與理由**：
  部分涉及 reparse point 祖先目錄驗證的測試（如 `Constructor_rejects_a_parent_owned_root_with_a_reparse_point_ancestor`）因缺少 `SeCreateSymbolicLinkPrivilege` 權限而在本機環境中被跳過。建議在 CI/CD 流程中配置具備該特權的執行節點，以確保此安全防線得到實質驗證。

---

## 4. Options (替代方案與權衡)

### 方案 A：在現有 Fresh Descriptor 中直接引入 Seed 標記（不分離 Schema）
- **做法**：不建立獨立的 seed descriptor 檔案，而是讓 `list-management-fixture.json` 在 cleanup 時不被刪除，僅清空其中的 fresh IDs，保留靜態配置。
- **優點**：不需要新增檔案路徑與環境變數，對現有腳本改動較小。
- **缺點**：同一個檔案在不同階段具有不同的 schema 狀態（有時含有 fresh IDs，有時沒有），增加了解析器的複雜度，且容易在 cleanup 失敗時殘留部分 fresh IDs，導致下一次 preflight 誤判。

### 方案 B：完全分離 Seed Descriptor 與 Fresh Descriptor Pair（推薦方案）
- **做法**：建立獨立的 `seed-descriptor.json`，持久保存於當前使用者的 `LOCALAPPDATA` 目錄下。`FreshPreflightProbe` 僅讀取此檔案。正式 provision 成功後，才在臨時目錄或專屬路徑下產生 `contact-basic-info-fixture.json` 與 `list-management-fixture.json`。
- **優點**：生命週期與職責完全分離，cleanup 階段可以安全地 `Remove-Item` 整個 fresh pair，而不會影響 seed。Schema 結構單純，fail-closed 邊界清晰。
- **缺點**：需要定義新的環境變數與檔案路徑。

---

## 5. Recommendation (首選方案與理由)

**首選方案：方案 B（完全分離 Seed Descriptor 與 Fresh Descriptor Pair）**

### 理由：
1. **徹底消除死鎖循環**：由於 `FreshPreflightProbe` 僅依賴持久的 seed descriptor，即使 cleanup 徹底刪除了 fresh descriptor pair，探針仍能正常執行，從而安全地開啟下一個 fresh cycle。
2. **極高的遷移安全性**：引導過程讀取 `.bak` 時，僅將其作為唯讀的 migration candidate，並在程式碼層面明確忽略 `targetOwnerId`，完全杜絕了過時 owner 欄位成為突變目標的風險。
3. **符合 Fail-Closed 哲學**：清理階段僅刪除 fresh pair 與 ledger，保留 seed。任何異常步驟均不進行重試，並保留 ledger 供人工排查，確保控制面的狀態轉移是確定且安全的。
