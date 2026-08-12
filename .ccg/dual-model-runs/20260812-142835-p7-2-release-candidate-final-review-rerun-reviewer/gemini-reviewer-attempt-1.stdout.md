# P7.2 Continuation Release Candidate 最終審查報告

本審查針對目前工作樹中相對於 `HEAD` 的 P7.2 continuation 變更進行安全契約與隔離邊界驗證。

---

## 審查結果摘要

**No Critical findings**

本機測試與隔離驗證均已通過，且變更嚴格遵守安全契約。CE 軌道目前因單次 fresh ExecuteFixture 的 `write-not-committed` no-go 而處於關閉狀態，D–H 繼續維持 local-only，P7.4/P7.5 亦維持 fail-closed。本報告不將本機測試成功視為 CE 實證完成。

---

## 嚴重性排序發現

### Critical
* **No Critical findings**

---

### Warning
* **無**

---

### Info

#### 1. 過渡 API 固定 Fail-Closed 限制
* **檔案／方法**：`SpeechMessageProducts.ChurchReport/Models/ListManager.cs` -> `SetupIntegrateData(String ListEntityId, IOrganizationService organizationService)`
* **可重現條件**：呼叫此新增的 service-aware `ListManager` 重載方法。
* **實際影響**：由於該方法目前缺少完整、不可變且已驗證的 operation context，為了防止 session-cached 的可變狀態污染 borrowed service，目前在讀取任何 instance 狀態或執行 CRM I/O 前會固定拋出 `InvalidOperationException` fail-closed。這符合安全設計，但意指該路徑目前在產品中不可達。
* **修正建議**：維持現狀。未來若要接入產品，必須建立完整、不可變且由伺服器驗證的 operation context，不得以此過渡 overload 偷渡 session state。

#### 2. 下載入口僅支援「小組長」唯讀路徑
* **檔案／方法**：`SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Core.cs` -> `SetupIntegrateData(...)`
* **可重現條件**：傳入非 `"小組長"` 的 `LoginType` 呼叫此方法。
* **實際影響**：為了確保隔離邊界，目前僅支援已完成隔離驗證的「小組長」唯讀路徑。其他登入型態（如個人回報）會直接拋出 `InvalidOperationException` 拒絕，防止其回落至共用的 `ToolUtility`，確保不會發生跨 Session/profile 的 service 混用。
* **修正建議**：維持現狀。未來若要擴充其他登入型態，必須先完成其對應的 operation-local 參數傳遞與隔離驗證。

#### 3. 延遲建立 UploadIntegrateData 確保讀取路徑隔離
* **檔案／方法**：`SpeechMessageProducts.ChurchReport/Models/ListSmallGroupWeeklyReport.cs` -> `m_UploadIntegrateData` 與 `GetUploadIntegrateDataForMutation()`
* **可重現條件**：建構 `ListSmallGroupWeeklyReport` 進行唯讀操作。
* **實際影響**：建構子中移除了 `m_UploadIntegrateData` 的立即初始化，改為在真正執行寫入/刪除操作時才延遲建立。這成功阻止了唯讀路徑意外初始化 legacy connector 並透過其取得共享的 `ToolUtility` 服務，確保了讀取路徑的純粹性與隔離性。
* **修正建議**：維持現狀。此延遲加載設計在維持相容性的同時，有效切斷了讀取路徑的隱式共享依賴。
