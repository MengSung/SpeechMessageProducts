# P7 server-derived immutable authorization boundary 實作計畫

## Phase 1：研究與設計收斂

- [x] 完成 principal source、legacy authority path 與可重用 safe pattern 的 source audits。
- [x] 以 45 秒／單次限制執行 CCG dual-model architect analysis；期限內無 usable output，記錄「雙模型未完成」且不重送。
- [x] 把 audit 事實寫回 PRD/design，建立精確檔案清單、scope/result API 與 fail-closed error matrix。

## Phase 2：TDD 與實作

1. [x] 在 `ChurchReport.MemberInfo.Tests/Security/P7GatewayRequestScopeResolverTests.cs` 寫 scope validation 的 fail-first tests：
   authenticated Cookie identity、duplicate/missing/conflicting claims、unsupported login kind、legacy password claim 忽略與 no shared state。
2. [x] 在 `SpeechMessageProducts.ChurchReport/Security/P7GatewayRequestScope.cs` 實作 immutable server-derived scope、
   resolution 與 pure request-local resolver；不得讀取 legacy Session／InMemoryContext、DI、cache、profile、connector 或 CRM。
3. [x] 在同一 focused suite 寫並通過 interleaved A/B scope、scope mismatch、malformed locator precondition、
   cancellation/fault 無資源 owner、以及 no-fallback tests；不得以 `DefaultHttpContext` 假裝完整 server lifecycle。
4. [x] 以 reflection/source contract 證明 scope 不保留 `ClaimsPrincipal`、`HttpContext`、credential、CRM entity 或 collection，
   且本 child 沒有 controller/bootstrap wiring；這是 default-disabled seam，不得切 consumer、feature gate、CE 或 traffic。

## Phase 3：檢查與封存

- [x] 執行 focused test project、Release build、full solution tests、encoding／CRLF、`git diff --check` 與 scope check。
- [x] 執行 45 秒／單次 CCG reviewer run；Gemini 產生可用 output、Claude 無 usable output，故記錄「雙模型未完成」並完成本機 review。
- [ ] 更新 task/parent records，scope-only commit/archive；接著由 matrix 選取第一個依賴此 scope 的 capability child。
