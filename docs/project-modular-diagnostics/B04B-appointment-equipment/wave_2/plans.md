# Wave 2 修復契約：B04B-SEC-001

CONTRACT_STATUS: WAVE_PLAN_APPROVED

## 審查核准證據

- Claude-only self-healing run `20260714-170234-wave2-b04b-contract-reviewer` 健康檢查通過，但兩次 Claude attempt 均為 `no-usable-output`；未取得 Claude verdict，未呼叫 Gemini。
- 依 Wave workflow，已進行一次唯讀 Codex fallback re-review；其比對 canonical issue、三份 Wave 2 契約與既有 Appointment／Equipment／OAuth 路徑後判定 `APPROVED`，無未解決 Critical 或 Warning。
- 此核准僅適用 B04B-SEC-001 的本三份不可變契約；不授權任何額外 issue、產品修改或範圍擴張。

## 不可變範圍

- 波次：全域 Wave 2 / B04B `wave_2`
- 工作區：`B04B-appointment-equipment`
- 唯一 canonical issue：`B04B-SEC-001 Appointment LINE binding can mint identity from caller-supplied LINE user id`
- 診斷依據：`issue.md`（SHA-256 `c0f21f29833ea2c73f45a00bba27951054331b5b4ceacb6278a121b351dba3cf`）及 `wave-execution-workflow.md`。

本契約只修復 B04B 預約／裝備入口將請求提供的 LINE 身分提升為工作階段或授權身分的問題。明確排除：`B04B-PERF-001`、`B04B-PERF-002`、`B04B-PERF-RV-001`，以及 B04A、B04C、B01、X05Q 的任何議題、診斷、重構、設定或修復。不得藉此波次調整全域授權、session fallback、LINE webhook/signature 設定、一般登入設計、CRM schema、UI、通知或背景工作。

## 未來修復檔案 allowlist

下一個零信任修復工作只能新增或修改下列產品／測試檔案；任何新增檔案必須是列出的精確路徑。`appsettings*.json`、`Startup.cs`、`BaseChurchController.cs`、`LoginClaimsFactory.cs`、CRM connector、模型、View、JavaScript、路由組態及所有未列檔案均禁止修改。

| 類別 | 精確路徑 | 必要性與邊界 |
| --- | --- | --- |
| 產品 | `SpeechMessageProducts.ChurchReport/Controllers/AppointmentController.cs` | 將 LINE 綁定入口改為只消費伺服器能力；於每個選取預約讀寫入口（含 `Schedule`）前呼叫 gate。`SchedulerView` 保留為匿名 LIFF shell 時，必須改為 request-local/stateless render，不得寫 shared manager。不得再從 request 建立登入 cookie、session 或 manager 身分。 |
| 產品 | `SpeechMessageProducts.ChurchReport/Controllers/EquipmentController.cs` | 於選取裝備讀取與目前的預留寫入命令前呼叫同一 gate；不得實作現有 placeholder 所註解的 CRM 寫入。 |
| 產品 | `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.LineLoginOAuth.cs` | 僅在既有 server-side OAuth state 成功、code exchange 成功、由 LINE profile 取得 subject、且 active CRM binding 已確認的同一請求內，傳遞不可由 HTTP 繫結取得的 verified-LINE provenance。不得記錄或回傳 subject／token。 |
| 產品 | `SpeechMessageProducts.ChurchReport/Controllers/AuthenticationController/AuthenticationController.Private.cs` | 在既有有效登入完成、session rotation 後，簽發或清除 B04B 專用能力；LINE 分支必須要求上述 provenance，帳號分支必須以已驗證 contact 為來源。不得改變一般登入回應。 |
| 產品（新增） | `SpeechMessageProducts.ChurchReport/Security/AppointmentEquipmentAccessGate.cs` | 唯一 B04B 身分、角色、範圍與所有權 gate。只讀伺服器簽發的短效能力與已驗證 principal；不可接受 request 的 LINE/contact/appointment/group/room/equipment id 作為授權依據。 |
| 測試（新增） | `ChurchReport.MemberInfo.Tests/Security/AppointmentEquipmentAccessGateTests.cs` | 使用 fake capability store、fake policy scope 與副作用計數器驗證本契約的矩陣；不得連線 LINE 或 CRM。 |
| 測試（新增） | `ChurchReport.MemberInfo.Tests/Security/AppointmentEquipmentControllerAuthorizationTests.cs` | 驗證所有列出 action 在 manager、CRM 讀寫、cookie/session 改寫、通知或背景排程之前完成 gate。不得使用真實識別資料。 |

