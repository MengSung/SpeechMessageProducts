# B04A Wave 2 量測合約

CONTRACT_STATUS: WAVE_PLAN_APPROVED

## 審查核准紀錄

- Claude-only review runner `20260715-093908-wave2-b04a-contract-reviewer` 兩次皆無可用輸出，未形成 Claude 核准。
- 後續恰有一次 workflow 允許的唯讀 Codex fallback re-review，結果 `APPROVED`，Critical=`None`、Warning=`None`；其核對範圍包含 A、B1、B2、C1、C2 inventory 與 Q0 到 A 的現況禁止可達性。
- 本核准只確認量測合約可交付；counter、hash、local test、staging 與 runtime proof 尚未執行或完成。

## 合成 fixture 與共通計數器

所有 case 使用固定 synthetic GUID 與匿名標籤：`SyntheticList-A`、`SyntheticList-B`、`SyntheticReport-A`、`SyntheticMember-A1`、`SyntheticMember-A2`、`SyntheticMember-B1`、`SyntheticMember-AInactive`。不使用真人姓名、電話、email、LINE ID、session token、CRM URL 或 CRM payload。

server fixture grants 固定如下：`Staff-A1` 具有 `AttendanceEditor`，且 server grant 只涵蓋 active `SyntheticList-A` 的 active `SyntheticMember-A1` / `SyntheticRecord-A1`；`Member-A1` 已登入但沒有 attendance mutation/query staff grant；A2 是同 list active 但不在 Staff-A1 record scope；B1 是另一 active list；AInactive 的 contact 或 list 為 inactive。每個 case 重置 fake state，client 可提供任意 name/ID/key，但 grant 只由 server fixture 解析。

每個 case 記錄下列 named counters：`sharedProjectionAdd`、`sharedProjectionUpdate`、`sharedProjectionRemove`、`listManagerWrite`、`cacheWrite`、`sessionWrite`、`crmCreate`、`crmUpdate`、`crmDelete`、`crmAssignOwner`、`crmMarketingListRemove`、`notificationDispatch`、`backgroundEnqueue`。CRM reads 可另記錄為診斷值但不是 write side effect。每個 reject 的上述 13 個 counter 都必須為 `0`。

每個 query 同時記錄：`responseLocalProjectionReset`、`responseLocalProjectionAdd` 與 `SharedStateSnapshotHash`。hash 輸入是 shared manager、所有 shared projection、cache、session、CRM mutation log、notification/background queue 的匿名 canonical structural snapshot；不含個資或 payload。

## B04A-SEC-001：固定 route matrix

下表每列都對三個 canonical routes 各執行一次，總數以 `allowed/rejected` 記錄；不得新增 batch endpoint。mixed-batch 是同一 test harness 依序送出的四個獨立 HTTP requests。

| Fixture case | Routes / expected status and reason | Allowed / rejected | 所有 named side-effect counters |
|---|---|---:|---|
| anonymous | POST/PUT/DELETE 各 `401 AUTH_REQUIRED` | 0 / 3 | 全部 0 |
| stale session | POST/PUT/DELETE 各 `401 SESSION_STALE` | 0 / 3 | 全部 0 |
| invalid anti-forgery | POST/PUT/DELETE 各 `400 ANTIFORGERY_INVALID` | 0 / 3 | 全部 0 |
| self `Member-A1` | POST/PUT/DELETE 各 `403 ROLE_NOT_GRANTED` | 0 / 3 | 全部 0 |
| cross-member `Staff-A1 -> A2` | POST/PUT/DELETE 各 `403 RECORD_NOT_IN_PRINCIPAL_SCOPE` | 0 / 3 | 全部 0 |
| cross-list `Staff-A1 -> B1` | POST/PUT/DELETE 各 `404 RECORD_NOT_IN_AUTHORIZED_LIST` | 0 / 3 | 全部 0 |
| inactive `Staff-A1 -> AInactive` | POST/PUT/DELETE 各 `404 ATTENDANCE_TARGET_INACTIVE` | 0 / 3 | 全部 0 |
| mixed-batch | A1 insert `200 ATTENDANCE_MUTATION_OK`; A2 update `403 RECORD_NOT_IN_PRINCIPAL_SCOPE`; B1 delete `404 RECORD_NOT_IN_AUTHORIZED_LIST`; AInactive delete `404 ATTENDANCE_TARGET_INACTIVE` | 1 / 3 | 僅 A1 insert 有下列預期計數；其餘三 request 全部 0 |

authorized staff path 以 reset fixture 分拆量測，不能只用 aggregate：

