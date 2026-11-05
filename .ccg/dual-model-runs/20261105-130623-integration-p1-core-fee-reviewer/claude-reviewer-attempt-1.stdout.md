審查完成，以下為唯讀審查結果（僅依附帶程式碼與固定基準diff分析，未擴讀其他檔案，未執行程式、未連D365）。整體正確性、XML安全編碼、ID完整性、生命週期（不Dispose/不跨租戶singleton/無cache）等核心要求均有對應實作與測試覆蓋，但發現數項需要人工確認的風險點，分類為 Warning / Info（未發現足以判定 Critical 或 High 的證據）。

## Warning 🟡

1. **三個新增查詢（GetPaidFee／GetNotYetPaidFee／RetrieveFeeByFetchXmlOrderNumber）的 golden XML 與 `100000005` 缺口無法獨立驗證回舊來源**
   - 位置：`FeeService.cs` 的 `CreateLessonFeeFetch`、`RetrieveFeeByFetchXmlOrderNumber`；`RetrieveDedicationFeeByDateRange` 中 `new[] { "100000001", "100000002", "100000003", "100000004", "100000006" }`（跳過 `100000005`）。
   - 失敗路徑：本次附帶內容只包含「委派既有查詢」的固定基準diff（Booking/Fee 兩個空方法），**沒有附帶三個新查詢與日期白名單對應的舊來源 diff**。目前的測試（`fee-paid.xml`/`fee-unpaid.xml`/`fee-order.xml`/`fee-dedication-date-range.xml`）只能證明「程式碼與同次提交的 golden 檔一致」，不能證明「golden 檔忠實還原了舊來源」——若 golden 是與程式碼同時手寫的，review 無法偵測到人為謄寫缺漏（例如 `100000005` 若在舊來源其實存在，會造成合法付款狀態被意外排除）。
   - 修正建議：請附上這三個查詢與日期白名單在舊來源中的原始片段（或獨立 diff），讓審查能核對 golden 檔案是否逐條還原，而非自我循環驗證。

2. **`RetrieveFee` 新增 `RequireText(paidPeriod, ...)` 改變了已被 host 委派、原本可運作路徑的行為**
   - 位置：`FeeService.cs` → `RetrieveFee(string dedicationBookingName, string dedicationBookingId, string paidPeriod)` 開頭的 `RequireText(paidPeriod, nameof(paidPeriod));`
   - 失敗路徑：依固定基準 diff，`FetchXmlQueryService.RetrieveFeeByFetchXml` 原實作直接把 `paidPeriod` 字串插入 FetchXml（`value='{paidPeriod}'`），**沒有做空白拒絕**；若 host 任何既有呼叫路徑曾傳入空白/空字串 `paidPeriod`（即便只是回傳空集合的冷路徑），搬移後會從「靜默回傳空結果」變成「丟出 `ArgumentException`」，造成既有呼叫端未預期的例外（相容性風險，而非新查詢的安全強化）。
   - 修正建議：確認 host 端所有呼叫點都保證 `paidPeriod` 非空；若無法保證，建議在 commit message / PR 說明中明確記載此為刻意行為變更，並補一筆「空白 paidPeriod 相容性」特性測試以留痕。`RetrieveFeeByFetchXmlOrderNumber` 的 `RequireText(orderNumber, ...)` 屬同一類行為變更，但因它是全新方法（無既有呼叫端），風險較低。

3. **三個新增查詢是否已在 host（ToolUtility）完成委派整合，證據中未見**
   - 位置：附帶的固定基準 diff 只涵蓋 `FetchXmlQueryService.RetrieveDedicationBookingByFetchXml` / `RetrieveFeeByFetchXml` 兩個委派點；`GetPaidFee`/`GetNotYetPaidFee`/`RetrieveFeeByFetchXmlOrderNumber` 沒有對應的 host 委派 diff。
   - 失敗路徑：若 host 端（ToolUtility 專案）仍保留這三個查詢的舊有獨立實作（未改為委派 `FeeService`），將形成雙重維護路徑——Core 做了安全修復與ID驗證，但 host 實際執行的仍是舊的、未修復的字串拼接版本，使本次修復在生產路徑上「名存實亡」。
   - 修正建議：請確認 host facade 是否也已改為委派這三個方法至 `ToolUtilityNameSpace.FeeOperations.FeeService`，並提供對應 diff。