## 信任與授權契約

### 伺服器身分綁定

`AppointmentEquipmentAccessGate` 的唯一可信輸入是伺服器保存的短效、不可由客戶端提交或修改的 B04B capability。它至少綁定：已驗證 principal 的 contact key、LINE subject（僅在記憶體／伺服器 session 使用）、簽發時間與到期時間、登入來源、允許操作集合、允許的 appointment／group／equipment selector 範圍，以及 session-rotation binding。文件、日誌、例外、測試輸出與 HTTP 回應不得輸出 LINE subject、contact key、token、cookie 或完整 selector。

LINE capability 僅可由既有 `LineCallback` 的同一 server-side OAuth 流程在 state 驗證、token exchange、profile subject 取得及 active binding 查核均成功後簽發。它必須在登入 session rotation 完成後寫入，並與同一次已驗證 cookie principal 的 contact key、LINE login type 和 server-derived subject 相符。直接 `SaveUserLineId`、`ProcessLineLogin`、`LoadAppointmentByLineId` 或任何 query/body/form/route/header 值皆不可簽發、續期或改寫該 capability。

帳號登入能力只可從既有已驗證的 `loginContact` 建立；它不得把帳號密碼、request contact id 或任何前端角色字串寫入 capability。若現有資料模型無法以伺服器資料建立明確的 staff/admin grant，該 grant 不存在，必須 fail closed；不得以 `UserType`、姓名、DisplayId、group/room query 或 in-memory client 值猜測授權。

能力的 scope 是授權真相：self scope 僅含其伺服器派生的 own records；staff/admin scope 必須在簽發時由伺服器資料建立為具體範圍或明確操作 grant。`ScheduleType` 是不可信 selector，不是授權或角色來源；它只能在 capability 允許的 canonical schedule-type 集合中比對成功後使用。未知、過期、來源不符、session/principal 不符、未綁定或範圍不符皆為拒絕。拒絕必須在任何業務 CRM read/write、`AppointmentsListManager`／`ListManager` shared-state 寫入、calendar/equipment mutation、通知與背景工作前完成，且不改寫 auth ticket、`_LoginAccount`、`_LoginPassword`、`_SessionUserId` 或 B04B capability。

### Action gate 表

| Action／命令 | 身分 gate | 所有權／角色 gate | 拒絕前副作用 |
| --- | --- | --- | --- |
| `GET /Appointment/SchedulerView/{...}` | 保留匿名 LIFF shell 的最小修復選項：只建立 request-local ViewData/ViewModel；LIFF 路徑參數僅供顯示，不是 identity。 | 不讀取 appointment/equipment/CRM；不得呼叫保護 API。不得呼叫或模擬目前會寫入 `InMemoryContext.ListManager`／`AppointmentsListManager` 的 setup 方法。 | 0 CRM、0 session/auth/capability、0 `ListManager`/`AppointmentsListManager` state write、0 notification/job。 |
| `GET /Appointment/Schedule/{ScheduleType}` | 必須有有效 capability 與 principal/session binding。 | `ScheduleType` 先作純驗證，再與 capability 的 server-issued canonical schedule-type scope 比對；不符、未知或 client role 字串皆拒絕。驗證成功前不得呼叫 `SetupSchedulerViewBag` 或改寫任何 manager。 | 0 CRM、0 `ListManager`/`AppointmentsListManager` state write、0 session/auth/capability、0 notification/job。 |
| `POST LoadAppointmentByLineId` | 必須有有效 B04B LINE capability；`UserLineId` 若保留相容參數，只能為空或與 capability subject 相等，且永遠不作來源。 | `GroupId`、`RoomId`、`ViewType` 必須落在 capability 的 server-issued context scope；不符即拒絕。 | 0 cookie/session/manager identity 寫入、0 CRM。 |
| `GET LoadAppointments`、`POST NavigateAppointmentDate` | 有效 capability 與 principal/session binding。 | 讀取範圍及日期導覽只限 capability 的 appointment/calendar scope。 | 0 CRM read、0 manager mutation（導覽的 request-local date mutation 亦不得發生）。 |
| `POST PostAppointments` | 有效 capability。 | 必須有 `AppointmentCreate`；申請人／owner 從 capability contact 派生，忽略 client owner/contact 欄位。 | 0 CRM create/update/delete、0 calendar mutation。 |
| `PUT PutAppointments`、`DELETE DeleteAppointments` | 有效 capability。 | appointment id 只可作 selector；必須在 own scope 或明確 staff/admin mutation scope，且在 manager/CRM 前檢查。 | 0 CRM read/write、0 manager mutation。 |
| `GET EquipmentView`、`GET LoadEquipmentList` | 有效 capability。 | `EquipmentRead`；list id 只能選 capability 內的 group/list scope。 | 0 CRM read、0 state reload。 |
| `GET LoadEquipmentContact`、`GET LoadEquipmentStorLessons` | 有效 capability。 | group id／present-record id 只能選 capability 內的 group/member scope。 | 0 CRM read、0 `SetupIntegrateData`、0 cache/manager mutation。 |
| `POST UpdateEquipmentStatus`、`POST AddEquipmentLesson` | 有效 capability。 | 要求明確 `EquipmentWrite` 及 contact/member scope。現況為 no-op placeholder，修復後仍不得新增實際 CRM 寫入。 | 0 equipment mutation、0 CRM、0 notification/job。 |
| `GET ExportEquipmentReport`、`GET GetEquipmentSummary` | 有效 capability。 | `EquipmentRead` 及 group scope；現況 placeholder 回應保持無敏感資料。 | 0 CRM、0 export/background job。 |

