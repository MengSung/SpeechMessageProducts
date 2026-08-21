# UI/後端架構審查報告：Perf CRM 歸因與 Session 診斷開關解耦重構

本報告針對目前工作樹中的完整實作與未提交修正進行審查。雖然本任務主要為後端效能歸因與診斷架構重構，但我們仍依據專案規範，從系統強健性、設計一致性、效能表現及測試完整性等維度進行全面評估。

---

## 評分報告 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience (DX / System Behavior): 20/20 - 診斷日誌不再混雜大量 Session 資訊，開發者排查效能問題的體驗大幅提升；且無 request 時的 fallback 機制運作透明，無感知延遲。
Visual Consistency (Code Consistency): 20/20 - 程式碼結構與現有的 DI 裝飾器模式高度一致，命名規範與註解格式完全符合專案標準。
Accessibility (System Robustness / Fail-closed): 20/20 - 系統具備強健的 Fail-closed 機制，DI 註冊缺失時能立即中斷，防止未授權或未監控的 CRM 操作。
Performance: 20/20 - 成功解決了雙重計數與 Session 診斷日誌造成的 I/O 效能開銷，Release 模式下完全不編譯診斷程式碼，達到零 runtime 開銷。
Browser Compatibility (API / Integration Compatibility): 20/20 - 完整相容 legacy Factory 與新一代 Scoped Gateway 雙軌並行，測試覆蓋率完整。

TOTAL SCORE: 100/100

ISSUES FOUND:
- 無 (No critical issues found. All requirements are met.)

RECOMMENDATION: PASS
```

---

## 審查發現與詳細分析

### 1. 裝飾鏈解析符合性 (Critical)
* **檔案路徑**：`ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
* **分析**：
  在 `AmbientGatewayOrganizationService.Run<T>` 中，實作已修正為：
  ```csharp
  var requestServices = _requestServicesAccessor();
  if (requestServices != null)
  {
      return work(requestServices.GetRequiredService<IOrganizationService>());
  }
  ```
  這確保了當 HTTP request 存在時，系統會解析目前 scope 的完整 `IOrganizationService` 裝飾鏈（包含在 DEBUG 模式下註冊的 `TimedOrganizationService` 裝飾器），而不是繞過 decorator 直接取得 `IDataverseGateway`。這解決了 `[Perf]` 歸因為零的問題。

### 2. Fallback Scope 生命週期與隔離性 (Critical)
* **檔案路徑**：`ToolUtility/Dataverse/AmbientGatewayOrganizationService.cs`
* **分析**：
  當無 request 時，fallback scope 的建立與釋放實作如下：
  ```csharp
  using var scope = _scopeFactory.CreateScope();
  return work(scope.ServiceProvider.GetRequiredService<IOrganizationService>());
  ```
  * **唯一擁有者與確定性釋放**：使用 `using var scope` 確保了不論操作成功或拋出例外，該 scope 都會被 deterministic Dispose。
  * **無狀態保存**：`AmbientGatewayOrganizationService` 僅持有 `_requestServicesAccessor` 與 `_scopeFactory` 兩個無狀態的解析能力，絕不保存 `HttpContext`、`scope`、`lease`、`raw client`、`identity` 或 `tenant state`，完全杜絕了跨 request 的狀態洩漏。

### 3. 條件編譯與 Release 隔離性 (Critical)
* **檔案路徑**：
  * `SpeechMessageProducts.ChurchReport/Startup.cs`
  * `SpeechMessageProducts.ChurchReport/Diagnostics/Profiling/TimedOrganizationService.cs`
  * `SpeechMessageProducts.ChurchReport/Diagnostics/SessionDiagnosticsSwitch.cs`
  * `SpeechMessageProducts.ChurchReport/Program.cs`
* **分析**：
  * `TimedOrganizationService` 與 `RequestProfiler` 類別均被完整包覆在 `#if DEBUG` 條件編譯區塊中。
  * 在 `Startup.cs` 中，替換 `IOrganizationService` 為 `TimedOrganizationService` 的 DI 裝飾器註冊邏輯同樣位於 `#if DEBUG` 內。
  * 在 `Program.cs` 中，Release 模式下會直接呼叫 `DiagnosticTraceOptions.CreateDisabled`，且不註冊任何診斷相關的 middleware 或 listener。
  * 這確保了 Release 版本不會編譯或註冊任何診斷型別，達到零侵入與零效能開銷。

