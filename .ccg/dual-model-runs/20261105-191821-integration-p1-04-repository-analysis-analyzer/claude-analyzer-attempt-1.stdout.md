## Analysis: EntityRepository → Core 雙模型唯讀分析（P1-04 切片）

###現狀摘要
- Host `EntityRepository`（介面＋實作同檔）6 方法皆在 try/catch 前做好自身 null/空白/Guid 檢查，再呼叫 SDK，catch 區塊無差別把任何例外包成固定中文訊息的 `InvalidOperationException`，且 Trace 會寫入 `ex.Message`。
- Core 已有 `EntityCrudService`、`OwnerManagementService`、`EntityQueryService` 三個轉接器，但它們各自在方法內部又加了一層「host 從未做過」的驗證（LogicalName 必填、entityName 與 entity.LogicalName 一致、entity.Id 非空、整頁重複/空 ID 拒絕、Create 空 ID 拒絕）。
- `EntityQueryService.RetrieveEntity` 目前只有 2 參數版本，固定用 `ColumnSet(true)`，尚未有 3 參數可控欄位版本。

---

### Critical（阻斷性，必須在實作前定案）

**C1｜host 的 try/catch 會把 adapter 內部的 ArgumentException 誤吞成 InvalidOperationException**
若直接在 host 既有 `try { ... _organizationService... } catch (Exception ex) { throw new InvalidOperationException(...) }` 的 try 區塊裡呼叫 Core adapter（`EntityCrudService.CreateEntity`／`UpdateEntity`／`DeleteEntity`、`OwnerManagementService.AssignOwner`），adapter 內新增的驗證（如 LogicalName 必填、entityName==LogicalName、entity.Id 非空）會丟出 `ArgumentException`，但會被 host 現有的 `catch (Exception ex)` 攔截並重新包裝成「固定安全外層訊息」的 `InvalidOperationException`。
- 失敗情境：caller 原本可以 `catch (ArgumentException)` 分辨「輸入不合法」與「SDK 真故障」，遷移後兩者都變成同一種 `InvalidOperationException`，且訊息不再是驗證失敗原因，而是「建立/更新/刪除/指派失敗」的 SDK 風格訊息。
- 要求：host 呼叫 adapter 時，catch 區塊必須明確先 `catch (ArgumentException) { throw; }`（或等效方式）再 `catch (Exception ex)` 包裝，確保「真 SDK 故障才用 InvalidOperationException wrapper」這條任務鐵律不被破壞。

**C2｜`OwnerManagementService.AssignOwner` 新增的 `entityName == entity.LogicalName` 強制一致性檢查，不在任務授權的「有意安全差異」清單內**
Host 現有 `AssignOwner` 從未比對 `entityName` 與 `entity.LogicalName`，`AssignRequest.Target` 直接用傳入的 `entityName` + `entity.Id`。若某呼叫端（`QueryService`／`MarketingListService`）目前傳入的 `entity` 物件 LogicalName 為空或與 `entityName` 不同（例如僅用 `new Entity { Id = x }` 當作參照載體），搬移後會立刻在呼叫 Core adapter 時丟 `ArgumentException`，造成現有可運作的呼叫端直接失敗（fail-closed，但未被任務授權）。
- 要求：在 git mv 前，必須用特性測試打一次所有現有呼叫端實際傳入的 entity 是否都滿足 `entityName == entity.LogicalName`；若不保證成立，host 必須自行吸收/放寬（例如只用 entityName 建構 `EntityReference`，不要求與 entity.LogicalName 比對一致），而不能讓這個新規則透過重用悄悄生效。

**C3｜`OwnerManagementService.AssignOwner` 新增 `entity.Id == Guid.Empty` 檢查，同樣不在授權清單**
Host 原本若 `entity.Id` 為空，會讓 SDK 的 `Execute(AssignRequest)` 去處理（最終落入 host 自己的 catch → 包裝成 InvalidOperationException）。Core adapter 改成在呼叫 SDK 前就丟 `ArgumentException`。這跟 C1 一樣會被 host catch 誤吞，且跟 C2 一樣是未授權的新 fail-closed。需要跟 C2 一起決策、一起測。