本次 scoped B04B source 未發現已選取的通知或背景工作命令。修復不得在成功或拒絕分支新增它們；後續若有任何通知／job，必須先走同一 gate 並另開議題。

## 修復步驟與驗證

1. 先以 fake-only 測試定義 capability issuer、principal/session binding、scope policy、所有 action guard 與拒絕時零副作用。
2. 實作 `AppointmentEquipmentAccessGate`：它回傳僅含安全拒絕碼的 allow/deny 結果；controller 將其轉為既有 Ajax 的 401 或 403，且不揭露是否存在特定 subject 或 record。
3. 將 OAuth provenance 經既有有效登入路徑交給 capability issuer；直接 caller-supplied LINE path 不得建立 capability。`LoadAppointmentByLineId` 不再呼叫 `IssueAuthTicketAsync`，亦不得以 request 值設定 session 或 manager identity。
4. `Schedule` 必須先在 request-local 值上驗證 `ScheduleType`，再通過 capability scope gate；只有成功後才可呼叫現有會寫入 manager 的 setup。`SchedulerView` 則選擇 stateless shell：用 request-local display model 取代會寫入 shared display/role state 的 setup，維持 LIFF shell 回應但不建立資料範圍。
5. 將表內其餘 action 的 guard 放在第一個可能存取 manager、CRM 或狀態的敘述之前。資料 selector 僅在 gate 成功後使用；更新／刪除需再次做 record scope 檢查，不可相信目前記憶體清單。
6. 在 controller tests 以明確的 fake sink/state-write recorder 或前後 shared-state snapshot 量測 `ListManager`、`AppointmentsListManager`、session/auth/capability、CRM、notification/job 寫入。任何 reject 及匿名 `SchedulerView` shell 必須全部為 0。
7. 僅執行下列本機驗證並保存不含敏感資料的 case id、計數、exit code 與 diff allowlist：

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~AppointmentEquipmentAccessGateTests|FullyQualifiedName~AppointmentEquipmentControllerAuthorizationTests" --logger "console;verbosity=minimal"
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LoginClaimsFactoryTests|FullyQualifiedName~GlobalAuthorizationFilterTests" --logger "console;verbosity=minimal"
rg -n --glob '*.cs' 'LoadAppointmentByLineId|IssueAuthTicketAsync|_LoginPassword|_SessionUserId|RetrieveContactEntityByLineUserId|CreateAppointment|UpdateAppointment|DeleteAppointment|SetupIntegrateData|RetrieveStorLessonsByFetchXml' .\SpeechMessageProducts.ChurchReport .\ChurchReport.MemberInfo.Tests
git diff --check
git diff --name-only
```

預期：兩個新增測試群組全綠；第二個回歸群組全綠；靜態檢查顯示 B04B action（含 `Schedule`）的 sink 都在 gate 後、`SchedulerView` 不再呼叫 shared-state setup，且 `LoadAppointmentByLineId` 不再從 request 設定登入身分或簽發 ticket；`git diff --check` exit 0；檔名僅為 allowlist。

## 回退

回退僅能回退這個修復 commit，並同時移除 capability issuer、gate 與其測試；不得以重新開放 caller-supplied LINE identity、關閉全域授權、放寬 session fallback、保留部分 capability 或變更 CRM 資料作為回退。若回退後無法安全保留原 workflow，服務應維持拒絕受保護 B04B action，直到有經核准的替代修復。
