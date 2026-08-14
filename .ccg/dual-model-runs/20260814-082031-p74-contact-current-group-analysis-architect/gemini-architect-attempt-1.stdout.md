# 1. Analysis (分析)

我們對 `SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs` 中的 `GetContactCurrentGroup` 及其呼叫者 `AddContactToListAsync` 進行了源邊界分析，結果如下：

1. **授權機制 (Authorization)**：
   在 `AddContactToListAsync` 中，系統首先透過 `GetLoginContactAsync(accountPasswordData)` 取得登入者實體 `loginContact`。然而，隨後呼叫 `GetContactCurrentGroup(existingContact)` 時，僅傳入了 `existingContact` 實體，並未將登入者身分或任何 request-local 授權上下文傳遞至查詢中。這意味著 `GetContactCurrentGroup` 內部呼叫 `_toolUtility.QueryListOfContactManyToMany(contact.Id)` 時，執行的是系統級別的全域查詢，缺乏 request-local 授權隔離。

2. **查詢基數與確定性 (Query Cardinality & Determinism)**：
   `QueryListOfContactManyToMany` 查詢聯絡人所屬的群組時，沒有指定任何排序欄位（`Order By`）。而 `GetContactCurrentGroup` 採用 First-match 行為（遍歷並返回第一個 `new_app_named = true` 的群組）。在多群組關聯的情況下，這會導致非確定性（Non-deterministic）的結果，違反了獨立邊界的確定性語義。

3. **寫入鄰接 (Write Adjacency)**：
   `AddContactToListAsync` 根據 `GetContactCurrentGroup` 的結果，會執行 `AddContactToNewListAsync` 或 `TransferContactBetweenListsAsync`。這些後續操作包含大量的寫入行為（如 `AddContactToListAsync`、`RemoveContactFromListAsync`、`CreatePresentRecordAsync`、`UpdateEntity`、`AssignOwner`）以及外部副作用（LINE 通知）。讀取與寫入之間存在極強的因果關係與事務邊界，無法安全地進行部分讀取切換 (partial read cutover)。

---

# 2. Architecture Decision (架構決策)

**DECISION**: `SOURCE_ONLY_LOCAL_DESIGN_NO_GO`

### Rationale (理由)
1. **強寫入鄰接 (Strong Write Adjacency)**：讀取結果直接決定了後續的寫入分支與外部通知。若將讀取拆分為獨立 Gateway 邊界，將導致嚴重的分散式交易一致性問題與競態條件。
2. **非確定性 First-match 語義 (Non-deterministic First-match Semantics)**：`QueryListOfContactManyToMany` 缺乏排序，導致 First-match 結果不確定，無法滿足獨立邊界的冪等性與確定性要求。
3. **CRM SDK 實體耦合 (CRM SDK Entity Coupling)**：方法簽章與內部邏輯直接使用 `Microsoft.Xrm.Sdk.Entity`，這違反了 DTO-only 邊界的隔離原則。
4. **缺乏 Request-Local 授權上下文 (Lack of Request-Local Authorization Context)**：查詢未將登入使用者的授權上下文傳遞至查詢中，若移至 Gateway 將面臨越權風險。

### Rejected Alternatives (被否決的替代方案)
- *將讀取部分拆分為 Gateway API，寫入保留在本地*：否決。因為這會引入分散式交易問題，且在讀寫之間存在競態條件，可能導致聯絡人被重複加入不同群組或狀態不一致。
- *使用靜態/共享授權狀態*：否決。違反安全性原則，且不符合 request-local 隔離要求。

### Assumptions (假設)
- 系統要求高一致性，不允許聯絡人同時存在於多個作用中的 `new_app_named` 群組中，或者若存在，必須有確定的優先順序。

### Potential Side Effects (潛在副作用)
- 保持本地設計意味著無法在 Gateway 層直接進行該讀取邊界的快取或獨立擴展，必須依賴現有的本地服務架構。

---

