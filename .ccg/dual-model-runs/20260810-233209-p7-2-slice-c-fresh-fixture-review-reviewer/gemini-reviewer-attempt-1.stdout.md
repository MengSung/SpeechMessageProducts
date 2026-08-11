# P7.2 Slice C fresh-fixture 審查報告

本報告針對 P7.2 Slice C fresh-fixture 相關的 `git diff` 變更進行安全性、正確性、防禦性設計及測試充分性審查。

---

## 一、 整體評估 (Summary)

本次變更主要強化了控制面（Control Plane）在處理測試 Fixture 時的防禦性設計，特別是針對本機檔案系統的 Reparse Point（如 Junction、Symbolic Link）防禦、嚴格的 CRLF/UTF-8 邊界解析，以及跨程序/跨工作階段的狀態隔離。程式碼展現了極高的防禦性編程水準，能有效防止 TOCTOU（Time-of-Check to Time-of-Use）漏洞與未授權的狀態覆寫。

---

## 二、 基準擁有者條件下的無變更保證確認 (No-Mutation Guarantee Confirmation)

**確認結果：確認安全，無變更（No-Mutation）保證確實保留。**

### 驗證理據：
1. **提早退出機制**：當 descriptor-bound 任務標記 Leader 屬於 Data8 `WhoAmI` 使用者時，系統會判定為 `baseline-owner-unavailable`。此判定發生在任何遠端 CRM 變更（如 `Create`、`Assign`、成員加入）以及本機 Ledger 首次持久化（Persist）之前。
2. **診斷檔隔離**：`TryWriteDiagnostic` 寫入的診斷檔僅包含固定的分類字串（如 `baseline-owner-unavailable`），不包含任何 CRM 識別碼或敏感資訊。父程序讀取此診斷檔後，僅會將其作為 `diagnosticCategory` 呈現，並維持 `outcome=no-go`、`safeToRetry=false`，且**絕不**觸發後續的 Descriptor 發布、Cleanup 派送或自動重試。
3. **離線合約測試驗證**：`P72FreshSliceCFixtureFileLedgerTests` 與 PowerShell 合約測試已明確驗證，在此分支下 Ledger 保持為空，且無任何 CRM 變更嘗試。

---

## 三、 審查發現與建議 (Actionable Findings)

### Critical (危急)
*無發現危急缺陷。*

### Warning (警告)

#### 1. 測試跳過導致 CI/CD 覆蓋率缺口
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureFileLedgerTests.cs`
* **行號**：第 1005 行 (於 `Constructor_rejects_a_parent_owned_root_with_a_reparse_point_ancestor` 測試方法)
* **說明**：該測試方法被標記為 `Skip = "Requires SeCreateSymbolicLinkPrivilege..."`。雖然這是由於 Windows 預設安全性原則限制非管理員權限建立目錄符號連結（Symbolic Link）所致，且程式碼中已實作 `P72FreshSliceCFixtureOwnedPathGuard` 進行逐層祖先檢查，但此測試被跳過意味著 CI/CD 管道中無法自動驗證「祖先目錄為 Reparse Point」的防禦邏輯。
* **建議**：建議在 CI/CD 執行環境中，提權或配置 `SeCreateSymbolicLinkPrivilege` 權限以啟用此測試；或設計一個 Mock 檔案系統屬性的單元測試，確保該遞迴檢查邏輯在不依賴 OS 權限的情況下仍有 100% 的單元測試覆蓋。

---

### Info (提示)

#### 1. 診斷檔寫入異常被完全忽略
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureLiveEvidence.cs`
* **行號**：第 138-142 行 (於 `TryWriteDiagnostic` 方法中)
* **說明**：為了避免寫入診斷檔時的次要 I/O 異常遮蔽了原本的主程序錯誤（如 Precondition No-Go），程式碼使用了空的 `catch (Exception)` 區塊。這符合 Fail-Closed 的安全原則，但若發生權限不足或磁碟空間不足等系統問題，將會無聲消逝，增加排查難度。
* **建議**：若系統有提供不記錄敏感資訊的內部安全日誌通道（非 Console/Stdout 避免干擾 Parent 解析），可於 catch 區塊中進行輕量級的 Trace 記錄。

#### 2. 嚴格 CRLF 檢查的效能考量
* **檔案路徑**：`ChurchReport.MemberInfo.Tests/P72FreshSliceCFixtureFileLedger.cs`
* **行號**：第 524-541 行 (於 `HasOnlyCrLfLineEndings` 方法中)
* **說明**：該方法對讀入的 Ledger 內容進行逐字元掃描以確保無單獨的 CR 或 LF。由於 Ledger 檔案大小被嚴格限制在 32 KiB 以內，此掃描的效能開銷極小且安全，此防禦性設計非常優異。

---

## 四、 結論

本次 Slice C fresh-fixture 的變更在安全性防禦（Reparse Point 阻斷、CRLF 邊界檢查、跨程序狀態防篡改）上做到了極為嚴密的防護，且確實保障了在 `baseline-owner-unavailable` 條件下的 **No-Mutation** 承諾。建議後續針對被 Skip 的測試在專用測試環境中進行權限解鎖，以確保防禦程式碼持續有效。
