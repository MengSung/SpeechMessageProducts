# P7.4 Feature-Gate Capacity Enablement Audit Report

## 1. 審計結論 (Decision)
**NO-GO**

---

## 2. 具體儲存庫證據 (Concrete Repository Evidence)

### 條件 (a)：Legacy ToolUtility 與 Gateway/Data8 路徑共享持久性組織准入/主機插槽授權 (Durable Organization Admission/Host-Slot Authority)
* **未達成**。
* **證據 1 (`DonationFeeQueryService.cs`)**：
  在 `DonationFeeQueryService.cs` 中，當 `_package01Enabled` 為 `false` 時，系統會調用舊有的 `_utility.RetrieveDedicationFeeByDateFetchXml`（即 `ToolUtilityClass` 實例）來獲取數據：
  ```csharp
  EntityCollection feeEntities = _utility.RetrieveDedicationFeeByDateFetchXml(
      fullName,
      contactId.ToString(),
      model.QueryStartDate,
      model.QueryEndDate);
  ```
  此舊有路徑直接調用 SDK/WCF，完全沒有接入或受控於 `SqlRuntimeHostSlotCoordinator` 的准入控制。
* **證據 2 (`SqlRuntimeHostSlotCoordinator.cs`)**：
  雖然 `SqlRuntimeHostSlotCoordinator` 實現了持久性的 `IRuntimeHostSlotCoordinator`（`IsDurable => true`），但它僅在 `SpeechMessage.Dynamics.ControlPlane` 的新版執行器生命週期中被調用，舊有的 `ToolUtilityClass` 並未引入此協調器。兩者無法共享同一個主機插槽授權。

### 條件 (b)：經運維驗證的「先排空再切換且無重疊」運行手冊 (Operationally Verified Drain-First Non-Overlap Runbook)
* **未達成**。
* **證據 3 (`.trellis/tasks/08-12-churchreport-productclient-cutover/implement.md`)**：
  實施清單中的 Phase 4 相關步驟仍處於未完成狀態（未勾選）：
  ```markdown
  - [ ] Gate enablement 與 read-only evidence audit：是否已具備 durable shared admission authority 或 verified drain-first non-overlap runbook？若無，必須為 exact no-go，且保持 gates=false。
  ```
* **證據 4 (`.trellis/tasks/08-12-churchreport-productclient-cutover/design.md`)**：
  設計文檔中已明確指出：
  > 「基於上述評估，結論為『P7.4 enablement no-go』，『保持現有配置』。不可啟用 flag。」

---

## 3. 發現分類 (Findings Classification)

### Critical (危險)
* **控制面隔離缺失**：Legacy `ToolUtility` 與新版 `Gateway/Data8` 路徑在並存時，缺乏統一的分布式鎖或准入協調機制。若在未執行排空手冊的情況下啟用功能閘，將導致同一個 Organization 的流量同時併發流向新舊兩條路徑，違反 `cross-user-isolation-and-performance.md` 中關於組織級容量限制與隔離的絕對規則。

### Warning (警告)
* **配置狀態一致性**：目前 `appsettings.json` 與 `appsettings.Development.json` 中的 `DynamicsAccess:Package01FeeReadsEnabled` 均正確設為 `false`。在未完成 P7.4 准入條件前，切勿將此值變更為 `true`。

---

## 4. 最小安全下一步本地交付成果 (Smallest Safe Next Local Deliverable)
1. **保持功能閘關閉**：維持 `appsettings.json` 與 `appsettings.Development.json` 中 `DynamicsAccess:Package01FeeReadsEnabled` 為 `false` 的設定。
2. **編寫並驗證排空運行手冊**：在本地或測試環境中，撰寫並實際演練 `drain-first non-overlap runbook`，確保在切換流量至 Gateway/Data8 前，舊有 `ToolUtility` 的所有 active 請求已完全排空且不再接收新流量。
3. **嚴禁僅憑單元測試啟用**：不可僅因 `SqlRuntimeHostSlotCoordinatorTests.cs` 或 `CrossProcessSqlRuntimeHostSlotCoordinatorTests.cs` 等未綁定協調器的單元測試通過，便在生產或本地配置中啟用該功能閘。
