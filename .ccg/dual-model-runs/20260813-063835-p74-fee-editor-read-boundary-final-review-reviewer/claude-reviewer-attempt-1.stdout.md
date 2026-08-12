## P7.4 Fee-Editor Read-Boundary 最終審查報告

已完整比對 diff、6 項 contract 要求，並實際讀取原始碼確認引用的既有方法（`FeeList.EnsureLoginScope`／`IsLessonListLoadedFor`、`OperationIds.FeesEditorLoadByDiscipleLesson` 註冊、`InMemoryContext.FeeList` 的生命週期）。結論：**本次變更符合全部 6 項 contract 要求，未發現 Critical 或 Warning 等級缺陷**。

### 合約逐項驗證

| # | 要求 | 結果 | 證據 |
|---|---|---|---|
| 1 | 雙閘門於任何 parse/session/I/O 前短路 | ✅ | `FeeManagementController.cs:377-384`：`GetFeeEditorRows` 第一行即 `IsPackage01FeeEditorReadEnabled` 檢查並直接回傳，之後才有 `CurrentLogin()`／`InMemoryContext.FeeList` |
| 2 | browser GUID 僅為 locator，授權來自 server snapshot，無 CRM scan/legacy loader | ✅ | `FeeManagementController.cs:392-413` 先 `EnsureLoginScope`→`TryCreateAuthorizedLessonIds`（`FeeEditorLessonAccessResolver.cs:41-71`，fail-closed on 未載入/null/invalid/duplicate）→才 `Guid.TryParse`→`IsAuthorizedTarget`；未呼叫 `EnsureLessonListLoaded`/`SetupLessonList`/`RetrieveEntity`（`FeeManagementControllerFeeEditorReadContractTests.cs:65-69` 亦以原始碼斷言鎖定） |
| 3 | 固定使用 `fees.editor.load.by.disciplelesson`、server profile、`church-report-service` workload | ✅ | `Package01FeeReadClient.cs:140-157` 固定映射；`OperationIds.cs:67` 與 `Package01OperationRegistry.cs:241-254` 確認該 capability 已在 registry 註冊，非未定義字串；`FeeEditorReadService.cs:35` 常數 workload |
| 4 | 不可變 allowlist scalar DTO，無 CRM Entity/可寫路徑 | ✅ | `FeeEditorReadResult.cs:74-100` 建構時 deep-copy 為 `ReadOnlyCollection`；`FeeEditorReadRow` 僅 scalar 屬性；contract test 斷言不含 `FeeDataList`/`UpdateFeeData`/`SaveBatch`/`new Fee(` |
| 5 | 每列須符合已授權 lesson；null/mismatch/fault 不發佈 partial；`OperationCanceledException` 須逃脫 catch | ✅ | `FeeEditorReadService.cs:91-96` 逐列比對，任一 mismatch/null 立即 `throw`（在 result 建構前）；`FeeManagementController.cs:439` 用 `catch (Exception ex) when (ex is not OperationCanceledException)`，正確排除取消例外 |
| 6 | 僅本機驗證，gate 維持 false，不宣稱 CE/cutover 完成 | ✅ | `appsettings.json`/`appsettings.Development.json` 新增 `Package01FeeEditorReadEnabled: false`；design.md 明確聲明「不開 gate、不發 CE…不構成 P7.5/P8 evidence」 |

### Findings

**Info — Gemini 先前回報的「檔案註解亂碼」應視為誤判，不需依其建議加 BOM**
- 檔案：`SpeechMessageProducts.ChurchReport/Services/FeeEditorLessonAccessResolver.cs`、`FeeEditorReadService.cs`、`SpeechMessageProducts.ChurchReport/Models/FeeEditorReadResult.cs`
- 直接以位元組層級檢查（`xxd`／`file`）三檔皆為合法 UTF-8、**無 BOM**、CRLF，中文註解在 Read 工具中正確渲染。`FeeManagementController.cs` 檔頭本身即聲明專案慣例為「UTF-8 without BOM 與 CRLF」（第 12 行），與現況一致。加 BOM 反而違反專案既有慣例，不應採納該建議。

**Info — `FeeList` 是以 Session 為 key 的共享可變快取物件，非 per-request 隔離，無鎖保護（既有架構風險，非本次引入）**
- 證據：`InMemoryDataContextSmallGroup.cs:1040-1081`（`FeeList` getter 以 `GetCurrentSessionId() + "_FeeList"` 存取共用 `IMemoryCache`）
- 說明：同一瀏覽器 session 若同時觸發多個請求（例如同時呼叫新端點與既有 `GetFeeData`/`LessonList`），理論上可在同一個 `FeeList`/`LessonList` 實例上競爭讀寫。此為既有架構（`EnsureLoginScope`/`IsLessonListLoadedFor` 等既有 action 皆共用同一模式）沿用的既有風險，非本 diff 新增。由於本端點是 fail-closed 設計，競爭的實際後果最多是拋出例外並落入 generic catch 回傳固定 `unavailable` 訊息，不會造成授權繞過或跨使用者資料外洩，因此不違反本次安全合約，僅供留意，不阻擋本 child 交付。

### 隔離／生命週期／rollback 檢查
- **Rollback**：唯一依賴 `Package01FeeEditorReadEnabled` 單一 flag，關閉後即完全零工作，未寫入任何持久狀態，無需額外 cleanup。
- **資源生命週期**：`FeeEditorReadService`/`FeeEditorLessonAccessResolver`/`FeeEditorReadResult` 皆為 request-local、無 `IDisposable`；`Package01FeeReadClient` 僅包裝既有 process-host 持有的 executor，未新建連線池。
- **Disclosure**：所有失敗路徑統一回傳常數訊息 `FeeEditorReadUnavailableMessage`，未 echo 例外訊息、CRM/Gateway 回應或設定值。

### 結論
無 Critical / Warning。上述兩項 Info 僅供紀錄，不需在本 child 範圍內修正。此為 local-only 證據，兩個 gate 皆維持 `false`，不構成 CE、Dedicated、cutover、P7.5 或 P8 完成證明。

---
SESSION_ID: c2bfadb0-0269-442a-a1ce-30729726b5a7
