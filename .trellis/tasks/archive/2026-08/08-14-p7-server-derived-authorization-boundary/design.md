# P7 server-derived immutable authorization boundary 設計

## 邊界

此 child 只建立 shared prerequisite，不直接查詢或修改 Dynamics 資料。資料流固定為：

```text
server-authenticated request -> server-owned principal snapshot -> immutable validated scope
    -> authorization decision -> future DTO-only ProductClient capability
```

任何 browser locator 僅能在 scope 已成立後被當作 locator；它不能決定 subject、tenant、role、profile、
organization、credential、connector 或 target authorization。

## 最小型別契約

建立 `Security/P7GatewayRequestScope.cs`，包含以下純、無 I/O 型別：

```text
P7GatewayLoginKind = Account | Line
P7GatewayScopeFailure = Unauthenticated | InvalidAuthenticationScheme |
    MissingOrAmbiguousContactClaim | ConflictingContactClaim | UnsupportedLoginKind
P7GatewayRequestScope = (SubjectContactId: Guid, ProductBoundary: "ChurchReport", LoginKind)
P7GatewayRequestScopeResolver.TryCreate(ClaimsPrincipal?) -> resolution
```

resolver 只接受 cookie middleware 已建立的、唯一 authenticated Cookie identity；它必須同時取得唯一且
非空的 `ClaimTypes.NameIdentifier`、`church:contactId` 與 login type，將兩個 contact claim 以 GUID D 格式
正規化後精確比對。它只發布新的 scalar `Guid`、常數產品邊界與封閉 login-kind enum；不發布 account、
password key、`ClaimsPrincipal`、claim、Session、`HttpContext`、CRM object、profile、generation 或任何 collection。

shared scope 只證明「誰」與「哪一個產品」的 request-local identity baseline；它不能替代 future capability
的 target authorization。每個未來 capability 必須以 deployment-owned fixed profile/generation 與其 server-derived
target policy 建立完整 operation isolation boundary，缺少其中一項即在 connector allocation 前 fail closed。

## 安全與生命週期

scope 只保存 immutable、allowlisted scalar identity/isolation fields；它不保存 `HttpContext`、
`ClaimsPrincipal`、Session、CRM `Entity`、ListManager、credential、token 或集合 reference。所有 result、
allowlist 與 error category 均於 request 結束時釋放；取消、fault 或任何 scope ambiguity 在 outbound I/O 前
fail closed。此 child 不建立 cache；若未來需要 cache，須由獨立 child 證明 full isolation partition、TTL 與 eviction。

## 相容性與 rollout

新 seam 預設不被 consumer 呼叫。只有後續 capability child 在自身 deployment-owned gate、no-fallback、
DTO、CE/read-back、rollback 與 cleanup evidence 全數通過後，才可把新 scope 接到特定 consumer。回滾 owner
是該 future capability，而非此 shared prerequisite。

現有 Cookie ticket 暫時仍含 legacy working key，以維持既有登入行為；本 child 不讀取、複製、快取或
傳遞該 claim。完全移除 legacy ticket credential 是獨立 login-migration 工作，不能藉由此 shared scope
宣稱已完成。
