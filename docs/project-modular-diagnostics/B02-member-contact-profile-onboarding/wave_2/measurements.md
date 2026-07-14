# Wave 2 B02-SEC-001 量測合約

`CONTRACT_STATUS: WAVE_PLAN_APPROVED`

審查證據：Claude-only run `20260714-163002-wave2-b02-contract-reviewer` 無可用輸出；一次唯讀 Codex fallback re-review 為 `APPROVED`，無 Critical/Warning，確認兩個 Personal 寫入 action 在 CRM、`EnsureCorrectUserData`、`SetupListManager` 與背景派送前完成 principal/target/server-side Permit Gate，並受既有 `LoginClaimsFactory`、`CanViewContact`、`CanViewContactsBatch` 政策限制。

## 量測邊界

唯一量測 issue 是 `B02-SEC-001`，唯一端點是：

- `POST Personal/SaveMaintainPersonInfomation`
- `PUT Personal/UpdateMaintainPersonInfomation`

本量測不覆蓋 CSRF、avatar、LINE、個人自助端點、onboarding、效能、B01/X05Q session/route/role 或 live CRM。所有 fixture 為固定合成 GUID 與假欄位，禁止輸出姓名、真實 contact、電話、地址、生日、CRM response 或 session 值。

## Fixture 與 server-side Permit 狀態

| 代號 | GUID | actor/Permit 狀態 |
| --- | --- | --- |
| `SELF` | `10000000-0000-0000-0000-000000000001` | actor contact；SELF 本身沒有 self-service Permit。 |
| `SHEPHERD_ALLOWED` | `20000000-0000-0000-0000-000000000001` | 活躍，僅在 SELF 的有效 ShepherdList Permit 中。 |
| `CHURCH_ALLOWED` | `30000000-0000-0000-0000-000000000001` | 活躍，僅在 SELF 的有效 Church Permit 中。 |
| `CROSS_CONTACT` | `40000000-0000-0000-0000-000000000001` | 活躍，但不在 Shepherd Permit。 |
| `CROSS_CHURCH_SCOPE` | `50000000-0000-0000-0000-000000000001` | 活躍，但不在該 request 的 Church route/list scope Permit。 |
| `INACTIVE` | `60000000-0000-0000-0000-000000000001` | inactive，沒有正向 Permit。 |
| `MALFORMED` | `not-a-guid` | 不可解析 target。 |
| 假欄位 | `0900000000`、`測試路 1 號`、`2000-01-02` | 只供 fake assertion。 |

`SELF_AS_ROLE_MAINTAIN` 使用同一 `SELF` GUID，但為它簽發獨立的有效 Church 或 ShepherdList **維護** Permit。它不是 self-service；用來確保 actor=target 不會錯誤地取代既有角色授權或使合法 admin 流程失效。

## 計數定義

每個測試在呼叫 action 前重置 observer。以下計數涵蓋 action 內所有路徑，而非僅 `UpdateEntity`：

- `legacyHydrationCount`：`EnsureCorrectUserData`、`SetupListManager`、`SetPersonalInfomationViewModel`、InMemory lazy load 或等價 helper 的呼叫數。
- `retrieveContactCount`：所有 CRM contact retrieve/retrieve-multiple/query、`ToolUtility.RetrieveEntity` 與任何會從 CRM hydrate contact/名單的操作數。
- `crmWriteCount`：所有 CRM create/update/delete/associate 等寫入數。
- `backgroundDispatchCount`：任何 `Task.Run`、queue dispatch 或等價背景工作排程數。
- `permitReadCount`：只讀 server-side Permit 的次數；它不是 CRM touch。

拒絕 case 的固定斷言是 `legacyHydrationCount=0`、`retrieveContactCount=0`、`crmWriteCount=0`、`backgroundDispatchCount=0`。不允許以「先 hydrate 再拒絕」把計數歸零或排除在 observer 外。

## 基線

基線是對修復前兩 action 的受控 fake-CRM 執行與 source-path assertion，不使用真正 CRM：

| 基線 ID | 輸入 | 必須記錄的缺口 |
| --- | --- | --- |
| B1 | Shepherd + `CROSS_CONTACT` PUT | client `key` 可在沒有 Permit Gate 前走向既有 action 邏輯。 |
| B2 | Shepherd + `[SHEPHERD_ALLOWED, CROSS_CONTACT]` POST | client batch ContactId 可在沒有全量 Permit Gate 前走向既有背景處理路徑。 |
| B3 | 未驗證/無 actor claim + 任一有效 GUID | 若 global route/filter 截斷，記錄其 HTTP 結果；不得把它當作 selected action 的 Gate 證明。 |

若 pre-repair controller 無法以 fake observer 執行，記為 `BASELINE_HARNESS_BLOCKED`，附 source evidence 與原因；不可改用真實 CRM 或真實帳號。基線不是成功條件，post-repair 計數才是成功條件。

## 修復後的精確拒絕案例

下表每列都必須對指定 endpoint 執行一次。除另註外，HTTP 結果與四個零計數都必須完全符合；總共 15 個拒絕 assertion。