# 3. Implementation Plan (實作計畫 - 最小安全恢復設計)

由於無法進行 partial read cutover，安全的設計是**保持該方法在本地執行 (Keep Local)**，但進行以下重構以消除隱患：

1. **定義 DTO 結構**：建立 `ContactGroupDto` 以解耦 CRM `Entity`。
2. **引入確定性排序**：修改 `QueryListOfContactManyToMany`，在 `QueryExpression` 中加入 `OrderExpression`（例如依 `createdon` 降冪排序），確保 First-match 行為是確定的。
3. **傳遞授權上下文**：重構 `GetContactCurrentGroup` 簽章，接受 `loginContactId`，並在查詢中加入權限過濾，確保符合 request-local 授權原則。

```diff
--- a/SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs
+++ b/SpeechMessageProducts.ChurchReport/Services/Contact/Impl/ContactService.cs
@@ -383,3 +383,3 @@
                 string existContactName = _toolUtility.GetEntityStringAttribute(existingContact, "fullname");
 
                 // 2. 瑼Ｘ?臬撌脣?嗡?撠?
-                Entity currentGroup = GetContactCurrentGroup(existingContact);
+                ContactGroupDto currentGroup = GetContactCurrentGroup(existingContact.Id, loginContact.Id);
 
@@ -518,17 +518,18 @@
-        public Entity GetContactCurrentGroup(Entity contact)
+        public ContactGroupDto GetContactCurrentGroup(Guid contactId, Guid loginContactId)
         {
             try
             {
-                EntityCollection lists = _toolUtility.QueryListOfContactManyToMany(contact.Id);
+                // 傳入 loginContactId 以進行 request-local 授權過濾，並確保查詢結果依建立時間排序以保證確定性
+                EntityCollection lists = _toolUtility.QueryListOfContactManyToMany(contactId, loginContactId);
 
                 foreach (Entity listEntity in lists.Entities)
                 {
                     bool isAppNamed = _toolUtility.GetEntityBoolAttribute(listEntity, "new_app_named");
                     if (isAppNamed)
                     {
-                        return listEntity;
+                        return new ContactGroupDto
+                        {
+                            Id = listEntity.Id,
+                            ListName = _toolUtility.GetEntityStringAttribute(listEntity, "listname"),
+                            IsAppNamed = isAppNamed
+                        };
                     }
                 }
 
                 return null;
             }
```

---

# 4. Considerations (考量因素)

### Critical Findings
- **寫入鄰接 (Write Adjacency) 阻礙讀寫分離**：
  `AddContactToListAsync` 根據 `GetContactCurrentGroup` 的結果，執行 `AddContactToNewListAsync` 或 `TransferContactBetweenListsAsync`，其中包含多個寫入操作（`AddContactToListAsync`、`RemoveContactFromListAsync`、`CreatePresentRecordAsync`、`UpdateEntity`、`AssignOwner`）與外部副作用（`SendAddNewPersonResultLine`、`SendListMemberLine`）。若將讀取拆分為獨立 Gateway 邊界，將導致嚴重的分散式交易一致性問題與競態條件。

### Warning Findings
- **非確定性 First-match 語義**：
  `QueryListOfContactManyToMany` 查詢 `list` 實體時未使用任何排序（`Order By`），而 `GetContactCurrentGroup` 遍歷結果並返回第一個符合 `new_app_named = true` 的群組。這在多群組關聯下會產生非確定性（Non-deterministic）結果。
- **CRM SDK 實體耦合**：
  `GetContactCurrentGroup` 接收並返回 `Microsoft.Xrm.Sdk.Entity`，這與 DTO-only 邊界所需的鬆耦合、序列化友善的 DTO 設計相違背。
- **缺乏 Request-Local 授權上下文**：
  `GetContactCurrentGroup` 僅依賴 `contact.Id` 進行全域查詢，並未將 `loginContact.Id` 或當前請求的授權上下文傳遞至查詢中，存在越權風險。
