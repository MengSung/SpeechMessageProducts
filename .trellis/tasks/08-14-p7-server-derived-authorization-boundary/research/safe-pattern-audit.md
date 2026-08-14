# Research: safe-pattern-audit

- Query: 找出 ChurchReport 中能在 locator parse、快取／manager 取得、connector 配置或 CRM I/O 前建立 immutable、server-derived、request-local 授權的既有模式；評估 fee audit 與 MemberInfo，提出 P7 最小 scope/result 與測試建議。
- Scope: internal
- Date: 2026-08-14

## Findings

### 結論

目前沒有一條可直接搬用、且已同時符合 P7「不以 Session、`InMemoryContext`、保存 credential 或可變 CRM `Entity` 作 Gateway authority」的完整 controller-to-I/O 路徑。可重用的是兩種**局部模式**：

1. 以 Cookie middleware 已驗證的 principal 為唯一 request source，嚴格取用一個正規化 `ClaimTypes.NameIdentifier` 的演算法；以及
2. 將 server snapshot 複製為新的 read-only scalar／GUID 集合、遇到缺失或歧義即 fail-closed 的投影做法。

P7 應以第 1 項建立全新的 immutable scope resolver；不得將第 2 項或既有 session manager 當 authority。後續 consumer 在取得 scope 後，仍須以該 capability 專屬的 server-owned target authorization 取得 DTO；本 child 不足以授權 fee audit、MemberInfo 或任何 CRM target。

### 可重用的安全片段

| 檔案／型別 | 可重用部分 | 規格評估 |
|---|---|---|
| `SpeechMessageProducts.ChurchReport/Security/DiagnosticsOperatorAuthorization.cs:29-39,50-78` | Host 啟動時將 deployment allowlist 正規化為 `FrozenSet`；request 僅讀 Cookie principal 的唯一 `NameIdentifier`，拒絕未登入、空清單、遺失、重複、空或非法 GUID；不保存 principal。 | 最接近的 principal extraction 演算法。可重用「唯一 claim + GUID D 格式 + fail-closed + 只產生 scalar」；不可直接重用其 diagnostics operator allowlist 當一般產品權限。 |
| `SpeechMessageProducts.ChurchReport/Startup.cs:638-669,927` | Cookie 為預設 authentication scheme，且在 MVC 前執行 `UseAuthentication()`；`BaseChurchController.IssueAuthTicketAsync` 由 server 建立 principal 並 `SignInAsync`（`Controllers/BaseChurchController.cs:668-675`）。 | Cookie middleware 驗證後的 `HttpContext.User` 是目前唯一已確認可作新 resolver 輸入的 server-owned principal source；resolver 只能立即複製 allowlisted scalar，不能保留 `HttpContext`／`ClaimsPrincipal`。 |
| `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs:225-270,847-858,861-925,1087-1109` | deployment gate 先於 options/profile/host/client；固定 ProfileAlias 的 process host 只擁有一個 generation，停止時撤銷、drain/dispose。 | 可重用 deployment-owned profile/generation snapshot 與單一 host owner 的生命週期界線；它不是 user authorization，且新 resolver 不得為取得 scope 呼叫 `GetOrCreate*` 或配置 connector。 |
| `SpeechMessageProducts.ChurchReport/Services/FeeEditorLessonAccessResolver.cs:41-70,81-99` | 對已存在的 server snapshot 檢查 null、無效與重複 ID，複製成排序後的 `ReadOnlyCollection<Guid>`，然後才比對 target。 | 可重用 defensive-copy、ambiguity fail-closed 與「snapshot 先、locator 後」的 pure 函式形狀；不可重用其 caller 的 Session/FeeList identity。 |
| `SpeechMessageProducts.ChurchReport/Models/DonationFeeAuditReadResult.cs:34-62,86-123` | 複製 DTO 陣列再以 `ReadOnlyCollection` 發布，避免 backing array 可被轉型改寫。 | 可重用 result publication 的不可變／request-local 規則；它是 read result，不是 authorization scope。 |

### Fee audit：有價值但只屬 partial pattern

