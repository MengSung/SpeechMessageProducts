# 技術設計：MemberInfo tree consumer 重新稽核

## 邊界

本 child 只產出來源／consumer 邊界判定，沒有 runtime implementation。下一個 child 若被允許建立，必須個別擁有其 Registry、Data8 executor、ProductClient、ChurchReport adapter、測試與 matrix update；本 child 不得預先接線或改寫 `MemberInfoController`。

| Matrix row | 現有依賴 | 可接受的新前置條件 | 目前決策方向 |
| --- | --- | --- | --- |
| 00031 descriptors | legacy access、Session、Church／Shepherd tree、`IOrganizationService` | immutable request-local scope、固定投影與數量/文字/位元組上限 | 可研究獨立 DTO descriptor data-plane child |
| 00032 memberships | 同上，且 membership/contact relationship 與 visible descriptor allowlist 交織 | 與 00031 相同 scope，且 descriptor allowlist 必須由 server-side result 導出 | 可與 00031 同一 data-plane child 研究，但不得直接切 consumer |
| 00033 relation goals | legacy target contact authorization、`CanViewContactsBatch`、relation helper、分頁累積 | 由 00031／00032 的 immutable DTO 結果導出的 target authorization、獨立 relation budget/error union | 暫時 no-go，不能進 implementation |

## 必要的安全資料流

若建立 00031／00032 implementation child，唯一容許的資料流為：已驗證的登入 subject → 固定 server-owned assignment evidence → immutable `MemberInfoTargetAuthorizationScope` → 零 caller-selected locator 的 fixed operation → Data8 bounded projection → ProductClient immutable DTO → default-disabled ChurchReport adapter。每一層都只能傳遞 immutable scalar 或 defensive-copied collection；不允許 credential、CRM `Entity`、Session、cookie、HttpContext、ListManager、query text 或 cache state 穿越邊界。

Church-wide 與 assigned-list scope 必須使用不同的 server-selected query branch。assigned-list branch 僅可使用 scope 已驗證且 bounded 的 list GUID 集合；空、重複、無效、來源不足或 subject 不一致全部 fail closed，且不得以 Church-wide 或 legacy query 回退。

## 回應與資源契約

- Query template、entity type、column projection、排序和所有 page/row/text/UTF-8 scalar-byte budget 必須由 registry 固定，呼叫端沒有自選 Filter、FetchXML、list ID、contact ID 或 page cookie 的權限。
- Data8 transport/lease 的 single owner 是 executor invocation；成功、取消、timeout、throw 與 schema mismatch 都必須沿既有 deterministic dispose/fault-eviction path 釋放，不得交給 ChurchReport consumer。
- 回應為 immutable DTO union：成功的完整 bounded snapshot，或去識別化的 fail-closed classification。不可把 partial page、原始 exception、Entity、GUID、名稱、cookie 或 endpoint 暴露給 consumer。
- A/B interleaving 測試必須證明 subject A 的 scope、result、fault 或 cancellation 不會被 subject B 讀到；測試 drain 後不得留下 session/profile/token/cache/lease/handler/registration/Task retention。

## 相容、回復與證據

所有新的 adapter 預設 disabled-by-default，不接 Controller，沒有 request-time fallback。因此 rollback 是移除未接線 local-only composition／維持 disabled，而不是在執行中轉回 legacy graph。完成本機實作後，僅能宣稱 local contract evidence；任何 CE evidence、consumer parity、traffic、P7.5 removal 或 P8 仍須獨立 gated。

00033 不得把新 assignment evidence 或新的 descriptor DTO 當成已經完成 relation-goal 授權。必須先有 server-derived target contact/list authorization mapping、bounded relation paging contract、schema validation、immutable error union 與 A/B tests，才可另建 re-audit child。
