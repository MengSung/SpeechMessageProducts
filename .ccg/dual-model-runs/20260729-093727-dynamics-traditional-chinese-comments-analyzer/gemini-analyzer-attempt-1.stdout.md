# Dynamics C# 繁體中文註解與純 UTF-8 改造分析報告

本報告針對自 `58657c0f`（Dynamics 無 SDK Gateway Phase 0）起到目前 HEAD，以及工作區尚未提交的變更中，所有新增或修改的 `.cs` 檔案進行繁體中文註解與純 UTF-8（無 BOM）與 CRLF 格式改造分析。

---

## 1. 檔案編碼與亂碼（Mojibake）分析

### 🔴 關鍵發現：工具鏈解碼問題而非檔案損毀
在透過 read-only 讀取工具檢視 `.cs` 檔案（如 `DynamicsWebApiClient.cs` 與 `DonationDynamicsAccessBootstrap.cs`）時，會看到大量的亂碼（例如 `// 瑼?嚗peechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`）。

- **根因分析**：檔案在磁碟上確實是以 **純 UTF-8 (無 BOM)** 編碼儲存。然而，讀取工具在 Windows 環境下執行時，預設使用了系統的 ANSI 編碼（繁體中文 Windows 預設為 **CP950 / Big5**）來解碼 UTF-8 的 byte 串，因而產生了 mojibake。
- **還原驗證**：將亂碼字串以 CP950 編碼轉回 byte 陣列，再以 UTF-8 解碼，即可完全還原為正確的繁體中文：
  - `瑼?` (CP950: `0xBF 0xFB 0xEE 0x5F 0x3F`) $\rightarrow$ 還原後為 `檔案` (UTF-8: `0xE6 0xAA 0x94 0xE6 0xA1 0x88`)。
- **結論**：目前工作樹中的檔案編碼方向正確（為 UTF-8），本次改造應繼續保持 **純 UTF-8（無 BOM）** 與 **CRLF** 格式，並在編輯器中強制指定 UTF-8 編碼以避免 mojibake 寫入。

---

## 2. 建議的分批順序與檔案清單

為了降低行為改變風險並確保逐步驗證，建議將改造範圍內的 `.cs` 檔案分為三批進行：

### 批次一：核心 Production 程式碼 (Core Production)
- **優先級**：最高 (Critical)
- **選取規則**：位於 WebApi、Embedded 專案及 ChurchReport 服務層中，直接參與 Dynamics 存取與業務邏輯的核心類別。
- **檔案清單**：
  1. `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiClient.cs`
  2. `SpeechMessage.Dynamics.WebApi/Runtime/DynamicsWebApiOptions.cs`
  3. `SpeechMessage.Dynamics.Embedded/DependencyInjection/EmbeddedServiceCollectionExtensions.cs`
  4. `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs`
  5. `SpeechMessageProducts.ChurchReport/Services/DonationDedicationFeeFormService.cs`
  6. `SpeechMessageProducts.ChurchReport/Services/DonationFeeQueryService.cs`
  7. `SpeechMessageProducts.ChurchReport/Services/DonationKeyInDedicationService.cs`
  8. `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`
  9. `SpeechMessageProducts.ChurchReport/Controllers/DedicationController.cs`
  10. `SpeechMessageProducts.ChurchReport/Models/DonationPaymentManager.cs`

### 批次二：Host / Integration 程式碼 (Host/Integration)
- **優先級**：中 (Warning)
- **選取規則**：負責 Gateway 啟動、 admission 控制與 readiness 探針的宿主層程式碼。
- **檔案清單**：
  1. `SpeechMessage.Dynamics.Gateway/Program.cs`
  2. `SpeechMessage.Dynamics.Gateway/DynamicsGatewayReadinessService.cs`

