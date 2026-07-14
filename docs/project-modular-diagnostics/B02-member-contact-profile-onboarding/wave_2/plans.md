# Wave 2 B02-SEC-001 修復合約

`CONTRACT_STATUS: WAVE_PLAN_APPROVED`

審查證據：Claude-only run `20260714-163002-wave2-b02-contract-reviewer` 無可用輸出；一次唯讀 Codex fallback re-review 為 `APPROVED`，無 Critical/Warning，確認兩個 Personal 寫入 action 在 CRM、`EnsureCorrectUserData`、`SetupListManager` 與背景派送前完成 principal/target/server-side Permit Gate，並受既有 `LoginClaimsFactory`、`CanViewContact`、`CanViewContactsBatch` 政策限制。

## 身分與不可變範圍

- Wave：`Wave 2`
- 工作區：`B02-member-contact-profile-onboarding`
- 唯一授權 issue：`B02-SEC-001`
- canonical evidence：`docs/project-modular-diagnostics/B02-member-contact-profile-onboarding/issue.md`

本合約只修復下列兩個 Personal 維護寫入 action 的 object-level contact authorization：

| 類別 | action / HTTP | client 提供的目標 |
| --- | --- | --- |
| 批次維護寫入 | `PersonalController.SaveMaintainPersonInfomation` / `POST` | `aResult[*].ContactId` |
| 單筆維護寫入 | `PersonalController.UpdateMaintainPersonInfomation` / `PUT` | `key` |

明確排除：B02-SEC-002、B02-SEC-003、B02-PERF-001、B02-PERF-002、B02-EXT-001；所有 avatar、CSRF、個人自助、onboarding、View、route/filter/configuration、CRM schema、背景工作改善、共用 CRM/授權重構；以及 B01/X05Q 的 login、cookie、session、claim、global authorization、route/role 與部署設定。本 issue 不驗證 live CRM、真實 contact 或真實小組資料。

## 寫入前的狹窄預授權 Gate

兩個 action 都必須以同一個 `B02 維護預授權 Gate` 作為第一個可觀察的業務步驟。Gate 成功以前，嚴禁呼叫任何可能 hydration、載入名單或觸及 CRM 的 legacy 程式，包括但不限於：`EnsureCorrectUserData`、`SetupListManager`、`SetPersonalInfomationViewModel`、`InMemoryContext` 的 lazy load、`ToolUtility`、`MemberInfoController` helper、`CanViewContact`、`GetAccess`、`GetShepherdContactIds`、任何 CRM retrieve/query/update，以及 `Task.Run`。

Gate 只可讀取下列不觸及 CRM 的輸入；任何缺失或不相符一律 fail closed。

1. **Actor**：讀取 `HttpContext.User.Identity.IsAuthenticated`，再讀取既有 cookie principal 的 `church:contactId` claim（`LoginClaimsFactory.ContactIdClaim`）並嚴格解析 GUID。它是 actor 的唯一身分來源；不可用 request body、query、header、referer、UI 列資料、帳號字串或 client user/contact id 補足。
2. **Target**：只剖析 `key` 或 `aResult` 中每一個 `ContactId` 成 GUID；這些值只用來查核 server permit，從不是 ownership 或 role authority。`POST` 必須完整剖析、去重並檢查全數 target，才可能進入後續步驟。
3. **Scope 與 active status**：只讀取由伺服器簽發、綁定 actor 的 `B02 Maintain Permit`。Permit 是 server-side session/cache 中的不可由 client 寫入之記錄，鍵至少綁定 `(actorContactId, targetContactId, action-class, permit-version)`；值至少含 `Church` 或 `ShepherdList`、`activeAtIssue=true`、來源 list/scope version、簽發時間、短時效與一次性 nonce。Permit 不含電話、地址、生日、姓名或其他 contact 資料。
4. **Permit 發行**：同一 `PersonalController` 的既有維護資料讀取路徑，在已依既有流程得出可顯示且可維護的 target 後，才可把正向 permit 寫入 server-side storage。它只能為該 actor、該 route/list scope、在籍且非結案 target 發行 permit；不得由 client payload、hidden field 或 client cache 建立。這只補足 selected write action 的前置授權快照，不改變或擴張任何讀取 route 的資料範圍。

現有 principal 只有 contact identity claim，沒有既有的 role、名單或 active claim。因此修復不得在 write action 以 legacy helper/CRM 補查這些資料。若可用的 server-issued Permit 不存在、過期、actor 不相符、scope/version 不相符、不是 `Church`/`ShepherdList`，或 `activeAtIssue` 不是 true，Gate 必須直接拒絕。若無法在不增加 B01/X05Q 或全域授權工作的前提下建立這個 server-side Permit，修復工作必須停止為 blocker，而不能退回到 hydration 後再決定是否拒絕。

### 決策與回應語義

| Gate 結果 | `POST SaveMaintainPersonInfomation` | `PUT UpdateMaintainPersonInfomation` | CRM/hydration 計數 |
| --- | --- | --- | --- |
| 未驗證 principal | `401 Unauthorized` | `401 Unauthorized` | 皆為 0 |
| 已驗證但缺少/無效 actor claim | `403 Forbid` | `403 Forbid` | 皆為 0 |
| 空白或格式錯誤 target | `400 BadRequest` | `400 BadRequest` | 皆為 0 |
| target 不在有效 Permit、scope/version 不符或 inactive | `403 Forbid` | `403 Forbid` | 皆為 0 |
| POST 批次含任一上述拒絕 target | 整批 `403 Forbid`（若全部 target 格式錯誤則 `400 BadRequest`） | 不適用 | 皆為 0 |
| 全部 target 有效 Permit | 才可進入原有處理 | 才可進入原有處理 | 僅此列可非 0 |

