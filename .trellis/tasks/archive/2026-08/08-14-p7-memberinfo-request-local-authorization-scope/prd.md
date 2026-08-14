# P7 MemberInfo request-local target authorization scope

## 目標

在既有 `P7GatewayRequestScope` 的 subject identity baseline 之上，建立 MemberInfo
可使用的 target authorization contract。此 contract 必須由 server-owned、可驗證的
來源推導、不可變且 request-local，並在 Session、`InMemoryContext`、legacy
`ListManager`、CRM Entity 或任何外部 I/O 前完成授權判斷。

這是 P7 的安全前置 child，不直接完成 MemberInfo consumer cutover，也不宣稱 CE、
feature gate、traffic、P7.5 或 P8 已完成。

## 已確認事實

- `P7GatewayRequestScope` 只投影唯一 Cookie principal 的 contact GUID、固定
  `ChurchReport` product boundary 與 `Account`／`Line` login kind。
- `LoginClaimsFactory` 的 cookie claims 沒有 Church job title 或 shepherd list
  assignment；其中 password-key claim 仍屬舊登入相容資料，不得複製到新 scope。
- `MemberInfoController.GetAccess()` 目前從 Session、`InMemoryContext` 與 legacy
  ToolUtility Entity 推導 Church／Shepherd access。
- `GetShepherdListIds()` 與相關 member projection 依賴 credential-bearing
  `ListManager` weekly-report records 及 CRM SDK bridge。
- `list.catalog.retrieve.appnamed.smallgroups` 的 typed catalog 雖已具備 local
  ProductClient，但其 response 只有名單描述與部分 leader 欄位，不能證明目前
  `ListManager` 的完整多種 assignment 關係。
- 因此目前沒有足夠 repository evidence 可安全宣稱「牧養者可見名單集合」或
  「全教會職稱授權」已可由新 scope 產生。

## 需求

1. 定義 immutable target scope/result contract，保留完整 isolation boundary 所需
   的 server-derived subject 與固定 target mode；不得保存 principal、HttpContext、
   Session、credential、token、CRM Entity、可變 collection 或跨 request state。
2. 明確區分 Church-wide 與 Shepherd-assigned-list authorization；login kind 不得
   直接被當成 target authorization。
3. 缺少、重複、衝突、過期、來源不可用或 assignment evidence 不完整時，回傳固定
   去識別化 failure，且在 locator parse、cache、manager、connector 或 CRM I/O 前
   fail closed。
4. 對未來 source provider 留下 disabled-by-default、無 request-time fallback 的
   seam；本 child 不把 typed catalog 或 legacy data 自動提升為完整 assignment source。
5. 以 TDD 證明 A/B interleaving、subject mismatch、duplicate target、invalid target、
   source unavailable、cancellation/fault 與 no-I/O-before-authorization。

## 不在範圍

- 不修改 MemberInfo controller、ViewComponent、consumer、feature gate 或 traffic。
- 不讀取或改寫 Session、`InMemoryContext`、legacy `ListManager`、CRM Entity 或
  credential 來建立新授權。
- 不執行 CE mutation，不建立 CE ledger／fixture，不重播歷史 P7.2 Slice C。
- 不把本 child 的 contract、unit tests 或 source audit 宣稱為 consumer、CE、host、
  P7.5 或 P8 evidence。

## 驗收條件

- [x] PRD、design、implement 與 CCG task records 完整，無 TBD／未決的 repository-answerable 問題。
- [x] 設計明確列出可用 source、拒絕 source 與未來 source provider 的必要 contract。
- [x] immutable target scope/result contract 只發布固定 scalar／bounded immutable IDs。
- [x] focused tests 證明跨使用者隔離、固定 failure、零 I/O 前置條件與 deterministic cleanup。
- [x] targeted tests、Release build、encoding／CRLF、`git diff --check` 與 scope check 通過。
- [x] parent matrix 仍正確標示 00031／00032／00033 consumer 未遷移、CE evidence pending，
  且 P7.5／P8 gates 不變。