### 批次三：測試 / 診斷 / 浸泡測試 (Tests/Soak/Diagnostics)
- **優先級**：低 (Info)
- **選取規則**：單元測試、整合測試、Soak 測試及暫時性診斷工具。
- **檔案清單**：
  1. `SpeechMessage.Dynamics.Tests/DynamicsWebApiClientTests.cs`
  2. `SpeechMessage.Dynamics.Tests/Phase4IsolationSoakTests.cs`
  3. `ChurchReport.MemberInfo.Tests/Payments/DonationFeeQueryServiceAsyncTests.cs`
  4. `ChurchReport.MemberInfo.Tests/Payments/` 底下所有命名與相容性測試 `.cs` 檔案（共約 10 個）
  5. `.ccg/diagnostics/LegacySoapProbe/Program.cs`
  6. `.ccg/diagnostics/LegacySoapProbe/Program.Net48.cs`

---

## 3. 關鍵檔案應補強之註解重點

針對高優先檔案，應補強實質深度的繁體中文註解，避免逐行翻譯，重點在於說明「為什麼」與「安全/效能邊界」：

| 檔案路徑 | 應補強註解之具體類型/方法/分支 | 註解應涵蓋的技術重點 |
| :--- | :--- | :--- |
| `DynamicsWebApiClient.cs` | `ExecuteRegisteredOperationAsync`<br>`ExecuteFetchXmlAsync` | 1. 說明如何避免 SDK 依賴，直接透過 Web API 進行安全呼叫。<br>2. 詳細說明 `ApprovedWebApiRoot` 的安全檢查機制，防止 SSRF 與 URL 繞過攻擊。<br>3. 說明 `HttpClient` 的生命週期與 `CancellationToken` 傳播機制。 |
| `EmbeddedServiceCollectionExtensions.cs` | `AddSpeechMessageDynamicsEmbedded`<br>憑證解析分支 | 1. 說明 Embedded 模式下的憑證來源隔離（`HostIdentity` vs `SecretReference`）。<br>2. 說明 ADFS OAuth 驗證模式下的 Token 取得與快取生命週期，防止 Token 洩漏與過期。 |
| `DonationDynamicsAccessBootstrap.cs` | `ProcessHost` 靜態欄位<br>`CreateFeeFormService` | 1. **核心重點**：說明 `ProcessHost` 的單例生命週期管理，解釋為什麼不能每次請求都 new `ServiceProvider`（防止 Socket/Timer/Handler 洩漏）。<br>2. 說明 `Package01FeeReadsEnabled` 預設為 `false` 的安全防線。 |
| `DonationFeeQueryService.cs` | `FillFeeListAsync`<br>`FillFeeListViaPackage01Async` | 1. 說明在 `Package01Enabled` 啟用與否時的路由分流邏輯。<br>2. 說明 FetchXML 查詢的參數綁定與防 SQL 注入/防竄改機制。 |
| `DonationPaymentManager.cs` | `DonationPaymentManager` 建構子<br>金流處理分支 | 1. 說明 UI 控制器與後端 CRM 服務的隔離，如何避免 Session Leakage。<br>2. 說明 LINE 通知與金流處理的非同步生命週期與錯誤處理。 |

---

## 4. 既有英文註解與 XML Summary 處理原則

- **XML Summary 契約保留**：
  - 對於 DTO、enum、常數與介面（如 `IDynamicsWebApiClient`、`DynamicsWebApiOptions`），既有的精確 XML summary 說明了公開 API 契約，**應予以保留**。若需新增或修改，則必須使用繁體中文。
- **複雜邏輯與分支的「為什麼」翻譯**：
  - 既有程式碼中若有英文註解僅是「逐行翻譯程式碼行為」（例如 `// Set total amount to zero`），應予以**精簡或刪除**。
  - 若英文註解說明了「為什麼使用此特定演算法」或「阻擋條件的歷史背景」，應將其**完整翻譯為繁體中文**，並補充資源擁有權與 Dispose 生命週期的說明。
- **測試註解整改**：
  - 排除僅重述 Arrange/Act/Assert 的英文註解。
  - 新增繁體中文註解，描述該測試所證明的**隔離/生命週期/效能契約**、**故障注入（Fault Injection）**的基線與預期行為。

---

## 5. 驗證方法與工具

