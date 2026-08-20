# UI/系統審查報告：Perf CRM 歸因與 SessionVerbose 解耦 (Step 1 & Step 2)

本報告針對 `feat/dataverse-scoped-connection` 分支中，關於 **Session 診斷開關解耦 (Step 1)** 與 **CRM 歸因裝飾器重構 (Step 2)** 的 unstaged 實作進行審查。

---

## 驗證評分 (VALIDATION REPORT)

```
VALIDATION REPORT
=================
User Experience: 18/20 - 診斷日誌不再被高頻 Session 資訊污染，且 CRM 呼叫能正確歸因，大幅提升運維診斷體驗。因缺乏實際 Trace 數據扣 2 分。
Visual Consistency: 20/20 - 日誌格式與 [Perf] 標籤保持一致，SessionVerbose 預設關閉，確保日誌輸出整潔一致。
Accessibility: 20/20 - 敏感 Session 資訊在 Release 模式下被完全移除，且 SessionVerbose 受到 allowEnabled 安全邊界保護，符合安全性合規。
Performance: 20/20 - 移除無效的 TimedToolUtilityProvider 欄位替換，改用 DI 裝飾器，且 Session 診斷在 Release 模式下被完全編譯期移除，消除 runtime 開銷。
Browser Compatibility: 20/20 - DI 裝飾器正確處理了三種 ServiceDescriptor 註冊形式，確保與 ASP.NET Core DI 容器完全相容。

TOTAL SCORE: 98/100

ISSUES FOUND:
- [Critical] Step 4 實際追蹤數據缺失 (Missing Evidence)：implement.md 中的數據表格為空，且未生成 ChurchReport-Trace-Report.md。

RECOMMENDATION: NEEDS_IMPROVEMENT
```

---

## 審查發現分類 (Findings)

### 1. Critical (嚴重問題)
*   **Step 4 實際追蹤數據缺失 (Missing Evidence)**
    *   **具體檔案**：`.trellis/tasks/08-20-perf-crm-attribution-and-switch-decouple/implement.md` (第 56-70 行)
    *   **判定理由**：任務明確要求「缺失的已驗證 CRM 追蹤必須報告為缺失證據，絕不能進行估計」。目前 `implement.md` 中的 Step 4.9 數據表格完全空白，且 repository 中並未生成 `ChurchReport-Trace-Report.md` 檔案。這導致無法驗證重構後的實際效能數據與歸因正確性。

### 2. Warning (警告/潛在風險)
*   *無。目前的程式碼實作在 DI 生命週期、隔離性與安全防線方面皆非常嚴謹。*

### 3. Info (資訊/合規性確認)
*   **DI 生命週期與資源所有權合規**
    *   **具體檔案**：`SpeechMessageProducts.ChurchReport/Startup.cs` (第 425-476 行)
    *   **判定理由**：`IOrganizationService` 的裝飾器註冊正確繼承了原始的 `Scoped` 生命週期，確保了 wrapper 與 inner 服務的生命週期一致，避免了跨請求的狀態提升或資源洩漏。同時，在 factory 中依據原 descriptor 的三種形式（Factory/Instance/Type）重建 inner，避免了直接解析導致的無限遞迴。
*   **SessionVerbose 語意與解耦合規**
    *   **具體檔案**：`ToolUtility/Diagnostics/DiagnosticTraceOptions.cs` (第 140-182 行)
    *   **判定理由**：`SessionVerbose` 正確地與一般 `Enabled` 開關解耦，並在 `FromConfiguration` 中以 `allowEnabled` 進行收斂，確保 Release 模式下必定為 `false`。
*   **Release `#if DEBUG` 保護合規**
    *   **具體檔案**：`SpeechMessageProducts.ChurchReport/Diagnostics/SessionDiagnosticsSwitch.cs` (整檔)
    *   **判定理由**：整個 `SessionDiagnosticsSwitch` 類別以及 `InMemoryDataContextSmallGroup.cs` 中的 `WriteSessionDiagnostic` 呼叫點都正確地被 `#if DEBUG` 和 `[Conditional("DEBUG")]` 保護，確保 Release 建置時不會引入任何診斷開關或高頻日誌輸出。
*   **測試覆蓋率合規**
    *   **具體檔案**：
        *   `ToolUtility.Dataverse.Tests/DiagnosticTraceOptionsTests.cs`
        *   `ToolUtility.Dataverse.Tests/StartupOrganizationServiceProfilingTests.cs`
    *   **判定理由**：新增了完整的單元測試，分別驗證了 `SessionVerbose` 的組態讀取邏輯，以及 `Startup` 中 `IOrganizationService` 的 Scoped 裝飾器註冊行為與生命週期一致性。

---

## 建議與改善措施 (Suggestions)

1.  **執行 Step 4 驗證並收集數據**：
    *   需要在開發/測試環境中實際啟動應用程式，並執行 CRM 相關操作。
    *   執行 `SpeechMessageProducts.ChurchReport/Tools/Analyze-ChurchReportTraces.ps1` 腳本以生成 `ChurchReport-Trace-Report.md`。
    *   將實際的量測數據填入 `implement.md` 的 Step 4.9 表格中，以補齊缺失的證據。
2.  **確認檔案編碼**：
    *   確保所有新建立或修改的 `.cs` 檔案皆維持 **UTF-8 without BOM** 與 **CRLF** 換行格式。

---

## 總結 (Summary)

本任務的 **Step 1 (Session 診斷開關解耦)** 與 **Step 2 (CRM 歸因裝飾器重構)** 的程式碼實作非常優異，完美解決了原本 `ToolUtilityFacade` 繞過 `TimedOrganizationService` 裝飾器的設計缺陷，且在 DI 生命週期與 Release 安全防線上做到了 fail-closed 的嚴謹防護。

目前唯一的阻礙為 **Step 4 實際追蹤數據的缺失**。請在實際執行並填入數據後，重新提交驗證。
