# P7.4 MemberInfo 關係／目標唯讀授權邊界稽核

## 目標

稽核權威矩陣的 `ORG-CALL-00033`
`memberinfo.connection.retrieve.relation.goals` 是否能在不改變既有
ChurchReport consumer 的前提下，成為一個預設關閉、DTO-only 的本機
Data8／ProductClient 唯讀 capability。若來源無法證明安全的授權、界限或
資源生命週期，必須精確記錄 source-only local design no-go，而不是以局部
Church branch 或舊有授權結果製造不完整遷移。

本 child 僅處理來源稽核與任務紀錄；不建立 registry、Data8 executor、
ProductClient、feature gate 或 CE fixture，不進行 CE 呼叫、流量切換、
ToolUtility 移除、P7.5 或 P8 工作。

## 已確認的來源事實

- `BatchRelationGoals` 由 `SearchDistrictTree`、`LoadGroupMembers` 與
  `LoadUngroupedMembers` 呼叫；三者都先進入 `EnsureCorrectUserData()` 和
  `GetAccess()`／`CanViewContactsBatch()` 的 MemberInfo 授權流程。
- `GetAccess()` 可直接信任 Session `_MemberInfoAccess`；cache miss 時從共享
  `InMemoryContext` 的 login model／`ListManager` 推導 access，並把結果回寫
  Session。這不是 cache、profile/client composition 或 CRM I/O 之前的
  request-local、server-derived、immutable authorization boundary。
- Shepherd 分支在取得 contact scope 時會呼叫
  `EnsureShepherdListsLoaded()`；其未載入分支以保存在 shared `ListManager`
  的 account/password 呼叫 `SetupListManager()`。因此它可能在新的 Gateway
  capability 選擇與 I/O 之前進行 legacy credential-backed load。
- relation 查詢對每個 contact-ID chunk 建立固定 `connection` projection，
  但 `RetrieveAllEntities` 會在 `MoreRecords` 時持續分頁，沒有 capability
  專屬的頁數、列數、每列文字或總 response-byte 上限；目前還會吞掉
  connection fault，將 partial/unknown 結果格式化為一般空白關係字串。
- 輸入 contact IDs 雖然在每個目前 consumer 都在後段經 `allowedIds` 篩選，
  但其授權證明仍回到上述 shared mutable MemberInfo 流程。既有 caller 不能
  成為新 Gateway 介面的 server-authorized input。

## 需求與約束

- 不可將 Session、`InMemoryContext`、`ListManager`、ToolUtility、static
  `IOrganizationService`、browser/route 值、舊 `allowedIds`，或任何 caller
  指定的 profile、connector、endpoint、credential、query 作為新 capability
  的授權或 routing authority。
- 不可只遷移 Church branch，也不可排除 Shepherd branch 後聲稱
  `ORG-CALL-00033` consumer 已安全遷移。
- 不可接受或回傳 CRM `Entity`、`EntityReference`、`QueryExpression`、
  `EntityCollection`、paging cookie、OData continuation、原始例外或未受界限的
  relation/target graph。
- 不可將 connection read 的 fault／timeout／partial page 降級為成功的空字串；
  未來 capability 必須在 publication 前 fail closed，並讓唯一 lease owner
  做 deterministic cleanup。
- 不可因本 child 改變 matrix migration/CE/host/traffic/P7.5/P8 狀態。

## 驗收條件

- [x] 已從權威 matrix、所有三個 consumer call chain、`GetAccess`、Shepherd
      scope loader、relation query 與 formatter 證明目前授權／response boundary
      不符合 repository isolation contract。
- [x] 已定義精確 no-go、受影響範圍與恢復條件；沒有進行任何 CE、feature gate、
      traffic、consumer 或 runtime 變更。
- [x] 已建立 design、implement、source audit、CCG task 與 context manifests，
      並記錄雙模型 45 秒預算的降級狀態。
- [ ] 已完成 task-record quality check、scope-only commit 與 Trellis/CCG archive。

## 恢復條件

先完成獨立的 MemberInfo request-local authorization-boundary child：它必須由
已驗證 principal 在伺服器端建立不可變 Church／Shepherd scope，且在任何
Session、`InMemoryContext`、cache、legacy `ListManager`、profile/client
composition 或 CRM I/O 之前完成。Shepherd scope 不可使用儲存帳密或 loader。

完成該前置條件後，才可重新設計 `ORG-CALL-00033`：輸入只能是此 scope
產生的 bounded、去重 contact IDs；server-owned fixed query 必須有明確的
chunk/page/row/text/response-byte 上限、只發佈 immutable scalar DTO，並在
timeout/fault/partial/cleanup failure 時 fail closed。還需要 A/B scope/profile
isolation、cancellation/lease-drain、CE 9.1、Embedded/Dedicated parity、
rollback 與流量證據，才能另行評估 enablement。
