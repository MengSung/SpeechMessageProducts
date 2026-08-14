# MemberInfo request-local server-derived target authorization scope

## 目標

在既有 `P7GatewayRequestScope` 的登入者 identity baseline 之上，定義 MemberInfo
所需的目標授權邊界。目標授權必須由伺服器驗證來源推導、不可變、request-local，
並在任何 Session、`InMemoryContext`、legacy `ListManager`、CRM Entity 或外部 I/O
前完成；若無法證明 Church／Shepherd 的授權範圍，必須固定分類 fail closed。

## 已確認限制

- 不重開或修改已封存任務，不重播歷史 P7.2 Slice C。
- 不執行 CE mutation、feature gate、traffic、consumer cutover、P7.5 或 P8。
- 不掃描 CRM、不猜選 Owner／小組／使用者、不把 browser locator 當 authority。
- 既有 `P7GatewayRequestScope` 僅提供 subject contact GUID、產品邊界與 login kind。
- 目前 MemberInfo controller 的 `GetAccess` 與 Shepherd list IDs 仍讀 Session、
  `InMemoryContext`、credential-bearing `ListManager` 或 CRM SDK bridge。

## 必要契約

1. scope DTO 不得保存 `HttpContext`、`ClaimsPrincipal`、Session、credential、token、
   CRM Entity、可變集合或跨 request state。
2. scope 必須明確區分 Church-wide 與 Shepherd-list target authorization；login type
   本身不得直接被當成 target authorization。
3. 缺少、重複、衝突、過期、無法從 server-owned source 證明的授權，一律固定分類拒絕。
4. 必須有 A/B interleaving、claim ambiguity、source unavailable、invalid target、
   cancellation/fault 與 no-I/O-before-authorization 測試。
5. 新 scope 預設不接 controller、不切 consumer、不建立 CE 證據；後續 capability 自行
   負責 DTO、CE、read-back、rollback 與 cleanup evidence。

## 驗收條件

- [x] PRD、design、implement 與 CCG task record 完整且無 TBD。
- [x] 明確指出可用與不可用的 server-owned authorization source。
- [x] immutable target scope/result contract 與固定去識別化 failure 分類完成。
- [x] focused tests 證明跨使用者隔離、fail closed 與零 I/O 前置條件。
- [x] Release build、相關測試、UTF-8 無 BOM、CRLF、`git diff --check` 與 scope check 通過。
- [x] 不宣稱 consumer、CE、P7.5 或 P8 已完成。