- `DonationFeeAuditAccessResolver.CanAccessFeeAudit` 是無欄位、無 I/O 的 fail-closed predicate（`Services/Donation/DonationFeeAuditAccessResolver.cs:32-65`）；其限定有效 `Entity.Id`、非空字串職稱與既有角色規則。這個「純 predicate 不持有輸入」原則可保留。
- 但 `DedicationAuditController.GetFeesByContactId` 在 predicate 前先呼叫 `EnsureCorrectUserData()`，並從 `InMemoryContext.PersonalInfomationModel.m_LoginContact` 取得可變 CRM `Entity`（`Controllers/DedicationAuditController.cs:376-378`）。其後才 parse browser GUID（`:388`）、取得 `DonationPaymentManager`（`:398`）並 dispatch（`:401-414`）。
- `EnsureCorrectUserData()` 本身讀 Session password、讀寫 static `_userValidationCache`，並可呼叫 `ListManager.SetupListManager`，還會把 LINE `passwordKey` 寫回 Session（`Controllers/BaseChurchController.cs:425-518`）。因此它已在 P7 scope 前進入 legacy credential／manager／cache 路徑；不能作為新的 authorization precondition。
- 既有 source contract test 只保證 resolver 位於 browser GUID／fee manager 前（`ChurchReport.MemberInfo.Tests/Controllers/DedicationAuditControllerFeeAuditContractTests.cs:30-49`），未保證它位於 `EnsureCorrectUserData`、Session、`InMemoryContext` 或 legacy cache 前，故不能當本 child 的 safety evidence。

### MemberInfo：object-level gate 可借鑑，來源與順序不可借用

- `CanViewContact`／`CanViewContactsBatch` 對 target 做精確 allowlist/狀態收斂，batch 版本用 chunk 避免 N+1（`Controllers/MemberInfoController.cs:2677-2787`）。這是後續 capability **在已有 P7 scope 後**可採用的 target-authorization 形狀。
- 它不是 P7 authority：`CanViewContact` 透過 `GetAccess()` 取得 Session-cached access（`:2684`；`GetAccess` 讀／寫 `_MemberInfoAccess` 於 `:1629-1655`），並以 `GetShepherdContactIds`、`IsCurrentContact` 進入 legacy 資料／CRM；batch version 直接使用 `ToolUtility.m_Crm2011OrganizationService`（`:2714-2771`）。這些均發生於完整 server-derived scope 尚未證明前。
- `MemberInfoAccessResolver.Resolve` 與 `MemberInfoScopeGuard` 是純函式（`Services/MemberInfo/MemberInfoAccessResolver.cs:18-37`；`Services/MemberInfo/MemberInfoScopeGuard.cs:19-79`），但前者接受 caller supplied string、後者接受 caller supplied collection，未自行證明資料來源或 immutable ownership；兩者只能作 scope 後的 local reducer，不能升格為 authorization source。
- `LoadContactPresentRecordsTypedAsync` 也先 `EnsureCorrectUserData()`，再 parse/contact authorization，最後才組 client（`Controllers/MemberInfoController.cs:804-821`）；相對順序優於 client allocation，但仍不符合本 child 禁止 legacy session/manager authority 的要求。

### 其他禁止直接重用的部分

- `GlobalAuthorizationFilter` 只檢查 `Identity.IsAuthenticated`，不驗證 subject claim、scope、profile 或 generation；更允許 Session identity fallback（`Filters/GlobalAuthorizationFilter.cs:23-75`）。checked-in 設定又將 `EnforceGlobalAuthorization` 設為 `false` 且 fallback 設為 `true`（`appsettings.json:75-78`）。它不可成為 P7 boundary。
- `LoginClaimsFactory.Build` 的確由 server 建立 Cookie principal，但票證含 `AccountClaim` 與 `PasswordKeyClaim`（`Security/LoginClaimsFactory.cs:9-26`）；後者在 LINE login 承載 working key（`ChurchReport.MemberInfo.Tests/Security/LoginClaimsFactoryTests.cs:30-36`）。新 scope resolver 只可讀取並正規化 `ClaimTypes.NameIdentifier`，不可複製 `ContactIdClaim` 以外未經一致性驗證的值，更不得讀取 `PasswordKeyClaim`、account、login type、token 或整個 principal。
- `FeeManagementController.GetFeeEditorRows` 的 gate-first、snapshot-before-locator、client-after-locator 順序是良好 local pattern（`Controllers/FeeManagementController.cs:377-428`；其 contract test 為 `...FeeManagementControllerFeeEditorReadContractTests.cs:28-78`），但它從 Session 取 account/password、讀寫 `InMemoryContext.FeeList`（`:388-396`）。`FeeList` 保存 account/password 並以它們決定 snapshot 是否同一登入（`Models/FeeList.cs:43-52,204-252`）。所以 P7 可借鑑其 copy/validation，不可把 FeeList、Session credential 或 lesson list 當 Gateway authority。

