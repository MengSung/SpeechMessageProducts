# 雙模型唯讀審查：integration-p1-04-mutation（EntityCrudService / OwnerManagementService 搬移至 Core）

## 審查範圍
搬移前後 `EntityCrudService`、`IEntityCrudService`、`OwnerManagementService`、`IOwnerManagementService` 四檔，以及對應特性化測試、安全測試、跨目標測試。僅依提供內容審查，未執行、未擴讀其他檔案、未修改、未再呼叫模型。

---

## Critical 🔴
無。核心寫入/刪除/指派邏輯在搬移後仍為單次呼叫 SDK、不保存實體/logger、不隱藏重試，且四個新驗證（Create 空名稱、Update 無 ID 無 key、Create 空 ID、AssignOwner 名稱不一致）均有對應測試覆蓋並在 I/O 前短路，沒有發現會造成資料損毀、重複寫入或授權繞過的缺陷。

---

## Warning 🟡

1. **`EntityCrudService.DeleteEntity` 的例外型別未列入已揭露差異清單**（ToolUtility.Core/EntityOperations/EntityCrudService.cs:57, `RequireName`:65）
   - 搬前：`entityName` 為空字串時用 `string.IsNullOrEmpty` 檢查並丟 `ArgumentNullException`；純空白字串（如 `" "`）完全不會被擋，會直接送進 `_organizationService.Delete(" ", id)`。
   - 搬後：改用共用 `RequireName`（`IsNullOrWhiteSpace`）一律丟 `ArgumentException`。
   - 工作說明中列舉的「有意安全差異」只明確提到 Create/Update 的空名稱拒絕與 Create 空 ID、Update alternate key，**沒有單獨提到 Delete 由 `ArgumentNullException` 改為 `ArgumentException`**。雖然 `ArgumentNullException` 是 `ArgumentException` 的子類別，測試中以 `Assert.ThrowsAny<ArgumentException>` 驗證不受影響，但若 host 端或既有呼叫端對 `DeleteEntity` 曾經做過 `catch (ArgumentNullException)` 的型別專一攔截（而非籠統 `catch (ArgumentException)`），行為會改變且不會再被該分支捕捉。
   - 建議：在搬移紀錄中明確補列此差異（純空白字串現在也會被擋、例外型別改變），或確認 29/29 既有呼叫端都沒有對此型別做專一攔截。

2. **`AssignOwner` 新增的 entityName 與 `entity.LogicalName` 一致性檢查屬新行為，未見於既有特性化黃金檔之外的交叉驗證**（ToolUtility.Core/OwnerOperations/OwnerManagementService.cs AssignOwner 區塊）
   - 搬前完全沒有此檢查，呼叫端若曾經（即使非典型）傳入與 `entity.LogicalName` 不同的 `entityName`，過去會被包裝成 `InvalidOperationException` 後才在 SDK 層失敗或甚至成功送出；搬後會在 I/O 前以 `ArgumentException` 直接拒絕。
   - 這是合理的新安全防護（也呼應 22 案中的修復項），但由於「正式 P1 不移業務」，表示呼叫端程式碼本身未被同步檢視。建議在 29/29 迴歸套件之外，額外確認目前所有呼叫 `AssignOwner` 的既有業務程式碼，傳入的 `entityName` 參數都與對應 `Entity.LogicalName` 完全一致（大小寫、拼字），否則上線後會從「可能成功/被包裝例外」變成「直接拒絕」。

---

## Info 🟢

1. **繁中維護註解風格由檔頭區塊改為 XML `<summary>/<remarks>`**（四個檔案全部）
   - 搬前每檔都有標準「AI-繁體中文檔案註解」檔頭區塊（檔案路徑/所屬區塊/主要型別/編碼要求等）；搬後 Core 版本改為方法級 `<summary>`、`<remarks>`、`<param>`、`<returns>`、`<exception>`，內容品質良好且更貼近程式碼位置，但不再有檔頭區塊。若專案其他已搬移檔案（如 Fee/Query 服務）維持同一風格轉換，屬一致性决策而非缺陷；若只有這批檔案缺檔頭，建議確認是否為刻意的 Core 層新慣例。