## Info 🟢

4. **`ToolUtility.Core.csproj` 未顯示 `LangVersion` 設定，但 `FeeService.cs` 使用了 `object? logger`（nullable 參考型別語法）**
   - 位置：`FeeService.cs` 第二個建構子 `FeeService(object? logger, IOrganizationService organizationService)`；`ToolUtility.Core.csproj` 沒有 `<Nullable>` 或 `<LangVersion>` 節點。
   - 說明：`ToolUtility.Tests.csproj` 明確設了 `<LangVersion>latest</LangVersion>` 才使用 `stream!`/`actual!` 等語法；Core 專案若未透過集中設定（如未附帶的 Directory.Build.props）取得等同設定，net462 目標在部分 SDK 下預設 LangVersion 為 7.3，`object?` 語法將編譯失敗或產生 CS8632 警告。證據宣稱「Core兩目標 build 0警告0錯誤」，若此為真應已有集中設定涵蓋，僅提醒：若日後 Core 專案被抽離集中設定（例如搬到獨立 repo），此建構子會成為隱性地雷。
   - 建議：在 PR 說明中註明集中 LangVersion 設定的來源檔案，便於日後稽核。

5. **新增三個查詢缺少 `Assert.Same(expected, result)` 的分頁保留斷言**
   - 位置：`FeeQueryContractTests.cs` → `NewQuery_MatchesGoldenAndPreservesSameNameRows`（Paid/Unpaid/Order）。
   - 說明：既有 `FormerPlaceholder_UsesEstablishedQuery`（Booking/Fee）與 `FeeCharacterizationTests` 均用 `Assert.Same(expected, result)` 以參照相等間接證明 `MoreRecords`/`PagingCookie` 未被複製或剝除；三個新查詢的測試只驗證筆數與欄位值，沒有同等的參照相等斷言。目前 `Query()` 實作對三者皆直接 `return candidate;`，行為正確，但這是本次審查聚焦的「分頁」要求裡唯一缺測試釘住的角落——未來若 `Query()` 被改成包裝/複製集合，這三個測試不會抓到分頁資訊遺失。
   - 建議：補上 `Assert.Same(expected, result)` 於 Paid/Unpaid/Order 三案，與既有方法測試對齊覆蓋水準。

6. **`Query()` 中 `row == null` 分支未見對應測試**
   - 位置：`FeeService.cs` → `Query` 方法內 `if (row == null || row.Id == Guid.Empty || ...)`。
   - 說明：實務上 SDK 不會回傳含 `null` Entity 的集合，此分支多為防禦性程式；目前所有「重複/缺ID/實體不符」測試都只覆蓋後三個條件，`row == null` 沒有特性測試覆蓋到，屬低風險的測試盲點，非功能缺陷。

## 總結
本次切片在 XML 安全編碼（XAttribute 自動編碼）、ID 完整性驗證（空ID/實體不符/重複ID 整頁拒絕、合法同名不同ID保留）、SDK 借入生命週期（不Dispose、不跨租戶、無cache/背景工作）、例外原樣傳遞等核心要求上，程式碼與測試證據相互印證、邏輯自洽。未發現可判定為 Critical/High 的缺陷。主要風險集中在：**(a)** 三個新查詢與日期白名單是否忠實還原舊來源，審查範圍內無法獨立核實；**(b)** `RetrieveFee` 的空白 `paidPeriod` 拒絕是對既有已委派路徑的行為變更；**(c)** 三個新查詢是否已在 host 完成委派整合缺乏證據。以上均為「需人工/以舊來源核對確認」的 Warning，不代表功能已驗證可上線，亦非實機測試結果。

---
SESSION_ID: 1abcc249-f4bf-486a-aa44-d38d9dcb1036
