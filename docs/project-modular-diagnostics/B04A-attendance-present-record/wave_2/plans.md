# B04A Wave 2 實作合約

CONTRACT_STATUS: WAVE_PLAN_APPROVED

## 審查核准紀錄

- Claude-only review runner：`20260715-093908-wave2-b04a-contract-reviewer` 兩次皆為 `no-usable-output`；它不構成核准，artifact 位於 `.ccg/dual-model-runs/20260715-093908-wave2-b04a-contract-reviewer/`。
- 依 workflow 僅執行一次允許的唯讀 Codex fallback re-review；結果為 `APPROVED`，Critical=`None`、Warning=`None`。fallback 核對 A、B1、B2、C1、C2 五個唯一宣告，並確認 `Q0` no-data branch 現況可達 A，且本計畫已將該可達性列為 repair 必須移除的禁止 query path。
- 此為 implementation contract approval，不是產品 repair、local validation、staging validation 或 runtime proof 完成聲明。

## 固定範圍

- Wave：`Wave 2 / B04A-attendance-present-record`。
- 唯一選定 issue：`B04A-SEC-001`、`B04A-SEC-002`。
- 明確排除：`B04A-SEC-003`、`B04A-PERF-001`、`B04A-PERF-002`、`B04A-EXT-001`、未確認的 `B04A-SEC-004`、`B04A-EXT-002`，以及 B01、B02、B04B、B04C、X05Q 的產品、測試、設定與文件工作。
- 此合約只定義未來 repair 的最小邊界；本 planning wave 不授權修改產品碼、測試、設定或部署環境。

## MVC 路由與 issue 對應

`Startup.Configure` 使用 conventional MVC default template：`{controller=Authentication}/{action=Login}/{id?}`；下列 action 沒有 `[Route]` attribute，也沒有較特定的 `SmallGroup` map。因此 controller token 為 `SmallGroup` 時，exact effective route template 與省略 optional `id` 的 canonical route 如下。`values`、`key`、optional `id` 均不可作為授權資料。

| Issue | Controller / action | HTTP method | Exact effective route template | Canonical route | Request fields |
|---|---|---|---|---|---|
| B04A-SEC-001 | `SmallGroupController.InsertPresentRecord` | POST | `/SmallGroup/InsertPresentRecord/{id?}` | `/SmallGroup/InsertPresentRecord` | `values` |
| B04A-SEC-001 | `SmallGroupController.UpdateSmallGroupPresentRecord` | PUT | `/SmallGroup/UpdateSmallGroupPresentRecord/{id?}` | `/SmallGroup/UpdateSmallGroupPresentRecord` | `key`, `values`, `cancellationToken` |
| B04A-SEC-001 | `SmallGroupController.DeletePresentRecord` | DELETE | `/SmallGroup/DeletePresentRecord/{id?}` | `/SmallGroup/DeletePresentRecord` | `key` |

`B04A-SEC-002` 沒有獨立 MVC action 或 HTTP route：它是 `DownloadIntegrateData` 的 private query chain，必須在每個到達該 chain 的 read flow 中套用本合約的 read guard。

## 最小 future repair allowlist

允許 future repair 修改或新增的最小產品/測試範圍：

1. `SpeechMessageProducts.ChurchReport/Controllers/SmallGroupController/SmallGroupController.Crud.cs`
2. `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs`
3. `SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.Members.cs`
4. `ChurchReport.Tests/B04A/AttendancePresentRecordSecurityContractTests.cs`（新增的合成 fixture contract test；SDK default compile glob 下不修改 project file）

`Startup.cs` 僅為 route evidence，不在 allowlist。不得修改任何 view、upload/weekly-report processor、model、CRM schema、cache/config、B01/B02/B04B/B04C/X05Q 或其他測試。若既有 test harness 使第 4 項無法獨立編譯，停止 repair 並將 project-file prerequisite 列為 blocker，不得擴大 allowlist。

## B04A-SEC-001：寫入授權上下文

三個 route 必須在第一次觸碰 `InMemoryContext.ListManager`、任何 shared projection、cache/manager、CRM command 或通知前，依序建立同一不可由 client 建構的 `AuthorizedAttendanceMutationContext`：

