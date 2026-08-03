# Phase 4C Compatibility-Harness 設計審查報告

本報告針對即將實作的 Phase 4C 唯讀身分識別子閘門（read-only identity sub-gate）相容性測試腳本 `Invoke-DynamicsOfficialWorkerCompatibility.ps1` 及其合約測試 `Invoke-DynamicsOfficialWorkerCompatibility.Tests.ps1` 進行設計審查與安全評估。

---

## 1. UX Analysis (使用者影響評估)

- **操作員使用體驗 (Operator Experience)**：
  - 本腳本定位為**唯讀身分識別子閘門**，而非完整的 Phase 4C 矩陣驗證工具。此限制必須在輸出與文件中明確揭露，避免操作員誤判系統已完全通過 Phase 4C 驗證。
  - 腳本支援 `-Json` 開關。當啟用時，無論成功或失敗，皆須輸出結構化且經過淨化（sanitized）的 JSON，以便上層自動化工具（如 CI/CD pipeline 或監控系統）解析。
  - 失敗時，腳本必須傳回非零結束代碼（exit code 1），以確保 fail-closed 政策在自動化流程中生效。
- **隱私與安全淨化 (Sanitization)**：
  - 輸出結果中絕不能包含敏感資訊（如真實的 Gateway URI、實體路徑、組織/使用者/部門 GUID、連線字串或詳細的例外堆疊追蹤）。
  - 針對 WhoAmI 回應中的敏感 GUID，設計採用 `identityShape` 結構（例如 `hasUserId: true`、`organizationIdMatchesExpected: true`），僅揭露結構特徵而非真實值，此設計在保護隱私的同時保留了足夠的診斷價值。

---

## 2. Design Evaluation (設計一致性與模式)

- **驗證邏輯一致性**：
  - 腳本對 `DeploymentManifestPath` 與 `GatewayOverlayPath` 的讀取與驗證，必須與 `New-DynamicsOfficialWorkerDeployment.ps1` 保持一致，採用嚴格的 JSON 格式檢查（限制檔案大小、無 UTF-8 BOM、CRLF 換行、無重複屬性）。
  - 必須驗證 overlay 中的 `WorkerKind`、`PackageLockId` 與 `WorkerExecutableSha256` 是否與 manifest 中對應的 worker 資訊完全一致，且實體執行檔存在且雜湊值正確。
- **Windows Identity 授權比對**：
  - 腳本必須在進行任何網路呼叫前，先讀取相鄰的 `appsettings.json`（及 `appsettings.Development.json`，若存在），模擬 .NET Configuration 的合併規則，解析出 `ActiveWorkloadBindingSet` 指定的 active binding set。
  - 必須比對目前的 Windows Identity（優先比對 SID，次之比對 Principal Name）是否在 active binding 中被授權執行 `runtime.health.whoami` 操作與指定的 `ProfileAlias`。若未授權，必須立即 fail closed，不發起任何網路請求。

---

## 3. Technical Considerations (技術考量與架構影響)

- **.NET HttpClient 資源生命週期管理**：
  - 為了確保在 Windows PowerShell 5.1 與 .NET Framework 4.8 環境下的相容性與資源釋放，必須使用 `[System.Net.Http.HttpClient]` 與 `[System.Net.Http.HttpClientHandler]`，而非 `Invoke-RestMethod`。
  - 必須在 `try/finally` 區塊中，確定性地釋放（Dispose）所有網路資源（`HttpClientHandler`、`HttpClient`、`HttpRequestMessage`、`StringContent`、`HttpResponseMessage` 與 `CancellationTokenSource`），防止 socket 殘留或記憶體洩漏。
- **JSON 註解處理**：
  - 既有的 `appsettings.json` 中包含 `//` 單行註解。Windows PowerShell 5.1 的 `ConvertFrom-Json` 不支援註解，直接解析會導致異常。腳本在解析前必須先使用正規表示式過濾掉這些註解。

---

## 4. Options (替代方案與權衡)

- **方案 A：使用 `Invoke-RestMethod`**
  - *優點*：語法簡單，PowerShell 原生支援。
  - *缺點*：難以精確控制 `HttpClientHandler` 的屬性（如停用代理伺服器、停用重新導向、使用預設認證），且無法保證底層連線與資源的確定性釋放，不符合安全與資源清理要求。
- **方案 B：使用 .NET `HttpClient` 搭配 `CancellationTokenSource` (推薦)**
  - *優點*：完全掌控 HTTP 標頭、認證、代理伺服器與重新導向設定，且能透過 `try/finally` 確保資源 100% 釋放，支援有界限的逾時控制。
  - *缺點*：程式碼較為繁瑣，需手動處理非同步工作（`GetAwaiter().GetResult()`）。

---

## 5. Recommendation (推薦做法與理由)

推薦採用 **方案 B**。此做法能完全滿足安全性（停用代理與重新導向、使用 Windows 整合認證）、可靠性（確定性資源釋放、有界限逾時）與 fail-closed 政策。

---

## 6. Concrete Findings (具體發現與修正建議)

### Warning: `appsettings.json` 註解解析失敗風險
- **路徑**：`docs/scripts/Invoke-DynamicsOfficialWorkerCompatibility.ps1` (待建立)
- **理由**：既有的 `appsettings.json` 包含 `//` 註解。若直接使用 `ConvertFrom-Json` 解析會拋出異常。
- **修正建議**：在讀取 `appsettings.json` 內容後，先使用正規表示式過濾註解，再進行 JSON 解析：
  ```powershell
  $cleanText = $rawText -replace '(?m)^\s*//.*$', ''
  ```

### Warning: 異常訊息洩漏敏感路徑或 URI
- **路徑**：`docs/scripts/Invoke-DynamicsOfficialWorkerCompatibility.ps1` (待建立)
- **理由**：當驗證或網路呼叫失敗時，`catch` 區塊若直接輸出 `$_.Exception.Message`，可能會洩漏實體檔案路徑、內部 URI 或 GUID。
- **修正建議**：在輸出錯誤訊息前，必須進行正規表示式淨化，將路徑、URI 與 GUID 替換為遮罩值（如 `<path>`、`<uri>`、`<guid>`）。

### Info: 測試合約中的網路 Mock 策略
- **路徑**：`docs/scripts/Invoke-DynamicsOfficialWorkerCompatibility.Tests.ps1` (待建立)
- **理由**：本地合約測試無法依賴真實的 Gateway 服務。
- **修正建議**：測試應著重於驗證「驗證失敗時的 fail-closed 行為」（不發起網路呼叫即退出）以及「驗證通過但網路無法連線時的淨化輸出行為」（將 `GatewayBaseUri` 指向無效的本地埠如 `https://localhost:65535`，驗證是否輸出 `outcome = "failed"` 的淨化 JSON 並傳回 exit code 1）。
