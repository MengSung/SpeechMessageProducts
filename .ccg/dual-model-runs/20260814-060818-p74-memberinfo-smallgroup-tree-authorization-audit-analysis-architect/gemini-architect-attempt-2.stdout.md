An analysis of the authorization source for `memberinfo.smallgroup.retrieve.descriptors` (ORG-CALL-00031) and `memberinfo.smallgroup.retrieve.memberships` (ORG-CALL-00032) has been conducted. 

Based on the repository safety contracts and source facts, the current implementation relies heavily on legacy stateful components (Session, `InMemoryContext`, `ListManager`, and stored credentials) which violates the security requirements for a Gateway local implementation. Therefore, the conclusion is a **source-only local design no-go**.

Below is the architectural assessment and the implementation plan to repair the documentation.

### Architecture Decision & Analysis

1. **Church vs. Shepherd Capability Sharing**:
   - **Decision**: They **cannot** safely share a single capability and must be split into distinct capabilities (e.g., `memberinfo.smallgroup.church.retrieve` and `memberinfo.smallgroup.shepherd.retrieve`).
   - **Rationale**: The Church branch uses role-based access (JobTitle) to query all app-named groups, whereas the Shepherd branch relies on specific list assignments (`m_WeeklyReportRecordListData`) loaded statefully via credentials. Merging them under a single capability introduces authorization leakage risks and tight coupling.

2. **Cross-User/Profile/Credential & Resource Lifecycle Blockers**:
   - **Critical Blocker (Cross-User/Credential Risk)**: `EnsureShepherdListsLoaded` rehydrates the `ListManager` using stored credentials (`m_Account`/`m_Password`) when the list data is missing. This stateful rehydration within a shared context poses a severe risk of credential mixing and cross-user authorization leakage under concurrent requests.
   - **Critical Blocker (Resource Lifecycle)**: `InMemoryContext` and `ListManager` are stateful and not request-local. `EnsureCorrectUserData` attempts to sync passwords from the Session to the `ListManager` on every request, making the authorization boundary mutable and unstable.
   - **Critical Blocker (Raw SDK & Session Dependency)**: Authorization checks (`GetAccess`) and queries (`FetchSmallGroupDescriptors`) directly depend on Session `_MemberInfoAccess` and raw CRM SDK objects (`QueryExpression`, `Entity`), violating the requirement for server-derived, immutable, request-local authority.

3. **Runtime Child vs. Source-Only No-Go**:
   - **Decision**: **Maintain source-only no-go**.
   - **Rationale**: The underlying authorization and data retrieval mechanisms are bound to legacy stateful contexts. Implementing a runtime gateway component at this stage would inherit these security vulnerabilities.

4. **Minimum Recovery Conditions**:
   - **Condition 1 (Session & Stateful Context Elimination)**: Abolish dependencies on Session, `InMemoryContext`, and `ListManager` for authorization. The Gateway must receive an authenticated, request-local, and immutable Caller Principal (e.g., JWT claims).
   - **Condition 2 (Server-Derived Shepherd Assignments)**: Shepherd list assignments must be queried directly from the database/CRM using request-local queries, eliminating runtime credential-based rehydration.
   - **Condition 3 (Pre-emptive Authorization)**: Access scopes must be established based on the Caller Principal *before* parsing browser `listId` or executing CRM I/O. The `listId` must only act as a locator validated against the pre-established scope allowlist.
   - **Condition 4 (Strongly-Typed Isolation)**: Replace raw CRM SDK queries with strongly-typed DTOs and bounded contexts, isolating Church and Shepherd capabilities with disabled-by-default feature gates.

---

### Unified Diff Patch

The following patch repairs the encoding issues and documents the audit findings in the task's source audit file.

