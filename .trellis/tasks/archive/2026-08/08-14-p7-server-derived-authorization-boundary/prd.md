# P7 server-derived immutable authorization boundary

## 目標與使用者價值

建立 ChurchReport 可重用、由伺服器驗證身分後推導、不可變且 request-local 的授權邊界。它要先移除
legacy Session／`InMemoryContext`／保存 credential／可變 CRM `Entity` 作為 Gateway authority 的前置依賴，
讓後續 P7 capability family 能在不造成跨使用者、跨 profile 或跨租戶洩漏的前提下被各自設計與遷移。

## 已確認事實

- current P7 matrix 有 70 rows，全部仍是 `temporary-legacy`；P7.4 direct safe consumer candidate 為零。
- ORG-CALL-00031／00032／00033、00047、00052、00060、00063 的現有 consumer 都在可證明的
  server-derived request-local scope 之前使用 Session、`InMemoryContext`、ListManager、保存 credential、
  caller locator、stored FetchXML 或可變 `Entity` graph。
- 歷史 P7.2 Slice C 是已 cleanup 的 `write-not-committed`／`no-go-closed` cycle，不能重播。
- 本 child 不是 consumer cutover、CE evidence、feature enablement、traffic、ToolUtility removal、P7.5 或 P8。

## 需求

1. 從既有 ChurchReport authentication/authorization flow 找出可安全重用的 server-owned principal source；
   browser、route、Session、`InMemoryContext`、saved credential、profile、connector 與 CRM object 均不得成為 authority。
2. 定義 immutable scope 與 bounded authorization-result contract。scope 需包含完整已驗證 isolation boundary，
   但不能保留 `HttpContext`、principal、token、credential、CRM entity 或可變 collection。
3. 在任何 cache、legacy manager、connector allocation、target lookup、stored query 或 outbound I/O 前，
   對缺失、歧義、expired 或不匹配 scope fail closed，並回傳固定去識別化分類。
4. 提供 request-local ownership、cancellation、fault、dispose 與 resource baseline 規則；不得新增 global mutable state、
   request-time fallback 或無界 scan。
5. 實作必須先以 TDD 證明 A/B interleaving、malformed/unauthorized locator、scope mismatch、cancellation/fault cleanup
   與 no-I/O-before-authorization；後續 consumer capability 仍需各自再取得 DTO、CE、rollback evidence。

## 不在範圍

- 不直接遷移 ORG-CALL-00031／00032／00033、00047、00052、00060 或 00063 consumer。
- 不執行 CE Create、Update、Assign、Delete、Associate、Disassociate、Action／Function、feature flag、traffic、
  ToolUtility removal、P7.5 或 P8。
- 不接受 browser input 選擇 CRM profile、organization、credential、connector、endpoint 或另一使用者 scope。

## 驗收條件

- [x] source audit 指出唯一可用的 server-owned principal source，並列出拒絕的 legacy authority paths。
- [x] immutable scope／result contract 不持有可變 user/profile/credential/CRM/request state，且可完整描述 isolation boundary。
- [x] unit／integration tests 證明 authorization 在 locator parse、cache、manager、connector 與 CRM I/O 前完成，
  同時證明 A/B isolation、fault/cancellation cleanup 與 bounded diagnostics。
- [x] disabled-by-default integration seam 不會改變現有 traffic 或提供 request-time legacy fallback。
- [x] 完成 targeted tests、Release build、full solution test、encoding／CRLF、scope review、CCG/Trellis check、
  task-owned commit/archive；不將本機成果升格為 consumer、CE、host、traffic、P7.5 或 P8 evidence。