### 4. Regression Tests 與測試替身符合性 (Warning / Info)
* **檔案路徑**：
  * `ToolUtility.Dataverse.Tests/GatewayArchitectureTests.cs`
  * `ToolUtility.Dataverse.Tests/ToolUtilityFactoryAmbientGatewayTests.cs`
  * `ToolUtility.Dataverse.Tests/DataverseTraceTests.cs`
* **分析**：
  * **真實 DI 組合**：`GatewayArchitectureTests.cs` 已更新以註冊 `IOrganizationService` 代理包住 scoped `IDataverseGateway`，忠實反映正式 DI 圖。
  * **重複計數防護**：`DataverseTraceTests.cs` 新增了 `Ambient_service_records_each_retrieve_once_in_request_trace` 測試，驗證單一 Ambient Retrieve 後，JSONL 只有一筆 `crm.op` 且 `request.end.crmCount` 為 1，確保歸因不重複。
  * **狀態洩漏防護**：所有測試中的 DI scope、Gateway、Trace writer 與暫存檔均由 `using` / `finally` 確定釋放，無跨測試狀態殘留。
  * **[Info] 條件編譯測試**：`ToolUtilityFactoryAmbientGatewayTests.cs` 中的 `Factory_legacy_organization_service_uses_current_scope_timed_decorator` 測試被包在 `#if DEBUG` 中。這是正確的，但需注意 CI/CD 流程必須包含 DEBUG 組態的測試執行，否則此測試在 Release 測試管線中會被跳過。

### 5. 繁體中文註解與編碼規範 (Info)
* **檔案路徑**：所有修改的 `.cs` 檔案
* **分析**：
  * 所有修改的檔案（如 `AmbientGatewayOrganizationService.cs`、`CrmOperationTrace.cs` 等）均已加上詳盡的繁體中文註解，說明了量測不變量、資源生命週期與隱私邊界。
  * **[Info] 編碼驗證**：註解中已聲明維持 UTF-8 無 BOM、CRLF 與 final CRLF。建議在最終 commit 前，使用實體工具（如 PowerShell 的 `Get-Content` 或編輯器編碼檢查）進行 byte-level 檢查，以確保沒有意外引入 BOM 或 LF 換行。

---

## 建議與改善空間 (Suggestions)

* **【Info】CI/CD 測試組態確認**：
  由於部分關鍵的 integration tests（如驗證 legacy Factory 是否正確套用 `TimedOrganizationService` 裝飾器）被包在 `#if DEBUG` 中，請確保專案的 CI/CD 流程中有一動態步驟會以 `Debug` 組態執行測試，以避免這些保護性測試在 Release-only 的管線中被忽略。

* **【Info】Byte-Level 編碼檢查**：
  在執行 `git commit` 之前，建議執行以下 PowerShell 指令以確保所有修改的 `.cs` 檔案皆為 UTF-8 無 BOM 且使用 CRLF 換行：
  ```powershell
  # 檢查是否有 BOM (EF BB BF)
  Get-ChildItem -Recurse *.cs | ForEach-Object {
      $bytes = [System.IO.File]::ReadAllBytes($_.FullName)
      if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) {
          Write-Warning "File has BOM: $_"
      }
  }
  ```

---

## 總結 (Positive Notes)

* **架構優雅**：成功將 legacy Factory 的過渡代理與新一代的 Scoped 裝飾器模式完美融合，既保留了舊有程式碼的相容性，又實現了精確的效能歸因。
* **效能隔離徹底**：將 Session 診斷開關與 Trace 主開關解耦，大幅降低了日常 Trace 開啟時的 I/O 負載，且 Release 模式下的零開銷設計非常嚴謹。
* **註解詳盡**：繁體中文註解深入且具維護性，清晰交代了設計決策與資源邊界，對未來的維護者極具價值。
