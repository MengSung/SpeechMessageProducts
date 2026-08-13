# MemberInfo 關係／目標 capability 的來源稽核設計

## 判定

`ORG-CALL-00033` 是 source-only local design no-go，不進入 runtime
implementation。它是個人資料的 relation expansion，而既有 consumer 只在
共享可變的 MemberInfo access/scope 流程之後才取得 contact IDs；將那個集合
傳給 Data8 並不能把它轉換為可信的 server authorization boundary。

## 現況資料流

```text
browser route / existing action
  -> EnsureCorrectUserData
  -> GetAccess (Session cache or InMemoryContext; writes Session)
  -> Church or Shepherd scope
       Shepherd -> EnsureShepherdListsLoaded -> saved-credential ListManager load
  -> contact query / CanViewContactsBatch
  -> BatchRelationGoals(contact IDs)
  -> unbounded RetrieveAllEntities(connection)
  -> RelationGoalFormatter -> JSON row
```

這條路徑在進行 capability selection、profile resolution、connector allocation
或 CRM I/O 前，沒有可信的 immutable scope。另一方面，`RetrieveAllEntities`
沒有查詢 family 專屬上限，且 catch 將 fault 與「確實沒有關聯」混為相同的
empty display result；兩者都不能作為安全 DTO contract 的基礎。

## 禁止的局部方案

- 只為 Church flow 新增 relation client。
- 信任 `CanViewContactsBatch`、Session 或舊 `allowedIds` 的輸出。
- 把 connection fault catch 後回傳空 string 作為 typed success。
- 以現有無上限的 paging helper 實作 Data8 request。
- 以 legacy entity/reference formatter 當 ProductClient boundary，或讓
  typed failure fallback/retry 到 ToolUtility。

## 恢復後的目標資料流

```text
authenticated principal
  -> immutable server-derived MemberInfo scope
  -> bounded authorized contact-ID snapshot
  -> fixed relation-goal query with explicit result budgets
  -> request-local Data8 lease
  -> validated immutable DTO response
  -> controller projection
```

只有前置 authorization-boundary child 證明此資料流後，關係查詢才能被拆成
獨立 fixed-query capability。這個 future child 必須將 A/B/profile isolation、
cancellation、fault eviction、lease disposal 與 bounded output 視為同一個
contract；它不是目前 child 的實作範圍。

## 回復與部署

本 child 沒有 runtime、設定或資料庫變更，故 rollback 是不改變執行行為。
它不提供 CE、host、traffic、P7.5 removal 或 P8 readiness evidence。