### 建議的最小 P7 scope/result contract

建立新的 pure request-local resolver；它的唯一輸入是 Cookie middleware 已驗證的 principal 與不會建立 client/lease 的 immutable deployment scope snapshot。建議概念契約如下（名稱可按實作調整）：

```text
TryCreateValidatedRequestScope(
  cookieValidatedPrincipal,
  deploymentScopeSnapshot)
  -> Authorized(ValidatedRequestScope) | Denied(FixedScopeFailure)

ValidatedRequestScope =
  SubjectId(Guid, canonical D) +
  TenantBoundary(optional, server constant or validated scalar) +
  ProductBoundary(constant) +
  AuthorizationScope(constant/minimal capability-independent scope) +
  ProfileAlias(server deployment scalar) +
  GenerationId(server deployment scalar)
```

必要不變量：

- 僅接受一個已驗證 Cookie identity 的一個非空 `NameIdentifier`；0/多個 identity、0/多個 subject claim、格式錯誤、空 GUID、profile/generation 缺失或不匹配一律回傳固定 `Denied` 分類。
- `ValidatedRequestScope` 是 sealed immutable scalar value object；不得保存 `HttpContext`、`ClaimsPrincipal`、Claim、Session、`IConfiguration`、`Entity`、manager、credential/token、collection、cancellation token、client/lease 或 cache entry。授權 result 只公布 fixed enum/category，不含原始 claim、profile、exception 或 locator。
- resolver 內不得 parse browser locator、讀 Session／`InMemoryContext`、查 cache、建立/取得 manager、profile resolver、connector、client、lease 或 CRM I/O；它不擁有需要 dispose 的資源。deployment snapshot 必須已由 composition root 建立、immutable、bounded，且讀取不會觸發 `DonationDynamicsAccessBootstrap.GetOrCreate*`。
- 這個 shared scope 只證明 authenticated subject 與完整 routing isolation boundary，**不**宣稱目前 fee audit 角色或 MemberInfo target 已被授權。不存在不依賴 legacy `Entity`／Session 的角色來源時，consumer 必須留在 disabled/no-go，直到後續 child 以固定 server-owned operation 建立 capability-specific target authorization。

### 必要測試

1. **Principal resolver unit tests**：Cookie identity + 單一合法 `NameIdentifier` 成功；未驗證、錯 authentication scheme、0/多 identity、0/多 claim、空／非法 GUID、subject/profile/generation mismatch 都固定拒絕。斷言 scope 只含 canonical scalar，且未讀取 `PasswordKeyClaim`。
2. **No-work-before-authorization contract test**：以 throwing/counting fakes 保護 locator parser、Session、`InMemoryContext`、cache、manager、profile resolver、client factory、connector/lease、CRM executor；每個 denial 都必須使其計數為零。controller/integration seam source contract 必須明確驗證 resolver invocation 位於上述 token 之前，而非僅位於 GUID parse 前。
3. **A/B interleaving isolation**：兩個相異 subject、profile/generation marker 交錯並行建立 scope；斷言 scope 物件與 diagnostics/result 無交叉 marker、無 shared mutable collection、無 static request state。profile 指向同一 organization 時亦必須保留不同 profile/generation。
4. **Locator and target tests**：只在 `Authorized` scope 建立後才 normalize GUID；malformed、ambiguous、scope-mismatch 或 target-unauthorized 全部回固定 denial，且不 target lookup、不 fallback、不 retry、不建立 client。
5. **Cancellation/fault/lifecycle tests**：拒絕與取消發生於授權前時 zero allocation；scope 後的 future dispatch 以 `await using`/`finally` 證明 lease/permit 剛好釋放一次，fault/timeout/cancelled connector 不可回池。重複 A/B/cleanup soak 後 counter 回 baseline。
6. **Seam/default-disabled tests**：checked-in gate 保持 false，false path 不取得 deployment profile、host、provider、handler、pool 或 CRM I/O；不得以 Session/legacy manager 作 request-time fallback。`DonationDynamicsAccessBootstrap` 現有 gate-first contract 可作測試樣板（`Services/DonationDynamicsAccessBootstrap.cs:232-270`；`ChurchReport.MemberInfo.Tests/AuthenticationContactReadBootstrapTests.cs:31-127`）。