**C4｜`EntityCrudService.UpdateEntity` 允許「純替代索引鍵（Id 為空但 KeyAttributes 非空）」更新，違反任務明定的「Update 仍保留舊規則 ID 必非空，不擴成 alternate-key-only」**
Host 現行 `Update` 對 `entity.Id == Guid.Empty` 一律 `ArgumentException`，不管有無 KeyAttributes。`EntityCrudService.UpdateEntity` 的規則是 `Id 為空 且 KeyAttributes.Count == 0` 才丟例外，等於放寬成「有替代鍵就放行」。
- 要求：host 的 `Update` 必須保留**自己** `entity.Id == Guid.Empty` 就直接拒絕的既有檢查，放在呼叫 adapter **之前**；不可讓呼叫路徑繞過此檢查直通 adapter 較寬鬆的規則。這是明確禁止「無聲放寬」的紅線，需要一個「alternate-key-only 仍須失敗」的紅測來鎖住。

**C5｜`EntityCrudService.CreateEntity` 新增 `LogicalName` 必填檢查，Host 現行 `Create` 完全沒有此檢查**
若呼叫端曾經（即使是邊界情境）用空 `LogicalName` 的 Entity 呼叫 `Create`，原本會直接把失敗丟給 SDK（再被 host 包裝），遷移後會在 SDK 呼叫前被 adapter 擋下。需先跑特性測試確認舊行為（是否真的能讓空 LogicalName 的 Entity 流到 SDK），再決定這個差異是否可接受；若接受，必須明確記錄為「新增 fail-closed」而非預設沿用。

---

### Warning（需要處理，但不阻斷設計方向）

**W1｜Trace 中的 `ex.Message` 洩漏，任務已明示不是相容義務，建議直接移除**
現行每個方法的 catch 都寫 `Trace.WriteLine($"... failed: {ex.Message}")`。任務原文「不要把舊 unsafe Trace 保留當相容義務」等於授權在搬移時順手修掉這段洩漏（可能含 SDK 回應細節/個資）。建議：搬移時把 `ex.Message` 從 Trace 行移除，只保留操作名稱與 ID（不含錯誤內容），不要求整段 Trace 功能本身消失（那是 Trace 機制存廢的另一個議題，不在本切片範圍）。

**W2｜`EntityQueryService.RetrieveMultiple` 對 `FetchExpression` 新增 XML 根實體解析（`ExpectedFetchEntity`），是 host 原本 `RetrieveMultiple` 從未做的前置驗證**
如果 `QueryService`／`MarketingListService` 目前傳入的 FetchXML 有任何 `ExpectedFetchEntity` 無法接受的結構（例如非預期的根層多個 `entity` 節點、具名空間的 fetch 根），遷移後會在送 SDK 前被擋下，而非讓 SDK 自己決定成功或失敗。建議：在 git mv 前，用現有呼叫端實際產生的 FetchXML/QueryExpression 跑一次特性測試，確認都能通過 `ExpectedEntity` 檢查。

**W3｜`RetrieveMultiple`／`ExecuteRetrieveMultiple` 整頁 `ValidatePage`（空 ID / 重複 ID 拒絕）是任務授權的差異，但目前沒有針對「同名不同 ID 不可被誤判為重複」的既有回歸測試**
雖然 `ValidatePage` 用 `HashSet<Guid>` 只比對 ID、不比對名稱，理論上同名不同 ID 不會互相誤殺，但仍建議補一個 golden 測試明確鎖住這個行為，因為這是任務特別點名要保護的語意（"不同ID同名保留,不去重不部分回傳"）。

**W4｜3 參數 `RetrieveEntity` 的 ColumnSet 身分一致性**
新 overload 必須把 caller 傳入的 `ColumnSet` 實例原樣遞交給 `service.Retrieve(...)`，不可中途重建或複製欄位清單。建議在實作完成後加一個 mock 層級的「引用相等」斷言測試（例如 mock `IOrganizationService.Retrieve` 的 `It.Is<ColumnSet>(cs => ReferenceEquals(cs, expected))`），避免未來不小心在轉接層重新包一層 ColumnSet。

---

### Info（確認無虞 / 設計已符合要求）