| ID | endpoint | actor / target | 必須結果 | `permitReadCount` |
| --- | --- | --- | --- | --- |
| R1 | POST | 匿名 / `SHEPHERD_ALLOWED` | `401 Unauthorized`；四個零計數 | 0 |
| R2 | PUT | 匿名 / `SHEPHERD_ALLOWED` | `401 Unauthorized`；四個零計數 | 0 |
| R3 | POST | 已驗證但無 `church:contactId` / `SHEPHERD_ALLOWED` | `403 Forbid`；四個零計數 | 0 |
| R4 | PUT | 已驗證但無 `church:contactId` / `SHEPHERD_ALLOWED` | `403 Forbid`；四個零計數 | 0 |
| R5 | POST | SELF 無維護 Permit / `[SELF]` | `403 Forbid`；四個零計數 | 1 |
| R6 | PUT | SELF 無維護 Permit / `SELF` | `403 Forbid`；四個零計數 | 1 |
| R7 | POST | Shepherd / `[CROSS_CONTACT]` | `403 Forbid`；四個零計數 | 1 |
| R8 | PUT | Shepherd / `CROSS_CONTACT` | `403 Forbid`；四個零計數 | 1 |
| R9 | POST | Church / `[CROSS_CHURCH_SCOPE]` | `403 Forbid`；四個零計數 | 1 |
| R10 | PUT | Church / `CROSS_CHURCH_SCOPE` | `403 Forbid`；四個零計數 | 1 |
| R11 | POST | Church 或 Shepherd / `[INACTIVE]` | `403 Forbid`；四個零計數 | 1 |
| R12 | PUT | Church 或 Shepherd / `INACTIVE` | `403 Forbid`；四個零計數 | 1 |
| R13 | POST | Shepherd / `[SHEPHERD_ALLOWED, CROSS_CONTACT]` | 整批 `403 Forbid`；四個零計數；不可部分成功 | 2 |
| R14 | POST | 有 actor / `[MALFORMED]` | `400 BadRequest`；四個零計數 | 0 |
| R15 | PUT | 有 actor / `MALFORMED` | `400 BadRequest`；四個零計數 | 0 |

`R13` 是混合 batch 的原子性證明：即使其中一筆有正向 Permit，整批仍無 CRM retrieve/write、無 legacy hydration、無背景排程。`R5`/`R6` 是 self-service 明確拒絕證明；它們不得因 target 等於 actor 而跳過 Permit。

## 修復後的授權且受界限案例

所有正向 case 只測一個 target 及一個真實 fake 欄位變更，且 server-side Permit 已在 action 前簽發。這些 case 才允許離開 Gate；不可用它們推論其他 contact 或 live CRM 可寫。總共 6 個允許 assertion。

| ID | endpoint | actor / Permit / target | 必須結果與上限 |
| --- | --- | --- | --- |
| A1 | POST | SELF + ShepherdList Permit / `[SHEPHERD_ALLOWED]` | 現有成功契約；`legacyHydrationCount` 可大於 0；`retrieveContactCount=1`；`crmWriteCount=1`；`backgroundDispatchCount=1`。 |
| A2 | PUT | SELF + ShepherdList Permit / `SHEPHERD_ALLOWED` | 現有成功契約；`retrieveContactCount=0`；`crmWriteCount=1`；`backgroundDispatchCount=0`。 |
| A3 | POST | SELF + Church Permit / `[CHURCH_ALLOWED]` | 同 A1 的計數上限與成功契約。 |
| A4 | PUT | SELF + Church Permit / `CHURCH_ALLOWED` | 同 A2 的計數上限與成功契約。 |
| A5 | POST | SELF + Church 或 ShepherdList 維護 Permit / `[SELF]` | 成功僅因角色 Permit；同 A1 的計數上限。 |
| A6 | PUT | SELF + Church 或 ShepherdList 維護 Permit / `SELF` | 成功僅因角色 Permit；同 A2 的計數上限。 |

`A1` 與 `A3` 的 `retrieveContactCount=1` 是一筆已通過 Gate 的既有 profile 比對讀取；`A2` 與 `A4` 的現行單筆寫入流程不需 contact retrieve。測試值必須確保確有欄位變更，因此每個允許 case 的 `crmWriteCount=1` 是固定而非「最多一次」。Permit 發行本身不在這些 action 的 observer 區間內。

## 指令、證據與不回歸

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~PersonalMaintainContactAuthorizationContractTests --logger "console;verbosity=detailed"
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter FullyQualifiedName~MemberInfoScopeGuardTests --logger "console;verbosity=detailed"
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --no-restore --logger "console;verbosity=minimal"
```

每個 action-case 產生一行不含個資的 evidence：

```json
{"issue":"B02-SEC-001","phase":"baseline|post-repair","case":"R13","endpoint":"POST SaveMaintainPersonInfomation","httpStatus":403,"result":"forbid","permitReadCount":2,"legacyHydrationCount":0,"retrieveContactCount":0,"crmWriteCount":0,"backgroundDispatchCount":0,"fixture":"MIXED","timestampUtc":"<UTC>"}
```

合格聚合：拒絕 15/15、允許 6/6、三條 test 指令全數通過。授權不回歸僅指有效 Church/ShepherdList Permit 的既有維護成功路徑、route、HTTP method、payload 欄位與成功契約保持可用；SELF 沒有 Permit 的自助寫入仍必須拒絕。

本機 evidence 是 local authorization proof。global route/filter、cookie principal 發行、部署 role 與真正 CRM active 狀態未由此驗證；若尚未在受控環境蒐證，標示 `DEPLOYMENT_ROUTE_ROLE_NOT_VERIFIED`，不得聲稱 live CRM 驗證。
