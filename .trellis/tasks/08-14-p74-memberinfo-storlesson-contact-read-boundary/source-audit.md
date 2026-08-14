# ORG-CALL-00027 來源與授權稽核

## Matrix 對應

| Row | Operation | 現況 |
| --- | --- | --- |
| ORG-CALL-00027 | `memberinfo.storlessons.retrieve.by.contact` | existing typed operation；consumer 尚未完成安全切換，CE／host／traffic evidence pending。 |

## 呼叫與 authority trace

1. `MemberInfoController.LoadContactStorLessons` 先呼叫 `EnsureCorrectUserData()`，接著 parse browser
   `contactId` 並呼叫 `CanViewContact(contactGuid)`；只有後者成功才建立 `StorLessonQueryService`。
2. `CanViewContact` 直接呼叫 `GetAccess()`。`GetAccess` 優先接受 Session `_MemberInfoAccess`，cache miss
   才由 `InMemoryContext.PersonalInfomationModel.m_LoginContact` 與
   `InMemoryContext.ListManager.LoginType` 推導 access，並把 access 寫回 Session。
3. Shepherd branch 的 `CanViewContact` 呼叫 `GetShepherdContactIds()`；它在建立 contact set 前呼叫
   `EnsureShepherdListsLoaded()`，且後者可在 shared list records 缺失時，以 ListManager 的
   `m_Account`／`m_Password` 執行 `SetupListManager()`。
4. `BaseChurchController.EnsureCorrectUserData` 會讀 Session password、比較 shared ListManager password，
   以 static `_userValidationCache` 快取結果，並可能以 Session credential 或 LINE ticket 呼叫
   `SetupListManager()`。這是 session hydration／legacy state repair，不是 scope authority。
5. `StorLessonQueryService.GetByContactAsync` 的 typed path 有既有 `IPackage01FeeReadClient`、固定
   deployment profile 與 fixed workload；它只在一個已證明的 authorization boundary 之後才可安全使用。
   現況 trace 將 contact GUID／名稱寫入 diagnostics，未來 migration 前亦必須改成有界、去識別化分類。

## 判定

Gateway 所需的 immutable `IsolationBoundary` 必須在 cache、profile resolution、connector allocation 和
outbound I/O 前從 authenticated principal 衍生。現有 call chain 反而先進入 Session、shared mutable
`InMemoryContext`、static validation cache，並在 Shepherd branch 有 credential-backed CRM loader。傳遞
後段 `CanViewContact` 成功的 GUID 不能移除這些既有 authority 依賴；將它包裝成 DTO 或 sub-gate 亦不能
證明 A/B user、profile 或 generation isolation。

因此 `ORG-CALL-00027` 是 source-only local design no-go。它不代表 typed client 不可用，也不阻塞其他
獨立 capability；它只禁止這一個 MemberInfo consumer 在前置 authorization-boundary 完成前接入 Gateway。

## 最小恢復條件

1. 已驗證 principal 在不讀寫 Session、`InMemoryContext`、ListManager、static user-state cache 或
   legacy credential 下，建立 immutable request-local MemberInfo scope。
2. Church 與 Shepherd 各由 server-owned、bounded source 建立 target allowlist；Shepherd assignment
   不得走 `EnsureShepherdListsLoaded`／`SetupListManager`。
3. scope 完成後才 parse browser locator、resolve deployment profile/generation、allocate connector 及執行
   CRM I/O；invalid、stale、duplicate、ambiguous scope 一律 fail closed。
4. 後續 capability 必須具有 bounded fixed query／DTO、A/B scope-profile isolation、cancellation／fault
   eviction、lease drain／cleanup、去識別化 diagnostics、CE 9.1、host parity、rollback 與 traffic evidence。
