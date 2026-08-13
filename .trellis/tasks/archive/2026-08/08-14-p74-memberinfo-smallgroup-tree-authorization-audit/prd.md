# P7.4 MemberInfo 小組樹授權來源稽核

## 目標與使用者價值

針對 authoritative gap matrix 的 `ORG-CALL-00031`
（`memberinfo.smallgroup.retrieve.descriptors`）與 `ORG-CALL-00032`
（`memberinfo.smallgroup.retrieve.memberships`）完成來源與授權稽核。判定既有
MemberInfo 小組樹是否能在不使用 Session、共享 `InMemoryContext`、`ToolUtility` 或保存帳密作為
Gateway 授權來源的前提下，成為獨立、強型別、request-local 的 ProductClient capability。

本 child 的交付物是安全設計決策及精確恢復條件。它不是 runtime migration、consumer cutover、
CE 測試、feature enablement、P7.5 或 P8 任務。

## 已確認事實

- 權威矩陣把 00031／00032 都列為 `churchreport.list.membership` 的 read capability；目前 registry、
  Data8 executor、ProductClient 都未建立，consumer 仍為 `not-migrated`／`temporary-legacy`，CE 8.2／9.1
  與 Embedded／Dedicated evidence 都是 `evidence-pending`。
- `LoadDistrictTree`、`SearchDistrictTree` 與 `LoadGroupMembers` 都先呼叫
  `EnsureCorrectUserData()`，再由 `GetAccess()` 從 Session `_MemberInfoAccess` 讀取或從
  `InMemoryContext.PersonalInfomationModel` 與 `InMemoryContext.ListManager` 推導 access。
- Church branch 以固定 CRM query 取得所有有效的 app-named「小組名單」descriptor；Shepherd branch 則呼叫
  `GetShepherdListIds()`，其會呼叫 `EnsureShepherdListsLoaded()`，並從共享
  `InMemoryContext.ListManager.m_MultiGroupList` 取得 list ID。
- `EnsureShepherdListsLoaded()` 在清單未載入時會使用 `ListManager` 保存的 account/password 呼叫
  `SetupListManager()`。這是授權判斷期間的 legacy CRM 載入與共享可變狀態，不是 request-local、
  server-derived authorization boundary。
- 小組 descriptor 及 membership 查詢同時使用 legacy `IOrganizationService`／`Entity`／`QueryExpression`；
  後續 tree、search、group member routes 又同時依賴 closed-status metadata、current contact
  filter、relation projection、Church cache 與不同的 browser locator 行為。

## 需求

1. 本 child 必須先分開稽核 Church 與 Shepherd 的 authorization source、資料流、shared mutable state、
   CRM/SDK bridge、cache 與 lifecycle，不得把兩者合併成一個未證明的 capability。
2. 禁止把 Session `_MemberInfoAccess`、`InMemoryContext`、`ListManager`、保存帳密、shared CRM service、
   `Entity`／`EntityCollection`、browser `listId`、或 caller-supplied profile／connector／endpoint／credential
   當成 Gateway 的授權或路由 authority。
3. 禁止以只支援 Church branch、只回傳 descriptor、或只實作 static list membership 的方式，宣稱完成既有
   MemberInfo 小組樹 use case；若 Shepherd 的安全邊界未解決，必須維持整個 consumer legacy。
4. 若無法證明 request-local、server-derived、immutable authorization scope 在所有 Session、cache、
   `InMemoryContext`、client composition 與 CRM I/O 之前存在，必須以 source-only local design no-go 結案。
5. 只允許 task／CCG 文件、來源稽核、限時架構審查、驗證、scope-only commit 與 archive；不得變更 runtime、
   matrix migration state、feature gate、CE、fixture、traffic、P7.5 或 P8。

## 驗收條件

- [x] `source-audit.md` 可對應 00031／00032 matrix rows、tree routes、`GetAccess`、Church／Shepherd
      branch、`EnsureShepherdListsLoaded`、legacy SDK bridge 及 cache/lifecycle 來源。
- [x] `design.md` 明確決定能否安全進入 runtime implementation；若 no-go，必須列出禁止事項與重新評估前的
      最小恢復條件。
- [x] `implement.md` 只包含 task record、審查、驗證、commit、archive；不得包含 production code、CE 或
      feature enablement 行動。
- [x] `implement.jsonl` 與 `check.jsonl` 僅含實際相關的 spec／source／matrix 參考，沒有 seed example。
- [x] CCG task 紀錄包含需求、風險、分析與 review 結果，並明確區分「雙模型未完成」和完整雙模型結果。
- [x] 本機檢查證明沒有 runtime、matrix、gate、CE、traffic、P7.5 或 P8 變更。

## 非目標

- 不建立 00031／00032 的 registry、Data8 executor、ProductClient、DTO、consumer route 或 feature gate。
- 不修改 `MemberInfoController`、`InMemoryContext`、`ListManager`、`ToolUtility`、Session 或 cache。
- 不建立或執行 CE fixture、nonce、ledger、preflight、mutation、read-back、reconcile 或 cleanup。
- 不啟用 feature gate、不切換 ChurchReport 流量、不宣稱 consumer migration、ToolUtility removal、P7.5-ready
  或 P8 readiness。