為確保「零行為改變」且檔案格式完全符合規範，應採用以下驗證程序：

### A. 行為無改變驗證 (Zero Behavior Change)
1. **測試套件驗證**：在修改前與修改後分別執行 `dotnet test`，確保測試通過率維持 100%，且測試語意無漂移。
2. **Git Diff 審查**：使用 `git diff --ignore-space-at-eol` 確保除了註解文字與 XML Summary 之外，沒有任何執行程式碼、公開 API、序列化契約或效能路徑被修改。

### B. 檔案格式驗證 (純 UTF-8 無 BOM, CRLF)
使用 PowerShell 腳本進行自動化掃描，嚴格禁止手動檢查：

```powershell
# 1. 檢查是否有 BOM (UTF-8 BOM 的前三個 byte 是 0xEF, 0xBB, 0xBF)
Get-ChildItem -Recurse -Filter *.cs | ForEach-Object {
    $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
        Write-Error "檔案包含 BOM，不符合純 UTF-8 規範: $_"
    }
}

# 2. 檢查換行符號是否為嚴格 CRLF (不允許單獨的 LF)
Get-ChildItem -Recurse -Filter *.cs | ForEach-Object {
    $text = [System.IO.File]::ReadAllText($_.FullName)
    if ($text -match "(?<!\r)\n") {
        Write-Error "檔案包含非 CRLF 換行符號 (LF-only): $_"
    }
}

# 3. 檢查是否有亂碼 (mojibake) 或無效字元 (U+FFFD)
Get-ChildItem -Recurse -Filter *.cs | ForEach-Object {
    $text = [System.IO.File]::ReadAllText($_.FullName)
    if ($text -contains "") {
        Write-Error "檔案包含無效字元 (mojibake/U+FFFD): $_"
    }
}
```

---

## 6. 風險評估與安全邊界

### 🔴 Critical 風險
1. **`DynamicsAccess:Package01FeeReadsEnabled` 啟用風險**：
   - **風險描述**：此開關在 `appsettings.json` 中必須維持 `false`。若在改造過程中因測試或配置對齊而不慎將其啟用，將會啟用尚未完全驗證的 Web API 讀取路徑，導致生產環境行為改變。
   - **防範對策**：在 `DonationDynamicsAccessBootstrap.cs` 的註解中，必須以顯眼的繁體中文標註此開關的強制性限制，且在程式碼中加入靜態斷言或防禦性檢查。

2. **明文憑證與 Token 洩漏風險**：
   - **風險描述**：在 `appsettings.json` 或 `launchSettings.json` 中可能殘留測試用的明文密碼或 ADFS Token。
   - **防範對策**：註解中絕對不得包含任何真實的憑證、密碼或 Token。所有敏感資訊必須使用 `SecretReference` 進行隔離。

### ⚠️ Warning 風險
1. **Session 與 Resource Leakage**：
   - **風險描述**：`DonationDynamicsAccessBootstrap` 內部使用 process-level 的 `ServiceProvider`。若註解未說清楚其生命週期，後續維護者可能會將其改為每次請求重建，導致 `HttpClient`、`Socket` 與 `Timer` 快速耗盡。
   - **防範對策**：在 `Bootstrap` 類別頂部加入詳細的繁體中文生命週期說明，強調 `ProcessHost` 的單例責任。

2. **測試假陽性 (False Positives)**：
   - **風險描述**：在 `Phase4IsolationSoakTests.cs` 等測試中，若註解未說明故障注入的基線，維護者可能會誤將「因配置錯誤而通過的測試」視為成功。
   - **防範對策**：測試類別必須具備繁體中文註解，說明如何區分「預期的隔離失敗」與「真正的系統異常」。

### ℹ️ Info 風險
1. **大量註解造成的維護成本**：
   - **風險描述**：過度冗長、空泛的繁體中文註解會降低程式碼的緊湊度，增加閱讀負擔。
   - **防範對策**：遵循「說明為什麼（Why）而非做什麼（What）」的原則，僅在複雜分支、安全邊界與生命週期管理處補充實質深度的註解。