| `Staff-A1 -> A1` operation | Expected status / allowed | Exact shared state counters | Exact CRM / external counters |
|---|---|---|---|
| POST `/SmallGroup/InsertPresentRecord` | `200 ATTENDANCE_MUTATION_OK`, 1 / 0 | `sharedProjectionAdd=1`；其餘 shared projection counters=0；`listManagerWrite=cacheWrite=sessionWrite=0` | `crmCreate=crmUpdate=crmDelete=crmAssignOwner=crmMarketingListRemove=notificationDispatch=backgroundEnqueue=0` |
| PUT `/SmallGroup/UpdateSmallGroupPresentRecord` | `200 ATTENDANCE_MUTATION_OK`, 1 / 0 | `sharedProjectionUpdate=2`（small-group 與 all-member 各一）；其他 shared projection counters=0；`listManagerWrite=cacheWrite=sessionWrite=0` | `crmCreate=crmUpdate=crmDelete=crmAssignOwner=crmMarketingListRemove=notificationDispatch=backgroundEnqueue=0` |
| DELETE `/SmallGroup/DeletePresentRecord`，fixture 有有效 present-record ID | `200 ATTENDANCE_MUTATION_OK`, 1 / 0 | `sharedProjectionRemove=4`（all-member、small-group、new-person-follow-up、happy-group 各一）；add/update=0；`listManagerWrite=cacheWrite=sessionWrite=0` | `crmCreate=crmUpdate=crmAssignOwner=0`；`crmMarketingListRemove=1`；`crmDelete=1`；`notificationDispatch=2`；`backgroundEnqueue=0` |

每一 request 另保存 route、HTTP method、status、reason code、principal fixture、server canonical list/record fixture ID hash。改變 client `key`、name、list/contact ID 或 query field 不得把任何 reject case 變成 allow。

## B04A-SEC-002：query purity、local projection 與 repeat

query test 從 `GetAllMemeberDataList(..., WeeklyReportEntityId, ref requestLocalReport)` 進入，並證明傳入物件與 shared `InMemoryContext.ListManager` object graph 沒有 reference identity 重疊。下列 status/reason 為 query adapter 對呼叫端的固定結果；private legacy method 本身不新增 HTTP endpoint。

| Fixture case | Expected status / reason | Allowed / rejected | Local projection | 所有 shared mutation counters與 hash |
|---|---|---:|---|---|
| anonymous | `401 QUERY_AUTH_REQUIRED` | 0 / 1 | reset=0, add=0 | 13 個 counter 全部 0；`beforeHash=afterHash=baselineHash` |
| self `Member-A1` | `403 QUERY_ROLE_NOT_GRANTED` | 0 / 1 | reset=0, add=0 | 同上 |
| authorized hit `Staff-A1 -> A1` | `200 PRESENT_RECORD_QUERY_OK` | 1 / 0 | reset=1, add=1 | 13 個 counter 全部 0；hash 三值相等 |
| authorized no-match `Staff-A1 -> A1`, no record | `200 PRESENT_RECORD_QUERY_EMPTY` | 1 / 0 | reset=1, add=0 | 13 個 counter 全部 0；hash 三值相等；fake CRM log 無 create/assign |
| cross-member `Staff-A1 -> A2` | `403 QUERY_RECORD_NOT_IN_PRINCIPAL_SCOPE` | 0 / 1 | reset=0, add=0 | 13 個 counter 全部 0；hash 三值相等 |
| cross-list `Staff-A1 -> B1` | `404 QUERY_RECORD_NOT_IN_AUTHORIZED_LIST` | 0 / 1 | reset=0, add=0 | 13 個 counter 全部 0；hash 三值相等 |
| inactive `Staff-A1 -> AInactive` | `404 QUERY_TARGET_INACTIVE` | 0 / 1 | reset=0, add=0 | 13 個 counter 全部 0；hash 三值相等 |
| mixed-batch harness | A1 hit `200`; A2 `403`; B1 `404`; AInactive `404`，reason 如上 | 1 / 3 | aggregate reset=1, add=1 | 每個 request 的 13 個 counter=0；全部前後 hash 等於同一 baseline |

對 authorized hit 與 authorized no-match 各重複 N=10。每次 hit 必須為 `200 PRESENT_RECORD_QUERY_OK`、local reset/add=`1/1`；每次 no-match 必須為 `200 PRESENT_RECORD_QUERY_EMPTY`、local reset/add=`1/0`。每一次的 result canonical hash 必須與該 case 第一次相同，且每一次 `SharedStateSnapshotHash.before == .after == baselineHash`，所有 13 個 shared mutation counters 維持 0。fake CRM operation log 必須沒有 direct 或 indirect `CreateEntity`、`AssignOwner`、update/delete、marketing-list change 或 notification/background dispatch。

## 本地證據與隔離限制

本地執行：`dotnet test ChurchReport.Tests/ChurchReport.Tests.csproj --no-restore`，以及 repo 既有 ChurchReport build command。測試輸出保存 case ID、status/reason、counter table、before/after/baseline hashes、result hash 與 source graph scan 結果；只可寫到 repair-run artifact location。這些是 local proof，不是部署證明；本波不得使用 staging 或 production CRM/person data。