這些拒絕回應是安全失敗語義；成功時才維持既有 action 名稱、HTTP method、payload 欄位、可寫欄位與成功回應契約。不得以「先呼叫 legacy helper、失敗再拒絕」或「先查 CRM 再確認 scope」取代 Gate。

## 角色與 SELF 語義

Permit 的正向內容必須等價於目前 `MemberInfoController.CanViewContact` / `CanViewContactsBatch` 的政策，但只限本 action 可維護的 route/list scope：

- `Church` permit：對該 action scope 內、簽發時在籍且非結案的 target 有效。
- `ShepherdList` permit：對該 actor 伺服器端牧養名單內、簽發時在籍且非結案的 target 有效。
- 任何其他 access、未簽發 Permit、CRM 讀取失敗後無法簽發 Permit，均不允許寫入。

`SELF` 對兩個 selected action 的**自助身分例外一律不允許**：actor ID 等於 target ID 本身不會產生 Permit，也不會放行 `POST` 或 `PUT`。若 actor 同時以獨立的 `Church` 或 `ShepherdList` 維護角色取得有效 Permit，對自身 target 的成功是「角色維護」而非 self-service；測試必須分別驗證無 Permit 的 SELF 拒絕，以及有角色 Permit 的 SELF 維持既有 admin 行為。

## 未來修復 allowlist

修復子代理只能建立或修改下列檔案；未列檔案一律禁止修改。

| 類別 | 路徑 | 最小變更 |
| --- | --- | --- |
| 產品 | `SpeechMessageProducts.ChurchReport/Controllers/PersonalController.cs` | 在兩個 selected write action 最前方加入 Gate；讓既有維護讀取流程只簽發 server-side Permit，不改變其輸出資料/route；Gate 成功後才呼叫既有 legacy/CRM 寫入邏輯。 |
| 測試 | `ChurchReport.MemberInfo.Tests/Security/PersonalMaintainContactAuthorizationContractTests.cs` | 以 fake principal、server-side Permit store 與 fake CRM observer 覆蓋本合約所有 baseline/post-repair cases；fixtures 僅用固定測試 GUID 與假欄位。 |

只可讀取、不得修改的政策依據：

- `SpeechMessageProducts.ChurchReport/Security/LoginClaimsFactory.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoAccess.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoAccessResolver.cs`
- `SpeechMessageProducts.ChurchReport/Services/MemberInfo/MemberInfoScopeGuard.cs`
- `SpeechMessageProducts.ChurchReport/Controllers/MemberInfoController.cs`
- `ChurchReport.MemberInfo.Tests/MemberInfoScopeGuardTests.cs`

禁止修改 `BaseChurchController.cs`、`MemberInfoController.cs`、security/filter/configuration、View、Startup、onboarding 或任何 B01/X05Q 檔案。若 PersonalController 內無法以既有 principal 與 route-local server-side permit 完成 Gate，回報 blocker；不得藉機新增全域 authorization 或 CRM service。

## 最小實施順序

1. 在維護讀取結果已由既有流程產生後，以 actor claim、route/list scope 和正向 active 狀態簽發短效 server-side Permit；不傳送 permit 到 client，且 issuer 不讀取 client 選取結果。
2. `POST` 先完整 parse/validate target 集合，`PUT` 先 parse `key`；然後只讀 principal 與 Permit store 完成 Gate。拒絕路徑不得呼叫 legacy helper 或 CRM。
3. `POST` 只在**所有** target 都有有效 Permit 時才呼叫 `EnsureCorrectUserData` 或排程背景工作；任一 target 拒絕即不排程且不部分更新。
4. `PUT` 只在 Gate 成功後才剖析 `values`、建立 CRM entity 或呼叫 `UpdateEntity`。
5. 不更動 fire-and-forget 成功語義、CSRF、View 或共享授權架構；若 Permit freshness/invalidation 無法被本 controller 安全保證，停止而非延長 Permit 或回退到 CRM pre-check。

## 驗證、部署區隔與回滾

執行與證據規格在 `measurements.md`；修復後至少執行：

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~PersonalMaintainContactAuthorizationContractTests
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~MemberInfoScopeGuardTests
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore
```

本機測試只證明 Gate 與 fake CRM observer 的本機授權契約。部署時的 global route、cookie/filter、role 發行與實際 CRM 狀態必須由受控環境另行蒐證；未蒐證時只能標示 `DEPLOYMENT_ROUTE_ROLE_NOT_VERIFIED`，絕不可聲稱 live CRM 驗證。

回滾邊界只有本 allowlist 的 Gate/Permit 邏輯與合約測試。若任一拒絕 case 發生 legacy hydration、CRM retrieve/write 或 background dispatch，若 Permit 可由 client 偽造/重放，若合法 Church/Shepherd role-maintain 被破壞，或若需碰觸未列檔案，立即停止 wave 並回滾本 issue 的變更；不得回滾他人工作或變動 CRM 資料。