1. 從 server-side authenticated principal 解析 principal ID；匿名固定回 `401 AUTH_REQUIRED`。
2. 驗證 anti-forgery token，無效固定回 `400 ANTIFORGERY_INVALID`；驗證 session fresh/version 與 server login context 一致，失效固定回 `401 SESSION_STALE`。
3. 從 server-side principal/session 解出 role 與 canonical list scope，確認 list active；沒有 write role 固定回 `403 ROLE_NOT_GRANTED`。
4. 僅用 server canonical IDs 查出 target record/member/contact，確認其 active 且屬於 allowed list/record scope；同 list 但未授權 record 固定回 `403 RECORD_NOT_IN_PRINCIPAL_SCOPE`，跨 list 固定回 `404 RECORD_NOT_IN_AUTHORIZED_LIST`，inactive target/list/contact 固定回 `404 ATTENDANCE_TARGET_INACTIVE`。
5. context 必須固定帶入 principal ID、role grants、session version、canonical list ID、canonical record/contact ID、operation 和 anti-forgery validation result；下游 mutation 只能接受此 context。

client `key`、`values`、list ID、contact ID、display name、query string 只可供資料比對，不能選擇 scope 或提升權限。guard rejection 時所有 named mutation side-effect counter 必須為零。

## B04A-SEC-002：query chain、local projection 與 create 禁令

現有 source call graph 為：

`DownloadIntegrateData.GetAllMemeberDataList(ListEntityId, WeeklyReportEntityId, ref report)`
-> 在有 `WeeklyReportEntityId` 時 `GetAllMemberDataFromPresentRecordOptimized(GroupName, WeeklyReportId, ref report)`
-> `GetPresentRecordByLoginType(GroupName, WeeklyReportId, ref report)`
-> no-match 時目前 `DownloadIntegrateData.CreatePresentRecordList(...)`
-> `CreatePresentRecord` -> `CreateEntity` -> `RetrieveEntity` -> `AssignOwner`。

`GetAllMemberDataFromPresentRecordOptimized` 也會透過 `ProcessPresentRecordEntityWithCache` 對傳入的 `ref ListSmallGroupWeeklyReport` 的 `m_SmallGroupDataList.m_AllMemeberData.Members` 做 assembly。修復後只允許下列受限行為：

- read guard 先完成 authenticated identity、fresh session、role/list/weekly-report/contact ownership 與 active-state checks；未通過時不得建立 response-local projection。
- 呼叫端必須傳入新建的 request-local `ListSmallGroupWeeklyReport` snapshot；它不得與 `InMemoryContext.ListManager.m_ListSmallGroupWeeklyReport` 或其任一 nested projection 參考相同，也不得被放入 shared manager 或 cache。
- 允許在這個 request-local snapshot 中 reset 一次 `m_AllMemeberData` 並加入 query result member，僅作為 response assembly；response 完成後不得保存、merge 或 publish 該 snapshot。
- 禁止 query path 直接或間接呼叫 `CreatePresentRecordList`、`CreatePresentRecord`、`CreateEntity`、`UpdateEntity`、`DeleteEntity`、`AssignOwner`、marketing-list member add/remove、notification send、background enqueue，或寫入 session/cache/manager/shared projection。
- authorized no-match 固定回 `200 PRESENT_RECORD_QUERY_EMPTY` 與空集合；不得建立、assign 或更新任何 state。authorized hit 固定回 `200 PRESENT_RECORD_QUERY_OK`。

每次 query 都必須在 request 前後擷取同一個 `SharedStateSnapshotHash`。canonical hash payload 僅含合成 ID/結構資料：每個 shared list/projection 的 stable synthetic ID、member/record ID set hash、count、`ModifyFlag`、manager reference/version、cache key set 與 value hash、session key set/value hash、CRM mutation operation log、notification/background queue log；不得含真人姓名、聯絡方式、token 或 CRM payload。每一 query（含 N=10 repeat）必須滿足 `beforeHash == afterHash == baselineHash`。

## CreatePresentRecordList 完整 symbol inventory 與 command boundary

