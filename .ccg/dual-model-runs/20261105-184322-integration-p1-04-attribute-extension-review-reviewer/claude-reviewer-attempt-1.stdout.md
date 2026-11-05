# P1-04 屬性工具/SDK Extension/Constants 增量 Review

範圍：僅依任務提供之固定快照（`EntityAttributeUtilityService`、`IEntityAttributeUtilityService`、`OrganizationServiceExtensions`、`EntityQueryService`、`CrmEntityColumns` 及其測試/golden/csproj/報告），未讀取、修改或執行任何其他檔案，未讀 Facade／appsettings／憑證，未再呼叫模型。

## Critical
無。逐一核對下列契約，均在現行程式碼中成立，且與給定 TRX（395/395、314/314，0 failed）一致，未發現會造成資料錯誤、安全繞過或崩潰的缺陷：
- `GetAttributeValue`／`RemoveAttribute`／`SetEntityAttributeToNull` 的 null/空白輸入包裝、`InvalidOperationException` + 真實 `InnerException` 保留、固定外層文字不回顯欄位值 — 與 `AttributeExtensionSafetyTests` 逐案核對一致。
- `RemoveAttribute` 移除程式碼由「先 Contains 再 Remove」簡化為直接 `Attributes.Remove(propertyName)`；`AttributeCollection` 底層為 Dictionary 語義，對不存在的鍵 `Remove` 回傳 `false` 不丟例外，行為等價未變。
- `SetEntityAttributeToNull` 由「Contains 則賦值否則 Add」簡化為 `entity.Attributes[propertyName] = null`；Dictionary 索引子賦值本身即是 add-or-update，行為等價未變。
- `OrganizationServiceExtensions.ExecuteRetrieveMultiple` 委派局部 `EntityQueryService`，`using` 僅清除借入參照、不 Dispose 原 SDK，與 `ExtensionSdkFault_PreservesExceptionRetryAndBorrowedOwnership` 測試斷言一致。
- `CrmEntityColumns` 21 組欄位數量、順序與 `columns-all.xml` 完全對應；getter 用 `Clone()`，不共享可變陣列。

## High
無。

## Warning
1. **W2（AssignOwner 35 個 literal caller 核對）仍未以實機證據關閉** — `wp-reports/P1-04-attribute-extension.md` 本身已誠實揭露「主會話靜態稽核不是實機證據」，但本輪補測（`MutationOperationsSafetyTests.cs`）並未新增任何針對 35 個既有呼叫點的整合/契約測試，僅涵蓋合成實體。這代表前輪 mutation review 標記的 W2 缺件**尚未真正關閉**，只是維持原有「已知限制」的揭露狀態。建議：若要關閉此項，需在後續輪次對至少具代表性的實際呼叫路徑（或以 FakeXrmEasy/合成 metadata 驗證 logical-name 一致性）補上可執行證據，而不僅是文字稽核結論。
   - 對照：W1（Delete 空字串／純空白）已由 `MutationOperationsSafetyTests.Delete_NameValidation_PreservesEmptyExceptionAndRejectsWhitespace`（`ToolUtility.Tests/DataOperations/MutationOperationsSafetyTests.cs`）以精確 `ArgumentNullException.ParamName == "entityName"` 及 `ArgumentException`（純空白）分案證明，**已確實關閉**。

## Info
1. `ToolUtility.Core/EntityOperations/EntityQueryService.cs:1` 檔頭仍保留顯式 `#nullable enable`，與任務敘述「Core strict全域啟用Nullable，不必逐檔pragma」的前提不一致（若全域已啟用，此行為多餘/可移除的歷史殘留；若未真正全域啟用，則本檔之外的新檔如 `EntityAttributeUtilityService.cs`／`IEntityAttributeUtilityService.cs` 缺少對應宣告反而可能造成 nullable 語境不一致）。由於 `ToolUtility.Core.csproj` 快照未顯示 `<Nullable>` 設定（可能來自未列入快照的 `Directory.Build.props`），無法在本次唯讀範圍內確認何者為真，僅記錄供後續確認，不構成行為缺陷。
2. `ColumnContractTests.ColumnsSafety_CallerMutation_DoesNotCrossBoundary` 中的 `finally { first[0] = original; }` 還原動作作用在 getter 回傳的獨立複本上，自 `CrmEntityColumns` 改為 `Clone()` 後此語句已是無效果的安全網（非 bug，測試檔註解本身已承認「新版只還原caller獨占複本」），無需動作。

## 結論
本次唯讀 review 未發現 Critical/High 級別缺陷；程式碼內部契約（例外包裝、借用所有權、欄位複本、整頁驗證、nullable 標註）與給定測試及 TRX 證據一致。唯一需要後續追蹤的是 **W2 仍未以實機/整合證據關閉**（Warning），其餘為文件一致性層級的 Info。本 review 僅涵蓋 P1-04 屬性/Extension/Constants 切片，不代表 P1-04 或 P1 全包驗收。

---
SESSION_ID: 08a5f1cf-ae8c-4a2e-8bd7-01ca1c2e5f48
