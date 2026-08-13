# P7.4 metadata boundary 審查修正設計

## 範圍邊界

這個 remediation 不建立新的 operation、DTO、client、route 或 feature gate。它只修正既有
`MemberInfoController` 與 `DonationDynamicsAccessBootstrap` 的 two-part quality boundary：公開 action 的
lifecycle 文件，以及 enabled Package02 composition 的 deployment ProfileAlias 驗證順序。

```text
Package02 gate=false
  -> null；不 bind options、不解析 host、不建立 provider/pool/handler

Package02 gate=true
  -> BindOptions(configuration)
  -> EnsureNonEmptyProductProfile(options)
  -> injected facade 或 CreatePackage02Executor(options, configuration)
  -> stateless Package02 client
```

ProfileAlias 是 deployment composition 的唯一 authority；呼叫端、HTTP request、Session、client facade、
connector、endpoint 與 credential 都不可替代它。先驗證再解析 host 可避免無效設定先建起 provider、handler、
pool 或 credential graph，之後才以例外回滾而延長資源生命週期。

## Action 文件契約

`LoadUngroupedMembers` 保留現有控制流。新增的 XML 文件只陳述既有不變量：

- `GetAccess` 與既有 Church scope 是 server-derived authorization；query 的 browser paging/sort/filter 值
  不能決定 profile、connector、owner 或 credential。
- Package03 base/sub-gate=false 只走 legacy metadata 相容路徑；true 時只使用一次 request-local typed
  metadata snapshot，任何 typed cancellation/fault 不 fallback 或 retry。
- legacy `IOrganizationService` 只由 action local 的 `service` 擁有，`finally` 必定 `ReleaseConnection`。
- typed DTO／metadata／exception／token 不進 static、cache、Session、singleton 或 background work，避免
  A/B user/profile/generation state leakage。

## 測試策略

先新增 Package02 enabled + blank ProfileAlias 的 lifecycle test。因現有 factory 尚未在 injected client
前驗證 profile，這個 test 必須 RED：預期 ProfileAlias validation，但實際會直接回傳 injected facade。
最小修正讓 factory bind and validate options，再處理 injected facade。GREEN 後保留既有 disabled-gate
short-circuit test，確保 false gate 不意外 bind/compose host。

`MemberInfoTreeControllerContractTests` 不增加 brittle source assertion；它只補齊現有測試的文件，因為
目前 action signature assertion 已涵蓋實際 public async contract。這項文件變更會以 C# XML comments 的
byte-level encoding/lint gate 驗證，不偽造一個非必要的行為測試。

## 回復與證據限制

回復是還原這三個檔案的 task-scoped commit；沒有外部資料、gate 狀態、process configuration 或 CE record
需要清理。此 task 的 green tests 僅證明 local source/lifecycle contract；不構成 CE execution、host parity、
traffic enablement、ToolUtility removal 或 Central Gateway deployment evidence。
