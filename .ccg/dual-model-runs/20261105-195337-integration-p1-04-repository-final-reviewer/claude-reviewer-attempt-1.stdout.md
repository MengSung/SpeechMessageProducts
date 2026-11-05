# P1-04 Repository 增量末端審查（唯讀）

## W1 / W2 關閉狀態確認

**W1（聚合投影無正式ID須明載）— 已關閉**
`EntityRepository.RetrieveMultiple` 與 `EntityQueryService.RetrieveMultiple` 的 XML 文件皆已新增明文：「本入口只發布具正式資料庫 ID 的記錄，不支援欠缺記錄 ID 的聚合投影，也不為其補造識別」。且新增 `RepositorySafetyTests.AggregateProjectionWithoutRecordIdentity_RejectsWithoutInventingId` 以合成 `AliasedValue` count 列（`Id == Guid.Empty`）具體化此契約，驗證拒絕、不補造 ID、不修改原候選、`sdk.VerifyNoOtherCalls()`。文件與測試一致，**W1 確認關閉**。

**W2（PageInfo=null 原地補值須明載）— 已關閉**
兩檔案文件皆新增「此預設會直接寫入 caller 擁有的 QueryExpression.PageInfo；呼叫中不得共用可變查詢」。既有 `FailureRetryAndCallerMutation_UseFreshCandidatesAndBorrowSdk` 已斷言 `query.PageInfo.Count == 5000 / PageNumber == 1` 證明原地寫入行為與文件相符。**W2 確認關閉**。

兩項均為文件補強＋既有/新測試具體化，未更動行為、型別、ParamName 或 InnerException 實例，符合「相容決策不反覆要求改架構」的審查邊界。

## 本輪來源檢查結果

### Critical
（無）

### High
（無）

### Warning
（無新缺陷）

### Info

1. **`EntityRepository.cs` AssignOwner 文件歸屬可更精確**（檔案：`ToolUtility.Core/EntityOperations/EntityRepository.cs`，方法 `AssignOwner`）
   方法 XML 文件寫「新增目標表名一致與非空正式 ID 防禦」，但本方法程式碼本身只顯式檢查 `newOwnerId == Guid.Empty`；表名一致性與 `entity.Id` 非空防禦依測試（`assign-name`、`assign-id` 案例）實際落在 `_owners.AssignOwner`（`OwnerManagementService`，本輪未提供原始碼、依指示不探索）內完成。行為經 12 案測試（含 `AdapterInvalidInput_RejectsWithoutSdk`）驗證為正確且在 SDK 前拒絕，純屬文件歸屬描述可更精確到委派層，非邏輯缺陷，不影響契約正確性。

2. **`EntityRepository.cs` 未見顯式 `#nullable enable`**，與同次異動的 `EntityQueryService.cs`（有該指示）風格不一致。由於 Core 專案已於專案層級統一啟用 Nullable（`CoreInterfaces_EmitNonObliviousStringContracts` 測試對 `IEntityRepository` 等介面的字串參數 NotNull 中繼資料驗證通過），此屬風格一致性觀察，不影響編譯中繼資料或執行行為，無需修正。

## 證據覆核摘要

- 16 份 TRX、2167/2167 全綠、0 失敗/略過，與回報一致；`ToolUtility.Tests` net48 363/363、net10 457/457 與提供摘要逐筆相符。
- `EntityRepository`/`EntityQueryService` 六入口＋新 overload 的驗證順序、ParamName、InnerException 型別、固定英文外層訊息，經 `RepositoryCharacterizationTests`、`RepositorySafetyTests`、`RepositoryCallerRouteTests`、`CoreTargetFrameworkTests` 交叉比對後與原始碼完全一致，未發現斷言與實作不符之處。
- 安全差異清單（空/重複/跨表 ID、名稱非空防禦、Fetch 根表單一性與 DTD 禁止、固定外層訊息、移除 Trace）在原始碼與測試中均可逐項對應，未發現遺漏或誇大宣稱。
- `ValidatePage`、`ExpectedFetchEntity`（含 `MaxCharactersInDocument`、`DtdProcessing.Prohibit`、`XmlResolver = null`）未發現新引入的注入或資源外洩路徑。
- 未發現本輪新增程式碼有違反「不補造 ID／不去重／不部分發布／不新增 Dispose 契約／不建立 cache」等既定邊界的情形。

## 總結

本輪最新來源中 **W1、W2 均已透過文件明載＋對應測試具體化關閉，未發現新的 Critical／High／Warning 缺陷**，僅有 2 項 Info 級、不影響正確性的文件歸屬與風格一致性觀察，供參考即可，不要求修正。審查範圍僅限提供之 Repository/QueryService 原始碼、測試與 wp-report，未觸及 Facade、設定、秘密或父工作樹；P1 整包進度仍為 2/12，本輪判定不代表整包完成。

---
SESSION_ID: 56a88b8f-dbf6-4f2d-b841-0b1363fc6094
