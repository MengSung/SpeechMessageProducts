# 技術設計：MemberInfo small-group snapshot

## 選定方案

採用單一 composed operation `memberinfo.smallgroup.snapshot.retrieve.authorized`，而不是兩個由 controller/adapter 任意串接的 queries。這使 00032 membership query 的唯一 list ID 集合能由同一次 00031 descriptor projection 在 Data8 connector 內部導出，避免 browser、Session、cache 或 legacy `ListManager` 成為 membership authorization source。

operation 的唯一可變 parameters 是已驗證 request scope 所衍生的 `subjectContactId`、`accessMode` 與 assigned-list GUID snapshot。它們在 ChurchReport source 內從 `MemberInfoTargetAuthorizationScope` defensive-copy 後構建；沒有 public HTTP endpoint、Controller route、caller profile selector 或 mutable authority。

## 資料流

```text
validated Cookie principal
  -> P7GatewayRequestScope
  -> existing server-owned assignment evidence
  -> immutable MemberInfoTargetAuthorizationScope
  -> ChurchReport local snapshot source
  -> ProductClient request (deployment profile/workload + copied scope scalars)
  -> Data8 fixed composed snapshot operation (CE 9.1 only)
  -> immutable descriptor + membership response union
  -> ProductClient defensive-copy DTO snapshot
  -> default-disabled ChurchReport local source result
```

Data8 先驗證 operation/CE version/registry/schema/scope；再取得固定 `contact.customertypecode` metadata，唯一解析「結案」option。descriptor query 固定 list entity、columns、active/app-named/small-group filters、ordering 和 assigned-list filter。membership query 只使用已驗證 descriptor IDs，固定 listmember→contact join、active/non-closed predicate、ordering 和 projection。任一 metadata、query、page/cookie、row identity/type、duplicate、overflow、UTF-8 bound 或 cancellation/fault 失敗，都不發布任何 snapshot。

## 上限與回應契約

- scope list IDs：0–512，原封不動套用既有 scope resolver 上限。
- descriptors：最多 512；memberships：最多 4,096；二者使用 overflow sentinel/one-page fail-closed strategy。
- 每個 string 都有 strict UTF-8 scalar-byte limit；整體 operation response 也有 registry page/cumulative byte bound。具體常數在 registry/Data8/response envelope 只能定義一次並由 tests 對齊。
- wire envelope 提供一個新的 snapshot response branch，內含 subject ID、access mode、read-only descriptor records、read-only membership records。ProductClient 不可接受 wrong operation/version/kind/subject/mode、non-subset membership、mutable/array backing collection 或任何 raw CRM state。

## 生命週期與回復

ProductClient／ChurchReport source 都只保存 DI-owned collaborator 與 deployment routing scalar；每次 request 的 scope、parameters、response、collection 與 cancellation token 都是 stack-local。Data8 executor 是 connector/lease/permit/transport 的 single owner，timeout/cancellation/fault 都沿現有 eviction/disposal path 處理。沒有 retry、cache、background task、partial success 或 legacy fallback。

此 capability 沒有 Controller registration 或 enabled consumer，因此 rollback 是刪除/停用 local composition，不會改變現行 runtime path。任何未來 CE evidence、consumer parity、traffic、P7.5/P8 仍需各自 gated。