- **I1** 兩個 adapter 共用同一個 local 非 null marker（如 `new object()`）滿足舊 logger 建構式，且不保存——符合要求，marker 本身無狀態、無需跨實例共享顧慮，但也沒有理由不共享（它永遠只被 null 檢查，不被讀取）。
- **I2** `EntityQueryService` 採「每次操作局部建立 + `using`」而非 Repository 欄位長期持有，是正確選擇：若改成欄位持有，等於讓 `EntityRepository`（目前無 `IDisposable` 契約、既有消費者不會 Dispose 它）間接持有一個需要清理的物件生命週期，違反「避免 Repo 新增未被既有 host 消費者 Dispose 的生命週期」；而 pool/static 又被任務明文禁止。每次呼叫配置一個小物件、呼叫結束前 `using` 收尾，是在禁止 pool 前提下成本最小且不複製邏輯的方案，不需要再找更小的替代方案。
- **I3** `RetrieveMultiple` 對 `QueryExpression.PageInfo == null` 補 `Count=5000,PageNumber=1` 的行為，與 Dataverse SDK 本身未分頁時的預設上限相近，對現有呼叫端通常無可觀察差異；仍建議補一個特性測試作為留痕，而非視為高風險項。
- **I4** `RetrieveEntity`／`RetrieveEntityNoCache` 的身分驗證（`candidate.Id == entityId && candidate.LogicalName == entityName`）不受 ColumnSet 欄位子集影響，因為 Dataverse 無論請求哪些欄位都會回傳 Id/LogicalName，3 參數 overload 重用此驗證邏輯是安全的。
- **I5** Delete/Retrieve 對 entityName/entityId 的 null/空白/Guid 檢查，只要 host 保留自己原有檢查順序與例外類型在 adapter 呼叫之前（任務已明定），adapter 內部重複的同類檢查只是防禦性冗餘，不會造成外部可觀察差異。

---

### 建議實作步驟（依序）

1. **先在原 host 檔案跑「特性測試」**（不改行為），針對以下邊界先錄下現況，作為紅綠基準：
   - Create：LogicalName 為空、SDK 回傳 `Guid.Empty`
   - Update：Id 為空但有 KeyAttributes（alternate-key-only）
   - AssignOwner：entityName 與 entity.LogicalName 不一致、entity.Id 為空
   - RetrieveMultiple：目前呼叫端實際的 FetchXML/QueryExpression 樣本（取自 `QueryService`/`MarketingListService` 既有呼叫，不讀 Facade/secrets，只讀查詢結構本身）
   - 真實/模擬 SDK 故障時，InnerException 是否為同一個 instance、外層訊息格式
2. **新增 `EntityQueryService` 的公開 3 參數 `RetrieveEntity(entityName, entityId, ColumnSet? columnSet)`**，2 參數版本改為委派（`columnSet ?? new ColumnSet(true)` 對齊 host 既有 `??=` 語意），不用 InternalsVisibleTo。此步驟可獨立先在 Core 完成並讓 host 專案引用驗證可編譯/可呼叫。
3. **在 host 實作中，把 catch 區塊改為「先 rethrow ArgumentException/ArgumentNullException，再包裝其餘例外」**（對應 C1），這是所有方法共用的結構性修正，必須先做，否則後面任何 adapter 重用都會被污染。
4. **逐方法接線到 adapter**，同時刻意保留 host 自身既有引數檢查（對應 C4 的 Update Id 檢查、既有 entityName/Guid 檢查）在呼叫 adapter 之前不變。
5. **對 C2/C3（AssignOwner 新規則）先用步驟 1 的特性測試結果決定**：若現有呼叫端都滿足一致性與非空 Id，才允許重用 `OwnerManagementService.AssignOwner`；否則需在 host 端自行用原邏輯組 `AssignRequest`，不透過 adapter 的強化檢查。
6. **紅→綠**：針對 C1～C5、W1～W4 每一項各補一個聚焦測試（特性測試已有基準者直接轉綠測；新行為需明確標註為「已授權的安全差異」或「待決」）。
7. **測試全綠後才 `git mv` 整檔**到 Core，過程中不應再變更公開 `IEntityRepository` 介面signature。

### 待決問題（需要你/團隊明確拍板，非我可單方面假設）
- AssignOwner 的 entityName/LogicalName 一致性檢查（C2）與 entity.Id 非空檢查（C3），是否要納入本切片的「已授權安全差異」清單？目前任務原文只列了 Create/Update/RetrieveMultiple 三類差異，未提到 AssignOwner，需要你確認是否同意新增，或要求 host 端吸收掉這兩個新規則以維持原寬鬆行為。

---
SESSION_ID: 7fa96fdd-cfbf-4042-8c8a-11a3f9133089
