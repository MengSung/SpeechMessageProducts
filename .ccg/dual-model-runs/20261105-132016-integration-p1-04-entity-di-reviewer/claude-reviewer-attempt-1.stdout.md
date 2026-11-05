# P1-04 Fee / Entity 切片複審結果（本輪增補）

本輪只讀比對本提示附帶的 Core 原始碼、host 委派 diff、測試與 golden 快照；未擴讀其他舊來源或執行程式。結論：**沒有發現 Critical 或 High 等級問題**，與前次 run（20261105-130623，dualModelComplete=true）結論一致，但發現兩個值得留意的 Warning 與測試盲點。

## Critical
無。

## High
無。

## Warning

**W1. `EntityQueryService.RetrieveMultiple` / `ExecuteRetrieveMultiple` 對 `FetchExpression` 查詢略過 LogicalName 驗證，且完全沒有對應測試**
- 來源：`ToolUtility.Core/EntityOperations/EntityQueryService.cs` 的 `ExpectedEntity(QueryBase query)`（僅對 `QueryExpression`/`QueryByAttribute` 回傳實體名稱，`FetchExpression` 回傳 `null`）與 `ValidatePage`（`entityName == null` 時跳過 `row.LogicalName` 比對，只驗非空與不重複 ID）。
- 失敗路徑：若呼叫端（例如既有 `FetchXmlQueryService` 類型未來改走 `RetrieveMultiple(QueryBase)`/`ExecuteRetrieveMultiple` 這兩個通用入口並傳入 `FetchExpression`）取得的 SDK 回應混入非預期實體列（ID 非空、不重複，但 `LogicalName` 不符），該頁仍會被判定合法並發布——與 `FeeService.Query`（任何查詢都強制比對 `row.LogicalName == entityName`）行為不一致，削弱「ID 完整發布」的一致性保證。
- 修正建議：`ExecuteRetrieveMultiple`/`RetrieveMultiple` 對 `FetchExpression` 情境至少記錄/要求呼叫端一併傳入期望的 entityName（或維持現狀但在文件/介面註解明確標示此限制，避免未來誤用為「全面型別化驗證」）。
- 測試建議：`EntityQueryContractTests.cs` 中 `MultiQuery_DuplicateId_RejectsEntireCandidate` 與 `MultiQuery_StableIds_AreRequiredAndSameNamesArePreserved` 都只用 `QueryByAttribute` 走 `Run(service, execute)`，完全沒有以 `FetchExpression` 呼叫 `RetrieveMultiple`/`ExecuteRetrieveMultiple` 並注入「跨實體混列」的案例——這是真正的測試盲點，應補一組 `FetchExpression` 版本的重複/跨實體注入測試，確認目前「只驗 ID」的降級行為是刻意接受的風險，而不是遺漏。

**W2. `RetrieveDedicationFeeByDateRange` 的日期區間防呆分支無對應測試**
- 來源：`ToolUtility.Core/FeeOperations/FeeService.cs` → `RetrieveDedicationFeeByDateRange`：`if (startDate.Date > endDate.Date) throw new ArgumentException("Invalid date range.", nameof(endDate));`
- 失敗路徑：目前附帶的 `FeeCharacterizationTests.cs` 與 `FeeQueryContractTests.cs` 都只用固定合法區間（`2027-1-2` ~ `2027-3-4`）呼叫此方法，沒有任何案例傳入 `startDate > endDate` 來驗證該 `ArgumentException` 真的被擲出、且在擲出前不碰 SDK。宣稱的 95.60%/100% 覆蓋率是「搬移前」基準，不能證明這個分支已被行使。
- 修正建議：不需改程式碼，純粹補測試。
- 測試建議：新增 `[Fact] DateRange_StartAfterEnd_RejectsBeforeIo`，仿照 `LessonQuery_InvalidId_RejectsBeforeIo` 的模式，用 `Mock<IOrganizationService>(MockBehavior.Strict)` + `client.VerifyNoOtherCalls()` 確認拒絕發生在 I/O 之前。

## Info

**I1. P1 驗收狀態聲明（非程式碼缺陷）**
本輪僅為原始碼靜態比對（XML 結構、ID 驗證、Dispose/生命週期、DI 冪等性、文化不敏感日期格式化等），全部基於任務提供的固定快照與固定基準 diff。按任務說明，全階段驗收仍為 0/12，且本審查**不構成**實機或完整 P1 通過的證據；Entity 覆蓋率重跑結果本輪未提供，無法對 EntityQueryService/DI 部分做覆蓋率背書。

**I2. 已確認無新增問題的項目（供記錄）**
- `FeeService` 兩個原空方法（`RetrieveDedicationBooking`、`RetrieveFee`）已確實改為呼叫與固定基準 diff 一致的既有有效查詢路徑，欄位/排序/上限/條件順序均與對應 golden XML（`query-dedication-booking.xml`、`query-fee-by-booking.xml`）逐一核對一致。
- 三個新增方法（`GetPaidFee`、`GetNotYetPaidFee`、`RetrieveFeeByFetchXmlOrderNumber`）欄位、條件、排序與唯讀舊來源逐行核對一致，且全部改用 `XElement`/`XAttribute` 建構，不存在字串拼接 XML 注入風險。
- `FeeService.Query` 的整頁 ID 驗證（非空、不重複、LogicalName 相符）正確且執行緒安全（無共用可變狀態）；`EntityQueryService.Dispose` 的 `Interlocked`/`Volatile` 競態處理在所給測試情境下邏輯自洽，不會 Dispose 借入的 SDK。
- `ServiceCollectionExtensions.RegisterServices` 的衝突/冪等判斷（`ReferenceEquals` 比對 configuration、product 字串比對）邏輯正確，沒有看到會讓衝突註冊靜默通過的路徑。
- `RetrieveDedicationFeeByDateRange` 刻意改用 `CultureInfo.InvariantCulture` 格式化日期（修正舊來源依賴 ambient culture 的潛在跨文化 bug），屬已記錄的安全差異，非新缺陷；`new XAttribute("top", top.Value)` 等數值型屬性走 XLinq 內建不變文化轉換，沒有類似的隱藏文化風險。

## 總結
本輪未發現 Critical/High 等級缺陷，程式碼與既有行為（golden XML、ID 完整性、XML 安全編碼、SDK 例外原樣傳遞、不建立 cache/背景工作、不 Dispose 借入服務）保持一致。建議在合併前補齊 **W1**（FetchExpression 混實體注入測試或明確文件化限制）與 **W2**（日期區間防呆分支測試）兩項測試盲點；兩者都不阻塞但會讓既有的覆蓋率數字更站得住腳。本審查仍只是切片程式碼比對，不可引用為 P1 全階段（0/12）或實機驗證通過的證據。

---
SESSION_ID: 73966f9a-8263-4497-86f0-17d2b773f937