下列 inventory 是以 `rg -n -i 'CreatePresentRecordList(ByList)?\\s*\\(' SpeechMessageProducts.ChurchReport --glob '*.cs'` 的直接靜態結果為準。每個 line/region、declaration、caller 與 reachability 必須在 repair 前後再次比對；不得以近似名稱、reflection 或新的 alternate path 繞過。

定義三條受限 query graph：

- `Q0`：`DownloadIntegrateData.GetPresentRecordByLoginType(string GroupName, Guid WeeklyReportId, ref ListSmallGroupWeeklyReport report)` 本身。
- `Q1`：`DownloadIntegrateData.GetAllMemeberDataList(string ListEntityId, string WeeklyReportEntityId, ref ListSmallGroupWeeklyReport report)` -> `GetAllMemberDataFromPresentRecordOptimized(string GroupName, Guid WeeklyReportId, ref ListSmallGroupWeeklyReport report)` -> `Q0`。
- `Q2`：`DownloadIntegrateData.GetAllMemberDataFromPresentRecord(string GroupName, Guid WeeklyReportId, ref ListSmallGroupWeeklyReport report)` -> `GetAllMemberDataFromPresentRecordOptimized(...)` -> `Q0`。

`Q0`、`Q1`、`Q2` 都禁止 direct 或 indirect reach 本節所有五個 create symbols，也禁止 reach `ExecuteAuthorizedPresentRecordCreate`。下面的「允許 command path」不是新增 public route；它只描述可到達該 legacy worker 的既有或明確內部 command boundary。

### A. DownloadIntegrateData：本波 create-on-read worker

- Declaring type / partial class：`ChurchReport.WebServiceConnector.DownloadIntegrateData`（`public partial class DownloadIntegrateData`）。
- Declaration：`private EntityCollection CreatePresentRecordList(string GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, double ValidNumber, double aWeeklySundayRate, double aWeeklySmallGroupRate, int aWeeklySundayNumber, int aWeeklySmallGroupNumber)`。
- Exact source：`SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs:69-92`，在 line 64 起始的 present-record creation `#region`。
- Current direct caller：`DownloadIntegrateData.GetPresentRecordByLoginType(string GroupName, Guid WeeklyReportId, ref ListSmallGroupWeeklyReport aListSmallGroupWeeklyReport)`，`SpeechMessageProducts.ChurchReport/WebServiceConnector/DownloadIntegrateData.PresentRecord.cs:30-60`，no-match branch line `59`。
- Forbidden query reachability：`Q0`、`Q1`、`Q2` 全部禁止；這正是 B04A-SEC-002 必須移除的 direct call。
- 唯一允許 command path（repair 後）：`mutation dispatcher` -> `ExecuteAuthorizedPresentRecordCreate(AuthorizedAttendanceMutationContext context, PresentRecordCreateRequest request, PresentRecordCreateIdempotencyKey key)` -> 此 worker。這個 dispatcher 必須已建立 canonical authorization context；沒有第二個 caller 或 public query/action route 可到達 worker。

### B. UploadIntegrateData：既有 weekly-report command workers，排除修改

- Declaring type / partial class：`ChurchReport.WebServiceConnector.UploadIntegrateData`（`public partial class UploadIntegrateData`）。
- Declaration B1：`private EntityCollection CreatePresentRecordList(SmallGroupData aSmallGroupData, String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)`。
- Exact source B1：`SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs:32-66`，present-record creation `#region` starting line `30`。
- Current direct caller B1：`UploadIntegrateData.CreateWeeklyReportAndPresentRecord(String GroupName, GroupWeeklyReportGuid aGroupWeeklyReportGuid, ref String WeeklyReportEntityId, ref Entity aListEntity, String UploadCategory, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, SmallGroupData aSmallGroupData, String WeeklyReportData, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)` in `SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs:133-188` calls B1 at `:161-166` when its login-type branch selects it.
- Forbidden query reachability B1：`Q0`、`Q1`、`Q2` must not reach B1, including through any new adapter or delegation.
- Allowed command path B1：only the existing `UploadIntegrateData.CreateWeeklyReportAndPresentRecord(...)` command path above. It is excluded from this wave and must not be modified or made a query callee.

