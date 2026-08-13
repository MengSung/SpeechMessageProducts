# ORG-CALL-00031／00032 來源稽核

## Matrix 對應

| Row | Operation | 現況 |
| --- | --- | --- |
| ORG-CALL-00031 | `memberinfo.smallgroup.retrieve.descriptors` | registry/Data8/ProductClient 未建立；consumer not-migrated；temporary-legacy。 |
| ORG-CALL-00032 | `memberinfo.smallgroup.retrieve.memberships` | registry/Data8/ProductClient 未建立；consumer not-migrated；temporary-legacy。 |

兩個 row 均標為 read，但 row 的 read 屬性不消除它們對登入、Session、list assignment、metadata、
membership contact authorization、cache 與 legacy CRM state 的耦合。

## 呼叫端及 authorization trace

1. `MemberInfoController.LoadDistrictTree` 呼叫 `EnsureCorrectUserData()`、`GetAccess()`、
   `GetVisibleSmallGroupDescriptors()` 與 `FetchGroupMemberships()`，並在 Church branch 寫入 tree/
   grouped-contact cache。
2. `SearchDistrictTree` 及 `LoadGroupMembers` 重複同一 access/descriptor/membership chain；後者只有在
   取完 visible descriptor 後才用 server allowlist 檢查 browser `listId`。
3. `GetAccess()` 優先信任 Session `_MemberInfoAccess`；cache miss 時使用
   `InMemoryContext.PersonalInfomationModel.m_LoginContact`、`InMemoryContext.ListManager.LoginType`，
   並把結果寫回 Session。這不是 Gateway 所需的 immutable server-derived scope。
4. Church access 直接以 `FetchSmallGroupDescriptors(service, null)` 查詢固定 list filter。這個 branch
   不會補足 Shepherd branch 的 authorization source。
5. Shepherd access 呼叫 `GetShepherdListIds()`；它先呼叫 `EnsureShepherdListsLoaded()`，再讀取
   `InMemoryContext.ListManager.m_MultiGroupList.m_WeeklyReportRecordListData`。
6. `EnsureShepherdListsLoaded()` 會在資料空缺時用 ListManager 保存的 `m_Account`、`m_Password`、
   `m_SelectDate` 呼叫 `SetupListManager()`。因此 browser request 可透過 legacy shared state 觸發 CRM
   login/list 載入，且 scope 無法證明屬於目前 request。
7. `FetchSmallGroupDescriptors()` 及 `FetchGroupMemberships()` 以 `IOrganizationService`、
   `QueryExpression`、`Entity`、`EntityReference`、`listmember` link query 讀取並投影。它們還依賴
   closed-status value；route 的其他階段會建立 member/relationship projection。

## 安全判定

`LoadGroupMembers` 的 visible descriptor allowlist 是 legacy action 後段的 target validation，不是可供
Gateway 使用的前置 authorization boundary：allowlist 本身由 Session／`InMemoryContext`／legacy credential
loader 和 CRM SDK query 建立。把它直接帶入 ProductClient 只會把未證明的 shared state 包裝成 DTO，不能防止
profile、session 或 authorization 洩漏。

## 恢復條件

下一個可實作 child 必須先建立並以 TDD 證明：

1. 已驗證 principal 在不讀寫 Session、`InMemoryContext`、ListManager 或 credential 的情況下，能產生
   request-local MemberInfo access scope。
2. Church／Shepherd 的 scope 各有明確 server-owned source；Shepherd list assignments 必須由固定、
   bounded、server-owned query 或可驗證的 immutable authorization service 產生，而非 legacy loader。
3. scope 建立在 browser `listId` parse、cache、profile/client composition、CRM I/O 前完成；listId 只在
   scope 成功後作為 locator。
4. 後續 descriptor／membership capability 各有固定 template、bounded DTO、A/B/profile isolation、
   cancellation/fault/lease cleanup、CE evidence、rollback owner 與 disabled-by-default consumer gate。

在這些條件完成前，00031／00032 保持 `temporary-legacy`，不修改 matrix、consumer、gate 或 runtime code。