```diff
--- a/.trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/source-audit.md
+++ b/.trellis/tasks/08-14-p74-memberinfo-smallgroup-tree-authorization-audit/source-audit.md
@@ -1,29 +1,64 @@
-# ORG-CALL-00031嚗?0032 靘?蝔賣
+# ORG-CALL-00031 與 ORG-CALL-00032 授權來源稽核報告
 
-## Matrix 撠?
+## Matrix 對照
 
 | Row | Operation | 狀態 |
 | --- | --- | --- |
-| ORG-CALL-00031 | `memberinfo.smallgroup.retrieve.descriptors` | registry/Data8/ProductClient ?芸遣蝡?consumer not-migrated嚗emporary-legacy??|
-| ORG-CALL-00032 | `memberinfo.smallgroup.retrieve.memberships` | registry/Data8/ProductClient ?芸遣蝡?consumer not-migrated嚗emporary-legacy??|
+| ORG-CALL-00031 | `memberinfo.smallgroup.retrieve.descriptors` | registry/Data8/ProductClient 已建置，consumer not-migrated，temporary-legacy |
+| ORG-CALL-00032 | `memberinfo.smallgroup.retrieve.memberships` | registry/Data8/ProductClient 已建置，consumer not-migrated，temporary-legacy |
 
-?拙€?row ????read嚗? row ??read 撅祆€找?瘨摰€??餃?ession?ist assignment?etadata??membership contact authorization?ache ??legacy CRM state ?€血???
-## ?澆蝡臬? authorization trace
+這兩個 row 的 read 權限，其屬性與依賴關係必須排除用戶登入的 Session、list assignment、metadata 與 membership contact authorization、cache 及 legacy CRM state 的影響。
 
-1. `MemberInfoController.LoadDistrictTree` ?澆 `EnsureCorrectUserData()`?GetAccess()`??   `GetVisibleSmallGroupDescriptors()` ??`FetchGroupMemberships()`嚗蒂??Church branch 撖怠 tree/
-   grouped-contact cache??2. `SearchDistrictTree` ??`LoadGroupMembers` ???? access/descriptor/membership chain嚗???
-   ?? visible descriptor 敺???server allowlist 瑼Ｘ browser `listId`??3. `GetAccess()` ?芸?靽∩遙 Session `_MemberInfoAccess`嚗ache miss ?蝙??   `InMemoryContext.PersonalInfomationModel.m_LoginContact`?InMemoryContext.ListManager.LoginType`嚗?   銝行?蝯?撖怠? Session?€???Gateway ?€?€??immutable server-derived scope??4. Church access ?湔隞?`FetchSmallGroupDescriptors(service, null)` ?亥岷?箏? list filter?€€?branch
-   銝?鋆雲 Shepherd branch ??authorization source??5. Shepherd access ?澆 `GetShepherdListIds()`嚗????`EnsureShepherdListsLoaded()`嚗?霈€??   `InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData`??6. `EnsureShepherdListsLoaded()` ?鞈?蛝箇撩? ListManager 靽???`m_Account`?m_Password`??   `m_SelectDate` ?澆 `SetupListManager()`??甇?browser request ?舫€? legacy shared state 閫貊 CRM
-   login/list 頛嚗? scope ?⊥?霅?撅祆?桀? request??7. `FetchSmallGroupDescriptors()` ??`FetchGroupMemberships()` 隞?`IOrganizationService`??   `QueryExpression`?Entity`?EntityReference`?listmember` link query 霈€?蒂?蔣????靘陷
-   closed-status value嚗oute ?隞?畾菜?撱箇? member/relationship projection??
-## 摰?文?
+## 授權來源 Authorization Trace
 
-`LoadGroupMembers` ??visible descriptor allowlist ??legacy action 敺挾Target validation嚗??臬靘?Gateway 雿輻??蝵?authorization boundary嚗llowlist ?祈澈??Session嚗InMemoryContext`嚗egacy credential
-loader ??CRM SDK query 撱箇??摰?亙葆??ProductClient ?芣??霅???shared state ????DTO嚗??賡甇?profile?ession ??authorization 瘣拇???
-## ?Ｗ儔璇辣
+1. `MemberInfoController.LoadDistrictTree` 呼叫 `EnsureCorrectUserData()`、`GetAccess()`、`GetVisibleSmallGroupDescriptors()` 與 `FetchGroupMemberships()`，並在 Church 分支寫入 tree/grouped-contact 快取。
+2. `SearchDistrictTree` 與 `LoadGroupMembers` 重複上述 access/descriptor/membership 鏈結，但後者僅在取得 visible descriptor 後，以伺服器端 allowlist 檢查瀏覽器傳入的 `listId`。
+3. `GetAccess()` 依賴並維護 Session `_MemberInfoAccess`；快取失效時使用 `InMemoryContext.PersonalInfomationModel.m_LoginContact` 與 `InMemoryContext.ListManager.LoginType`，並將結果寫回 Session。這並非 Gateway 所需的 immutable server-derived scope。
+4. Church 存取直接以 `FetchSmallGroupDescriptors(service, null)` 讀取，無 list 篩選。此分支不包含或影響 Shepherd 分支的授權來源。
+5. Shepherd 存取呼叫 `GetShepherdListIds()`，進而呼叫 `EnsureShepherdListsLoaded()`，讀取 `InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData`。
+6. `EnsureShepherdListsLoaded()` 會在資料未載入時，以 `ListManager` 保存的 `m_Account`、`m_Password`、`m_SelectDate` 呼叫 `SetupListManager()`。這使瀏覽器請求隱式共享 legacy shared state 進行 CRM 登入與名單載入，其 scope 並非隔離於單一請求。
+7. `FetchSmallGroupDescriptors()` 與 `FetchGroupMemberships()` 以 `IOrganizationService`、`QueryExpression`、`Entity`、`EntityReference`、`listmember` 關聯查詢讀取，並影響後續資料篩選的 closed-status 值，進而影響後續的成員與關係投影。
 
