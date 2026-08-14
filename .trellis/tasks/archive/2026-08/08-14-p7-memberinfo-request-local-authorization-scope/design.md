# P7 MemberInfo target authorization scope 設計

## 設計結論

本 child 只建立「授權結果的形狀與 fail-closed 邊界」，不把目前的 legacy
assignment data 直接搬進新型別。原因是目前 repository 沒有能同時證明 Church
職稱授權與 Shepherd 多種 assignment 關係的 server-owned source。既有
`list.catalog.retrieve.appnamed.smallgroups` 只包含名單描述與部分 leader lookup，
不足以重建 `DownloadListManager.FindListCollection` 的六種 relationship 查詢。

因此採用兩層邊界：

```text
Cookie principal
  -> P7GatewayRequestScope (已完成：subject identity baseline)
  -> server-owned target evidence provider (本 child 只定義 contract)
  -> immutable MemberInfoTargetAuthorizationScope
  -> future locator/DTO/ProductClient capability
```

沒有 provider 或 provider 回報證據不完整時，不建立 target scope；不得退回
Session、`InMemoryContext`、`ListManager`、browser locator、CRM Entity 或第一筆
名單。

## 型別與資料流

### `MemberInfoTargetAccessMode`

只允許 `ChurchWide` 與 `AssignedLists`。此 enum 不是由 browser 或 login type
字串直接解析；它必須由受信任的 source provider 產生。

### `MemberInfoTargetAuthorizationScope`

不可變、request-local DTO，包含：

- `SubjectContactId`：必須與 `P7GatewayRequestScope.SubjectContactId` 完全相同。
- `AccessMode`：封閉 enum。
- `VisibleListIds`：只有 `AssignedLists` 才可有；使用 bounded、defensive-copy 的
  immutable GUID collection；ChurchWide 不依賴此集合。

此 DTO 不含 principal、Session、credential、profile、connector、endpoint、raw CRM
Entity、週報、cache 或 mutable list reference。

### `MemberInfoTargetAuthorizationResolution`

固定回傳 `Scope` 或 null，以及去識別化 `Failure`：

- `None`
- `MissingRequestScope`
- `SourceUnavailable`
- `SubjectMismatch`
- `UnsupportedAccessMode`
- `InvalidOrDuplicateTarget`
- `IncompleteAssignmentEvidence`

Failure 不得包含 CRM ID、名稱、端點、帳密、token、原始例外或 baseline。

### source provider seam

未來 provider 必須是 server/deployment-owned，輸入只能是已建立的
`P7GatewayRequestScope` 與 request cancellation token；不得接受 browser owner、
list ID、profile、connector、endpoint 或 credential。provider 完成後只交付
immutable evidence，resolver 驗證 subject、mode、GUID 唯一性、上限與證據完整旗標。

evidence 的建立 factory 必須維持 assembly-internal，不得以 public factory 讓 controller、
browser adapter 或其他 consumer 偽造「完整」來源。測試組件只能透過明確
`InternalsVisibleTo` seam 產生 fixture；production provider 仍必須在 ChurchReport assembly
內完成 server-owned source 驗證後才可建立 evidence。

本 child 不註冊 provider、不接 controller，避免產生假授權來源。若未來要將
`ORG-CALL-00031/00032` 接入，必須另立 capability child，先完成固定 operation、
Data8 executor、typed ProductClient、authorization source、read-back、rollback
與 CE evidence。

## 安全與生命週期

- resolver 是純 request-local 邏輯，不使用 static mutable state、cache、Session、
  background task、timer 或 connection。
- collection 在 scope 建立時 defensive copy 並封閉；呼叫端不得改變 source evidence。
- cancellation、source fault、scope mismatch 與 cleanup uncertainty 均在外部 I/O
  前或 provider ownership boundary 內 fail closed；不得 retry 或 legacy fallback。
- A/B requests 只能看到自己的 subject 與 target IDs；不允許跨 request reuse。

## 相容性與 rollout

新型別預設沒有 consumer 呼叫。現有 MemberInfo 路由保持既有行為與 disabled seam，
直到後續 child 取得完整 source 與自身 evidence；本 child 不變更 traffic、feature
gate、CE、P7.5 或 P8 狀態。

## 驗證策略

測試必須涵蓋：有效 Church scope、有效 bounded Shepherd scope、subject mismatch、
empty／duplicate／invalid IDs、Church 帶有不應有 list IDs、assignment evidence
不完整、source unavailable、A/B interleaving、cancellation/fault 與 reflection
檢查不得保留 request／credential／CRM state。另以 source contract test 證明
controller 未被接線、沒有 legacy fallback，且缺少 provider 時不會產生 scope。
