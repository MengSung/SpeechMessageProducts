# 審查結果

## 本機架構判定

**Critical — 維持 source-only local design no-go。** `GetAccess()` 會優先接受 Session
`_MemberInfoAccess`，cache miss 再由 `InMemoryContext.PersonalInfomationModel` 及 mutable
`InMemoryContext.ListManager` 推導並寫回 Session。這不是在 cache、profile/client composition 或 CRM I/O 前
已建立的 request-local、server-derived immutable authorization scope。

**Critical — Shepherd scope 不可跨入 Gateway。** `GetShepherdListIds()` 在建立 allowlist 前呼叫
`EnsureShepherdListsLoaded()`。當 shared `ListManager` 尚無小組資料時，後者會以保存的 account/password
呼叫 `SetupListManager()`。這使 legacy credential 與 shared mutable state 成為授權判斷的一部分；把隨後取得的
list ID 包裝成 DTO 不能消除跨使用者、profile 或 generation 洩漏風險。

**Warning — Church 與 Shepherd 不是可直接合併的 capability。** Church branch 的 fixed descriptor query
不能補足 Shepherd 的 assignment authority；只遷移 Church 或只遷移 descriptor 都會將既有 tree consumer 的
語意縮窄，卻錯誤聲稱 00031／00032 完成。

**Info — legacy locator guard 不足以成為前置 scope。** `LoadGroupMembers` 雖在 descriptor allowlist 後
檢查 browser `listId`，但 allowlist 本身由上述 Session/InMemoryContext/legacy loader/SDK query 建立，
故不能直接交給 ProductClient 作為 trusted authorization input。

## 限時雙模型狀態

已透過 `Start-CcgDualModelRun.ps1` 分別發起 architect 與 final reviewer run。兩次 self-healing health
check 均成功；在使用者授權的 45 秒上限內都沒有得到 Gemini 或 Claude 的 usable output，已停止等待且未重試。
此結果是「雙模型未完成」，不是完整雙模型審查，也沒有可採用的外部 finding。

## 最小恢復條件

1. 先建立獨立 MemberInfo authorization-boundary child：已驗證 principal 在不讀寫 Session、
   `InMemoryContext`、ListManager 或保存 credential 下，建立 immutable request-local access scope。
2. Shepherd assignment 必須由固定、bounded、server-owned query 或可驗證 authorization service 產生；
   不得由 `EnsureShepherdListsLoaded`／`SetupListManager` 或 legacy cache 產生。
3. Church 與 Shepherd 在 browser `listId` 解析、cache、profile/client composition 與 CRM I/O 前，各自建立
   bounded list allowlist；invalid/duplicate/stale/ambiguous scope 一律 fail closed。
4. 其後的 descriptor/membership capability 才能分別設計 fixed template、bounded DTO、A/B/profile isolation、
   cancellation/fault/lease cleanup、disabled gate、CE evidence 與 rollback owner。

本 child 沒有 CE、consumer cutover、ToolUtility removal、P7.5 或 P8 evidence。