- Declaration B2：`private EntityCollection CreatePresentRecordListByList(SmallGroupData aSmallGroupData, SmallGroupData aSmallGroupDataFromList, String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber, ref GroupWeeklyReportGuid aGroupWeeklyReportGuid, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)`。
- Exact source B2：`SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.PresentRecord.cs:135-202`，same present-record creation `#region` starting line `30`，line `135` declaration。
- Current direct caller B2：同一個 `UploadIntegrateData.CreateWeeklyReportAndPresentRecord(...)`，`SpeechMessageProducts.ChurchReport/WebServiceConnector/UploadIntegrateData.WeeklyReport.cs:173-178`，在 alternate login-type branch。
- Forbidden query reachability B2：`Q0`、`Q1`、`Q2` must not reach B2.
- Allowed command path B2：only the same existing `CreateWeeklyReportAndPresentRecord(...)` command branch; excluded and not mutable in this wave.

### C. WeeklyReportProcessor：既有 weekly-report command workers，排除修改

- Declaring type：`ChurchReport.Tools.WeeklyReportProcessor`。
- Declaration C1：`private EntityCollection CreatePresentRecordList(String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, Double aWeeklySundayRate, Double aWeeklySmallGroupRate, int aWeeklySundayNumber, int aWeeklySmallGroupNumber, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)`。
- Exact source C1：`SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs:548-575`，present-record-list `#region` beginning at line `547`。
- Current direct caller C1：`WeeklyReportProcessor.CreateWeeklyReport(ref Entity aListEntity, ref Dictionary<String, String> WeeklyReportDictionary)` at `SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs:393-455`, call line `444`.
- Forbidden query reachability C1：`Q0`、`Q1`、`Q2` must not reach C1.
- Allowed command path C1：only the existing `WeeklyReportProcessor.CreateWeeklyReport(...)` path above; excluded and not mutable in this wave.

- Declaration C2：`private EntityCollection CreatePresentRecordListByList(String GroupName, ref Entity aListEntity, ref Guid aWeeklyReportId, Double ValidNumber, ref Double aWeeklySundayRate, ref Double aWeeklySmallGroupRate, ref int aWeeklySundayNumber, ref int aWeeklySmallGroupNumber, ref int ValidSundayMemberNumber, ref int ValidSmallGroupMemberNumber, String HappyWeekIndex, String HappyWeekTopic, bool PauseCheckBox)`。
- Exact source C2：`SpeechMessageProducts.ChurchReport/Tools/WeeklyReportProcessor.cs:576-605`，同一 `WeeklyReportProcessor` region。
- Current direct caller C2：none found by the complete direct static symbol search; the only current match is declaration at line `576`.
- Forbidden query reachability C2：`Q0`、`Q1`、`Q2` must not reach C2.
- Allowed command path C2：none in the current repository. This wave must not add one; any future command caller is outside this contract and requires separate review.

修復後的 exact command boundary remains `ExecuteAuthorizedPresentRecordCreate(AuthorizedAttendanceMutationContext context, PresentRecordCreateRequest request, PresentRecordCreateIdempotencyKey key)` in `DownloadIntegrateData.PresentRecord.cs`. It validates canonical list/report/contact IDs, uses `(listId, weeklyReportId, contactId, operation)` as its idempotency lookup, and only then reaches A. It returns record ID plus `CREATED` or `ALREADY_EXISTS`. It may not delegate into B1, B2, C1, or C2; query paths may reach none of A/B1/B2/C1/C2 or this command boundary.

## 驗證與回滾

執行 `dotnet test ChurchReport.Tests/ChurchReport.Tests.csproj --no-restore`，再執行 repo 已有的 ChurchReport build command。測試只使用 synthetic fixture 與 fake CRM/state/notification providers，絕不連線 CRM 或使用真人資料。

整個 rollback boundary 僅限本 allowlist 的 controller guard、query/local-snapshot boundary、former query create call 與新增 contract test。任何 rejection side effect、shared-state hash 變更、query create、route/authorized flow regression、build/test failure 均使 wave unsuccessful，必須回滾這些 future repair 變更並保留量測/review artifact。