## Files Found

- `SpeechMessageProducts.ChurchReport/Security/DiagnosticsOperatorAuthorization.cs` — 最嚴格的 Cookie claim + immutable deployment allowlist 模式。
- `SpeechMessageProducts.ChurchReport/Security/LoginClaimsFactory.cs` — Cookie principal 的 server-side 簽發點，亦揭示 password-key claim caveat。
- `SpeechMessageProducts.ChurchReport/Startup.cs` — Cookie middleware、pipeline 與目前 global authorization 設定。
- `SpeechMessageProducts.ChurchReport/Filters/GlobalAuthorizationFilter.cs` — 認證門檻的 partial/forbidden Session fallback。
- `SpeechMessageProducts.ChurchReport/Controllers/DedicationAuditController.cs`、`Services/Donation/DonationFeeAuditAccessResolver.cs` — fee audit 的 pure predicate 與其不符合 P7 的 legacy 前置順序。
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`、`Services/MemberInfo/MemberInfoAccessResolver.cs`、`Services/MemberInfo/MemberInfoScopeGuard.cs` — target allowlist/reducer 與其 legacy authority 限制。
- `SpeechMessageProducts.ChurchReport/Controllers/FeeManagementController.cs`、`Services/FeeEditorLessonAccessResolver.cs`、`Models/FeeList.cs` — snapshot-copy good pattern 與保存 Session credential 的禁止來源。
- `SpeechMessageProducts.ChurchReport/Services/DonationDynamicsAccessBootstrap.cs` — deployment profile/generation 與 process resource owner。
- `ChurchReport.MemberInfo.Tests/Controllers/DedicationAuditControllerFeeAuditContractTests.cs`、`.../FeeManagementControllerFeeEditorReadContractTests.cs`、`.../Payments/DonationFeeAuditAccessResolverTests.cs`、`.../Security/AdfsDiagnosticSecurityTests.cs`、`.../AuthenticationContactReadBootstrapTests.cs` — 可延伸的 source-order、claim fail-closed、gate-first 與 zero-I/O test 形狀。

## Related Specs

- `.trellis/spec/backend/cross-user-isolation-and-performance.md:23-156` — 完整 isolation boundary、authorization-before-I/O、cache/resource lifecycle 與 A/B/soak requirements。
- `.trellis/spec/backend/cross-user-isolation-and-performance.md:198-334` — fee-audit/browser locator 既有 scenario；本 P7 需採用其中 locator/result 不變量，但升級其 legacy request/session source。
- `.trellis/spec/backend/cross-user-isolation-and-performance.md:565-663` — credential verification 與 session handoff 必須分離，禁止 secret/Entity bridge。
- `.trellis/spec/backend/cross-user-isolation-and-performance.md:665-755` — P7 直接適用：scope 必須在 locator、`InMemoryContext`、profile/client/I/O 前完成。
- `.trellis/spec/guides/cross-user-isolation-and-performance-review.md:7-50` — 完整 boundary、single owner、A/B、fault/cancellation 與 performance review checklist。

## External References

- 未進行網路、CE 或外部系統動作；本 audit 僅依 repository source 與 Trellis specs。ASP.NET Core Cookie authentication 的實際使用以本 repository `Startup.cs:638-669,927` 為準。

## Caveats / Not Found

- 未找到現成的 `ValidatedRequestScope`、server-only deployment scope snapshot，或可在不讀 Session/`InMemoryContext`/CRM `Entity` 下提供 fee-audit/MemberInfo role 的 claim/policy。
- 未找到可證明「authorization 在 `InMemoryContext`／Session/cache/manager 之前」的 fee audit 或 MemberInfo integration test；現有 tests 只保護較晚的 GUID/client relative order。
- `GlobalAuthorizationFilter` 的 checked-in enforcement 為 false，且 session fallback 預設 true；即使 Cookie pipeline 存在，也不能把現有全域 filter 宣稱為 P7 protection。
- 本報告不授權 consumer cutover、CE、traffic、ToolUtility 移除或修改登入票證。特別是移除現有 `PasswordKeyClaim` 屬 credential/session architecture 變更，需另立 task 與安全設計。
