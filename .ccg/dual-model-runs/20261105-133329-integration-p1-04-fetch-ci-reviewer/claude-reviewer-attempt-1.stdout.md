## 審查結果（唯讀快照，未執行任何程式碼）

### Critical
無。

### Warning

1. **審查範圍缺口 — 無法驗證 csproj 是否新增 NoWarn**
   任務明確要求查核「Core net462/net10 strict，不新增NoWarn」，但 `ToolUtility.Core.csproj` 雖在 git status 中列為已修改，內容未包含在本次固定快照內。依規則不可擴讀其他檔案，故此項**無法核實**，建議下一輪把該 csproj diff 納入快照後再結論。

2. **審查範圍缺口 — 新增測試檔與 golden 未提供內容**
   `EntityQueryCharacterizationTests.cs`、`ProductRegistrationTests.cs`、`CoreTargetFrameworkTests.cs`（修改）、`Golden/ToolUtility/entity-task.xml` 均在本輪 diff 中（git status 顯示 A/M），但未列入審查快照。`entity-task.xml` 很可能是 `EntityQueryCharacterizationTests.cs` 的 golden 依據——在看不到該測試內容的情況下，無法判斷斷言是否被放寬或 golden 是否確實對應 `EntityQueryService` 的 FetchXML 輸出。**不得視為已核過**。

3. **「11 失敗→11 通過」「93/93」無執行證據佐證**
   任務敘述提及測試先紅後綠的數字與 artifacts 路徑，但本輪快照未附上實際測試輸出/trx/日誌內容，僅有路徑字串。依規則「無執行證據不得猜通過」，此輪只能確認**程式碼邏輯在靜態閱讀下自洽**，不能確認測試實際執行結果與聲稱數字一致。

### Info

1. **FetchXML 安全邊界（XXE/DTD/字元上限）落實良好**
   `EntityQueryService.ExpectedFetchEntity`：`DtdProcessing.Prohibit` + `XmlResolver = null` + `MaxCharactersInDocument = query.Query.Length` 三者疊加，DOCTYPE（含內部實體，非僅 SYSTEM）一律在 `XmlException` 階段被擋下並轉譯為固定訊息 `ArgumentException`，符合測試案例 `<!DOCTYPE ... SYSTEM 'file:///p1-must-not-read'>` 的紅線要求。根實體辨識只認 `Depth==1 && LocalName=="entity" && NamespaceURI.Length==0`，`link-entity`、帶 namespace 的 `entity` 都正確地不被誤判為根實體。

2. **固定外層錯誤訊息確認不洩漏輸入**
   逐一核對 `ExpectedFetchEntity`、`RequireLogicalName`、`RetrieveEntity`、`ValidatePage` 丟出的例外訊息，均為固定字串，未內嵌 `query.Query`、ID 或 LogicalName 原始值；內層 `XmlException` 作為 `InnerException` 傳遞但不進最外層 `.Message`。

3. **借入 SDK 所有權邊界正確**
   `Dispose(bool)` 只 `Interlocked.Exchange` 清除欄位參照，從未對 `_organizationService` 呼叫 `Dispose()`；對應測試 `Dispose_StopsNewOperationsWithoutDisposingBorrowedClient` 與 `NoCache_ParallelScopes_DoNotShareResultsAndDrain` 皆以 `Verify(..., Times.Never)` 驗證未代呼叫端關閉 SDK，與介面文件聲明一致。`GetService()` 中「先讀 service 參照、後讀 disposed flag」的順序與 `Dispose` 中「先設 disposed、後清參照」順序互補，不存在可用到已清空參照卻放行的競態窗口。

4. **W1（FetchExpression 未驗根實體）與 W2（反向日期未測）已確實落地**
   `RetrieveMultiple`／`ExecuteRetrieveMultiple` 兩個通用入口都經過 `ExpectedEntity`→`ExpectedFetchEntity`，且 `FetchQuery_InvalidRowIdentity_RejectsEntireCandidate`（entity/duplicate/empty 三種故障 × 兩入口）覆蓋完整。`FeeQueryContractTests.DateRange_StartAfterEnd_RejectsBeforeIo` 新增且 `client.VerifyNoOtherCalls()` 確認未觸達 SDK，但底層 `FeeService.RetrieveDedicationFeeByDateRange` 實作本身未在快照內，無法逐行核對判斷式，僅能確認測試本身邏輯嚴謹。

5. **根實體驗證未被誤標為授權，空 ID 拒絕未被誤標為新修復**
   XML doc 註解明確寫「此驗證不取代 host 授權」「真實 SDK 例外原樣上傳」，`ValidatePage` 的空 ID／重複 ID 拒絕邏輯與既有行為一致，未見把舊行為包裝成本輪新功能的誤導性敘述。

6. **CI 排除清單與 Core 覆蓋率矩陣在給定檔案範圍內一致**
   `Test-CiTransition.Tests.ps1` 的 `$known` 固定三項排除（`ChurchReport.MemberInfo.Tests`、`SpeechMessageProducts.ChurchReport`、`ToolUtility.Dataverse.Tests`）與 `SpeechMessageProducts.CI.slnf` 中不含上述三者相符；`Coverage_IncludesBothCoreProductTargets` 以 AST 解析 `$cases` 變數，嚴格要求 `Module -ceq 'ToolUtility.Core'` 恰好 2 筆、`Project` 必為 `ToolUtility.Tests`、`Framework` 須同時含 `net48`/`net10.0`——比對 `solution-ci.yml` 的 `cases` 陣列，兩筆 `ToolUtility.Core` 條目確實存在且框架相符，**未見斷言被放寬**（仍是精確集合比較，非子集或包含式檢查）。

7. **既有查詢 XML 注入防護維持**（`RetrieveTaskBySubject` 用 `XAttribute` 自動轉義、`RetrieveEntityByField`/`RetrieveAccountByName` 用型別化 `QueryByAttribute` 而非拼接字串）——與舊行為相容，未見新引入注入面。

### 結論
本輪提供的兩個核心檔案（`EntityQueryService.cs`／`IEntityQueryService.cs`）與對應契約測試在靜態閱讀下**未發現 Critical 問題**；XML 安全邊界、借入 SDK 所有權、固定外層錯誤訊息、CI 排除與覆蓋率矩陣在審查範圍內自洽。但本輪快照刻意排除的新增測試檔、golden 檔與 `ToolUtility.Core.csproj` 無法核實，加上缺乏實際測試執行輸出，**依規則不得視為 P1-04 完成**，與任務自述「0/12」一致——建議下一輪把上述缺口檔案與雙宿主／完整回歸的實際執行證據一併納入快照後再覆核。

---
SESSION_ID: cf321bc6-930d-4df7-a8fa-2bdb8ee38adf
