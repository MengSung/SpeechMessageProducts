# P7 MemberInfo 伺服器擁有指派證據來源設計

## 資料流與隔離邊界

```text
已驗證 Cookie principal
  -> P7GatewayRequestScopeResolver
  -> MemberInfoServerAssignmentEvidenceSource
  -> 固定 ProductClient operation
  -> Data8 executor（同一 lease 內固定 contact/list query）
  -> immutable assignment wire/DTO
  -> assembly-internal MemberInfoTargetAuthorizationEvidence
  -> MemberInfoTargetAuthorizationScopeResolver
```

完整 isolation boundary 為 `(subjectContactId, ChurchReport, authorization scope, profileAlias,
generationId)`。subject 只由唯一 Cookie claim 取得；profile/workload 僅由 deployment composition
傳入。list ID 是 server query 的結果，不是 browser request 的 authority。任何一段不能證實時，
在 connector allocation/CRM I/O 前或目前 query 的 bounded response validation 後停止，且不保存 request
state、entity、lease、cookie、credential 或 response。

## 固定 operation

operation ID 為 `memberinfo.authorization.assignment.resolve.by.subject`，kind 是 read。request 僅承載
已驗證 subject contact GUID，不能承載 access mode、list ID、date、role、query、owner、profile、endpoint 或
credential。Data8 executor 的 operation definition 固定 CE 9.1、固定 list schema、固定 max 512 list IDs
及 top count 513 sentinel。

executor 先 direct-retrieve contact 的 `new_church_jobtitle`。若 role 是 Church-wide，直接回傳 Church-wide
assignment response 而不查 assignment list。否則以一個固定 OR filter 查詢六個 list lookup，並在 server
側固定 `statecode=0`、`purpose="小組名單"`、`new_app_named=true`。query 僅投影 list ID 與有效日欄位；
每筆由 server-owned `TimeProvider` 的 local current time 套用既有 inclusive date rule。實際回覆必須不超過
512 個 unique non-empty list IDs；top 513、paging、日期型別錯誤、duplicate、null response 或未知 logical name
都使 operation fail closed。

這重現 legacy tree 最終的可見 list 邊界，而不是重用 `ListManager`。現有 generic app-named catalog 缺少四個
assignment lookup 與有效日欄位，因此不能被加以推論或作為 fallback。

## 分層責任

| 層 | 責任 | 禁止事項 |
| --- | --- | --- |
| Abstractions/registry | operation、bounded request/response discriminator | generic CRUD、caller-selected query |
| Data8 executor | 固定兩步 query、型別/row/page/bound validation、lease cleanup | Session、claims、cache、response reuse |
| ProductClient | routing validation、exact operation/branch、defensive DTO copy | authorization policy、Entity rehydration、fallback/retry |
| ChurchReport source | `P7GatewayRequestScope` + deployment routing + internal evidence | browser locator、Session/ListManager、CRM SDK |
| target scope resolver | 完整 evidence → immutable target scope | CRM I/O 或 server source guessing |

## 錯誤與資源行為

所有 public failure 是封閉分類：unauthenticated/invalid request scope、source unavailable、unsupported
assignment response、incomplete/ambiguous assignment、invalid list bound。不得記錄 subject、list ID、job title、
profile、endpoint、credential 或上游 exception。取消原樣傳遞；timeout/fault/unsuccessful connector result 由
Data8 executor 標記 faulted lease 並按照 pool contract 釋放。source adapter 不配置 cache、timer、queue、
background work 或 disposable resource。

## 相容性與後續

此 child 不修改 `MemberInfoController.GetAccess()`、`GetShepherdListIds()` 或 consumer route，故 legacy
行為不變。完成後只解除「可以重新稽核」00031/00032/00033 的 source prerequisite；它不授權 controller
cutover、CE evidence、feature gate、capacity enablement、ToolUtility removal、P7.5 或 P8。