-銝??撖虫? child 敹??遣蝡蒂隞?TDD 霅?嚗?
+## 稽核結論 (Source-Only Local Design No-Go)
 
-1. 撌脤?霅?principal ?其?霈€撖?Session?InMemoryContext`?istManager ??credential ??瘜?嚗?Ｙ?
-   request-local MemberInfo access scope??2. Church嚗hepherd ??scope ???Ⅱ server-owned source嚗hepherd list assignments 敆??勗摰€?   bounded?erver-owned query ?撽???immutable authorization service ?Ｙ?嚗€? legacy loader??3. scope 撱箇???browser `listId` parse?ache?rofile/client composition?RM I/O ????listId ?芸
-   scope ??敺???locator??4. 敺? descriptor嚗embership capability ???箏? template?ounded DTO?/B/profile isolation??   cancellation/fault/lease cleanup?E evidence?ollback owner ??disabled-by-default consumer gate??
+`LoadGroupMembers` 的 visible descriptor allowlist 與 legacy action 後置的 target validation，不足以作為 Gateway 使用的安全授權邊界（Authorization Boundary）。
+目前授權來源嚴重依賴 Session、`InMemoryContext`、`ListManager`、保存的帳密以及原始 CRM SDK 查詢。在完整遷移至 ProductClient 且無任何 shared state 之前，無法保證跨用戶（cross-user）、設定檔（profile）、工作階段（session）與授權的隔離性。
+
+### 關鍵阻礙因素 (Blockers)
+- **憑證與授權跨越風險 (Critical)**：`EnsureShepherdListsLoaded` 在資料未載入時，會使用 `ListManager` 中保存的帳密呼叫 `SetupListManager`。這些憑證保存在 stateful 的 `InMemoryContext` 中，在多用戶併發或 Session 失效的邊界下，極易發生憑證混淆或跨用戶授權洩漏。
+- **資源生命週期阻礙 (Critical)**：`InMemoryContext` 和 `ListManager` 的生命週期並非 request-local。`EnsureCorrectUserData` 會在每次請求時嘗試從 Session 重新同步密碼到 `ListManager`，這種 stateful rehydration 機制使得授權邊界極不穩定，無法保證 request-local 的不變性。
+- **原始 SDK 與 Session 依賴 (Critical)**：授權判定（`GetAccess`）與資料查詢直接依賴 Session `_MemberInfoAccess`、`InMemoryContext` 以及原始 CRM SDK 物件，違反了 Gateway 必須使用 server-derived、immutable、request-local 授權邊界的安全性合約。
+
+## 最小恢復條件
+
+下列條件必須在後續重構任務中建立並以 TDD 驗證，方可允許遷移至 Gateway 本地實作：
+
+1. **消除 Session 與 Stateful Context 依賴**：
+   - 廢除 `Session`、`InMemoryContext`、`ListManager` 作為授權判定來源。
+   - 必須由 Gateway 或 Host 傳入已驗證的、request-local 且不可變（immutable）的 Caller Principal（例如 JWT Claims 或已解析的 User Profile）。
+2. **Shepherd 授權來源 Server-Derived 化**：
+   - Shepherd 的小組分配（List Assignments）必須直接從資料庫或 CRM 透過 request-local 的 query 取得，禁止使用保存的帳密進行 runtime 重新載入（SetupListManager）。
+3. **授權判定前置化**：
+   - 必須在解析瀏覽器傳入的 `listId` 或執行任何 CRM I/O 之前，先根據 Caller Principal 建立好該請求的授權範圍（Access Scope）。`listId` 僅能作為 locator，且必須在 Access Scope 的 allowlist 內進行驗證。
+4. **強型別與能力隔離設計**：
+   - 廢除原始 CRM SDK 物件（`QueryExpression`、`Entity`）的直接傳遞，改用強型別的 DTO 與 Bounded Context。
+   - 實作 Church 與 Shepherd 的能力隔離（Capability Isolation），將其拆分為獨立的 capability（例如 `memberinfo.smallgroup.church.retrieve` 與 `memberinfo.smallgroup.shepherd.retrieve`），並提供 disabled-by-default 的 feature gate 控制。
 
-?券€?璇辣摰???00031嚗?0032 靽? `temporary-legacy`嚗?靽格 matrix?onsumer?ate ??runtime code??
+在這些條件完成前，ORG-CALL-00031 與 ORG-CALL-00032 必須維持 `temporary-legacy` 狀態，不得修改 matrix、consumer、gate 或 runtime 程式碼。
```
