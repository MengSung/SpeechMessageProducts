# ORG-CALL-00031／00032／00033 本機重新稽核

## 證據來源與限制

- Phase-0 matrix：`.trellis/tasks/07-23-dynamics-connection-compatibility/phase0-organization-call-matrix.json`。
- legacy call graph：`SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs` 的 `GetAccess`、`GetVisibleSmallGroupDescriptors`、`FetchSmallGroupDescriptors`、`FetchGroupMemberships`、`CanViewContactsBatch`、`BatchRelationGoals` 與 `GetRelationGoals`。
- 新安全前置條件：`MemberInfoServerAssignmentEvidenceSource` 與 `MemberInfoTargetAuthorizationScope`，其 assigned-list scope 的 server-derived list ID 有效、唯一且最多 512 個；任何 subject/evidence 不完整均 fail closed。
- CCG architecture run 於 2026-08-14 啟動，總等待預算 45 秒已用盡，run directory 只留下 health/prompt artifacts，沒有 `summary.json` 或可用模型回應。因此本次狀態為「雙模型未完成」，以下結論僅使用本機可追溯證據。

## ORG-CALL-00031：small-group descriptors

### 現有行為

`GetVisibleSmallGroupDescriptors` 對 Church 以 unrestricted list query 讀取，對 Shepherd 以 `GetShepherdListIds()` 限制。後者先呼叫 `EnsureShepherdListsLoaded()`，再從 `InMemoryContext.ListManager.m_MultiGroupList` 取值；這是 request/session/profile/credential-bearing legacy state，不能沿用。`FetchSmallGroupDescriptors` 則以 `IOrganizationService`、`QueryExpression`、`RetrieveAllEntities` 與 `Entity` 實作，且沒有產品層 response budget。

### 重新判定

可以建立一個新的獨立 implementation child，但它只能提供 **internal scope → immutable descriptor snapshot**，不接 `MemberInfoController`。它不可接受呼叫端 `listIds`，而必須直接取 `MemberInfoTargetAuthorizationScope`：

- Church-wide branch 使用固定 server-owned small-group filter；assigned-list branch 僅取 immutable scope 的 defensive-copied allowlist。
- query 的 projection、排序、每頁、總頁、row 數、每列文字與累積 UTF-8 scalar bytes 上限都必須新增為 registry contract；任一超限、schema mismatch、unknown lookup、null page/cookie 或 transport fault 都回傳去識別化 fail-closed union。
- 不得把現有 `RetrieveAllEntities`、`Entity`、optional caller list ID、legacy cache 或 Church-wide branch 當作 assigned-list fallback。

這是「可建立 child」而非已實作、已接線或有 CE evidence。

## ORG-CALL-00032：small-group memberships

### 現有行為

`FetchGroupMemberships` 的 input 來自 descriptor list IDs，並以 legacy `QueryExpression(listmember)` join `contact` 後用 active/non-closed 條件篩選。輸入可能源自上述 legacy scope；查詢和結果用 CRM SDK 型別表示，跨 chunk 的累積也沒有 Gateway-specific response budget。

### 重新判定

可以與 00031 一起放入同一個 **small-group snapshot data-plane** child，但只有以下條件同時成立時才可開始：

1. 00031 的 immutable descriptor result 是該 request 唯一可使用的 membership list allowlist；不允許 browser、session、legacy cache 或外部 caller 注入 list/contact ID。
2. 00032 固定 listmember/contact join、projection、active/non-closed filter、排序與所有 page/row/text/byte budget；rows 必須被 descriptor result 的 list IDs 再次收斂。
3. 每次 invocation 有自己的 immutable subject scope、result snapshot 與 Data8 lease；完成、取消、timeout、schema mismatch 都不得保留 A/B 的資料、fault 或 lease。
4. 測試包含 Church-wide/assigned-list、empty scope、invalid/duplicate scope、source unavailable、A/B interleaving、bounded paging 與 deterministic drain。

這個 child 的產物只能是 default-disabled local DTO adapter。它不證明 UI parity、consumer cutover、CE parity 或 P7.5 removal。

## ORG-CALL-00033：relation goals

### 現有行為

`BatchRelationGoals` 接受任意 `contactIds` 後查詢 connection 的 `record1id`／`record2id`，再把 role/target display name 格式化；它把每個 chunk 的例外吞掉並回傳 partial-like empty relation labels。`GetRelationGoals` 也使用 static organization service，並在 exception 時靜默回空。上游 `CanViewContactsBatch` 仍依賴 legacy access/membership/contact chain。

### 重新判定：no-go

assignment evidence 與 00031／00032 的 descriptor/membership snapshot 都不等同於「此 target contact 的 relation goals 可供本 caller 檢視」。在以下項目被另一個 source-only child 完整證明前，00033 不可建立 implementation child：

- server-derived、request-local 的 target-contact authorization mapping，且由 authorized membership DTO 導出；
- fixed relation query 的 `record1id`／`record2id` 語意、target type/role schema validation、排序、chunk/page/row/text/byte budgets；
- 不混淆 empty、unavailable、partial、timeout、cancelled 的 immutable result/error union；
- 對每個 target 都重做 authorization 收斂，並完成 A/B isolation、cancellation、lease fault eviction 與 drain proof。

不得以「assignment source 已完成」、「目前 query 是 read-only」或「測試 CRM 可寫」作為繞過。relation-goal 的問題是授權與結果完整性，不是資料庫寫入權限。

## 下一步

建立 `p7-memberinfo-smallgroup-snapshot-data-plane` 作為獨立 implementation child，僅處理 00031／00032 的 local-only fixed read contract。00033 保持本機 no-go，且 P7.5/P8、Controller、CE、flag、traffic 維持不動。
