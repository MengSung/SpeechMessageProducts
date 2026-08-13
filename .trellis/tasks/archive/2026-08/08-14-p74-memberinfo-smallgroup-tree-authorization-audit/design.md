# 設計：以 request-local 授權邊界保護 MemberInfo 小組樹遷移

## 決策

本 child 採取 source-only local design no-go。這不是把 Church branch 的固定 descriptor query
誤判為已完成 Gateway capability，而是拒絕讓 Shepherd 的 shared `ListManager`、Session access cache
及保存帳密的 loader 跨越 Gateway authorization boundary。

## 既有資料流與缺口

```text
browser route / Session
  -> EnsureCorrectUserData
  -> GetAccess (Session or InMemoryContext)
  -> Shepherd: EnsureShepherdListsLoaded (saved credential / mutable ListManager)
  -> list IDs / descriptor query / membership query
  -> Entity-based tree and member projections
```

這條資料流不符合 repository-wide isolation boundary：access 與 Shepherd list scope 在 request-local
server authorization 尚未建立前，會讀寫 Session、讀取 shared `InMemoryContext`，並且可能以保存帳密觸發
legacy CRM 載入。它不能保證 A/B user、profile 或 generation 不會共享 mutable authorization state；即使
Church branch 表面上是固定 query，也不能藉此省略 Shepherd branch 或重新定義 legacy consumer 的語意。

## 未來允許的資料流

```text
authenticated principal
  -> server-derived immutable MemberInfo scope
  -> Church or Shepherd capability selected on server
  -> request-local, bounded authorized list ID allowlist
  -> fixed registered descriptor/membership template
  -> Data8 bounded projection
  -> immutable DTO response
```

未來必須先由獨立 authorization-boundary child 建立 `MemberInfo` 的 server-derived scope；此 scope
不得從 Session、`InMemoryContext`、legacy ListManager、password、browser locator 或 shared cache 取得
authority。Church 與 Shepherd 可成為不同 capability，前提是各自都能產生同一 request 內 immutable、
bounded list allowlist，並先於 profile resolution、cache、connector allocation 與 CRM I/O。

Gateway registry 僅能執行 server-owned fixed templates。request 只能傳遞經 scope 驗證的 bounded list
locator，不得包含 profile、connector、credential、endpoint、organization、query、entity、paging cookie 或
authorization decision。任何 missing、ambiguous、duplicate、stale、cancelled、timeout、faulted 或
unbounded state 都 fail closed；沒有 ToolUtility fallback、retry、DTO-to-Entity rehydration 或 request-time
dual path。

## 相容性、rollback 與資源所有權

本 child 不改變 legacy route，既有 ChurchReport 行為維持不變。因沒有 runtime、gate、CE 或資料變更，
rollback 是 no-op；commit/archive 僅保存本次安全決策。

未來 child 的 runtime owner 必須明確：產品 request 擁有 request-local scope/DTO/cancellation；Data8 host
擁有 profile-generation pool、lease、permit 與 fault disposal；任何 Church-safe cache 只能保存明確宣告
的公開 skeleton，且需 bounded size/TTL/eviction。Shepherd rows、authorization decision、session object、
login credential、Entity、exception 或 response 不得放進共享 cache。

## 明確禁止

- 不可將 `GetAccess()` Session cache 或 `InMemoryContext` 視為 Gateway 授權證明。
- 不可在授權前呼叫 `EnsureShepherdListsLoaded()`、`SetupListManager()` 或以帳密載入 CRM。
- 不可只遷移 Church branch 而宣稱完成 00031／00032 或 MemberInfo 小組樹 consumer。
- 不可將 `IOrganizationService`、`Entity`、`EntityCollection`、CRM query 或 response state 穿越 typed boundary。
- 不可增加 shared cache、legacy fallback、feature enablement、CE evidence、traffic cutover、P7.5 removal 或 P8。
