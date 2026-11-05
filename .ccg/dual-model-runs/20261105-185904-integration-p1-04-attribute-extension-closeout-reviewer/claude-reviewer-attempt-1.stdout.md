## Review：integration-p1-04-attribute-extension-closeout（唯讀複核）

### 範圍確認
本輪僅變更：新增 `[InlineData("new_group_present_weekly_report")]` 一筆 Theory 案例與對應中文註解，無任何產品程式（ContactService / ToolUtilityClass / Facade / Core）變更。依指示僅依附件快照（11 案 C# 原始碼、TRX 計數器、35→8 集合對照 JSON）進行複核，未重新讀取倉庫檔案、未執行測試、未再調用外部模型。

### 前輪 Warning 關閉核對
- 前輪缺陷：35 個呼叫點實際涵蓋 8 種 logical name，測試 InlineData 僅記載 7 種，漏 `new_group_present_weekly_report`。
- 本輪附件 `observedNames` 與 `testTheoryNames` 兩陣列逐項比對，8 對 8 完全相同（`exactSetEqual: true`），且測試原始碼中 `LegacyFacade_RetrieveThenAssign_PreservesEveryObservedEntityName` 的 8 筆 `InlineData` 確實含該筆。
- TRX 計數器 `total/executed/passed = 11/11/11`，`failed/error/aborted = 0`，與程式碼中 3 個 `[Fact]` + 8 個 `[InlineData]` 的案例數吻合，無虛報執行數的跡象。
- **判定：前輪 Warning 已實質補證關閉，不再沿用。**

### Critical 🔴
無。

### Warning 🟡
無。本輪新增內容僅擴充既有 Theory 案例的資料驅動參數，未引入新程式路徑、新替身邏輯或新斷言型態，風險面與前輪已通過部分一致。

### Info 🟢
- **測試檔 `OwnerRouteFixture.Dispose()`**（附件程式碼內，非本輪新增）：於 `Dispose()` 內呼叫 `Assert.Equal(0, State.DisposeCount)`，屬「斷言寄生於 Dispose」的脆弱模式——若未來某案例的測試主體先行失敗並拋出例外，`using` 區塊結束時仍會執行此 Dispose 內斷言；兩個例外同時存在時，CLR 只會向外傳播 Dispose 內丟出的新例外，原始失敗原因可能被覆蓋而不易排查。目前 11/11 全數通過，未實際觸發此風險，故不列為 Warning，僅留存記錄供未來若有案例失敗時優先檢查該檔案的實際失敗堆疊是否被此行為遮蔽。
- **複核方法論限制**：35→8 集合相等與測試原始碼雜湊值一致性，係依使用者提供之 JSON 快照內容進行比對，複核本身依指示未重新讀取倉庫中的實際呼叫點原始碼或重算雜湊；此結論建立在快照資料真實性之上，而非複核方自行重新萃取驗證。
- 其餘前輪已確認且本輪未變更之限制（nullable 全域設定、wrapper 相容性、區域查詢配置、借用所有權語義、僅走 ContactService/Facade 真實鏈而非 DI/建構式/授權/實機 D365、SDK 邊界替身不重作驗證、非全 35 業務分支皆執行）維持原結論，不重複列入本輪發現。

### Summary
前輪指出的「7 vs 8 種資料表名」缺口已以具體程式碼變更（新增 InlineData 及集合比對 JSON）補證關閉，11/11 TRX 通過且計數與原始碼案例數一致，未見新引入之 Critical/Warning 問題。**結論：Approve（無待辦阻斷項）**，僅留一筆既有程式碼模式之觀察性 Info 供未來留意，非本輪變更所致、不要求本輪修正。

---
SESSION_ID: 47febe8f-3aa6-46fe-92e2-0eeee434a865