2. **未包裝例外路徑不僅涵蓋 SDK 例外，也涵蓋屬性型別不符等執行期例外**（如 `GetOwnerId`/`GetOwnerName` 內 `GetAttributeValue<EntityReference>("ownerid")` 若實際型別不符會丟 `InvalidCastException`）
   - 目前設計原則「真正 SDKException 原樣交 host」在實作上其實是「所有例外都不包裝、原樣往外丟」，包括非 SDK 來源的型別轉換例外。這與契約精神一致（不製造含實體/ID/Message 的包裝），但 host 端共用通知器若有針對 `SDKException`/特定型別做分支處理，需確認它對任意 `Exception`（含 `InvalidCastException`、`ArgumentException`）都有合理的 fallback，而不是只預期 SDK 家族例外。

3. **`CreateEntity` 的空 ID 檢測是以 `Guid.Empty` 判斷「狀態不確定」**（EntityCrudService.cs:37）
   - 這一行為已在工作說明中揭露為「有意安全差異」，測試 `Create_EmptySdkIdentity_RejectsOnce` 覆蓋良好。僅提醒：此判斷依賴真實 Dataverse SDK 永不回傳 `Guid.Empty` 的慣例；如未來改用非官方/自訂 `IOrganizationService` 實作且其在特殊情境下合法回傳空 Guid（極不常見），會被誤判為「狀態不確定」而拋例外。此為可接受的保守設計，非缺陷，僅留意前提假設。

---

## 其他檢查結果（通過，無異常）

- **相容性**：`IEntityCrudService`、`IOwnerManagementService` 介面簽章（方法名稱、參數、回傳型別）搬移前後完全不變，二進位/原始碼層的 API 面未破壞。
- **生命週期**：兩個服務皆為無狀態轉接器，僅持有 `IOrganizationService` 參考；legacy logger 參數維持 null 拒絕但不保存（以 `_ = logger ?? throw ...` discard），`LegacyLogger_IsNotRetained` 測試以 `WeakReference` 證明可被 GC 回收；未發現對借入 SDK 呼叫 `Dispose`（`SdkFault_...`、`ConcurrentScopes_...` 測試均以 `Times.Never` 驗證）。
- **跨 scope / 並行**：兩服務除建構時注入的 `_organizationService` 外無任何可變欄位，`ConcurrentScopes_SameDisplayNames_NoCrossTalkAndDrain` 以 1000 次 Create + 1000 次 Assign 跨兩個獨立 scope 驗證無交叉污染、外層 Task 全部 `await` 後計數精確收斂，設計上無跨租戶共享風險。
- **安全**：Create/Update 前置驗證（空名稱、無 ID 且無 key）、AssignOwner 名稱一致性檢查、空 Create ID 拒絕等新防護皆在呼叫 SDK 前以 Strict Mock（`VerifyNoOtherCalls`）證明不會送出部分寫入；真實 SDK 故障（`TimeoutException`）以 `Assert.Same` 證明原樣往外拋、且 `Invocations.Count == 1` 證明沒有隱藏重試。
- **命名空間**：`ToolUtilityNameSpace.EntityOperations` / `OwnerOperations` / `Interfaces` 全部保留原樣，符合「正式產品原 namespace 保持不變」要求。

---

## 結論
未發現 Critical 等級缺陷；兩項 Warning 均屬「行為差異是否已完整揭露/是否需要額外迴歸確認」範疇，而非功能性錯誤，建議在搬移紀錄中補充說明或以現有 29/29 迴歸結果交叉確認後即可視為可接受。整體搬移在正確性、生命週期、並行安全、介面相容性上均符合預期設計意圖。

---
SESSION_ID: 0c6cf7af-2f41-4233-9fca-36dc8c97ae21
