# Wave 2 量測契約：B04B-SEC-001

CONTRACT_STATUS: WAVE_PLAN_APPROVED

## 審查核准證據

- Claude-only self-healing run `20260714-170234-wave2-b04b-contract-reviewer` 健康檢查通過，但兩次 Claude attempt 均為 `no-usable-output`；未取得 Claude verdict，未呼叫 Gemini。
- 依 Wave workflow，已進行一次唯讀 Codex fallback re-review；其比對 canonical issue、三份 Wave 2 契約與既有 Appointment／Equipment／OAuth 路徑後判定 `APPROVED`，無未解決 Critical 或 Warning。
- 此核准僅適用 B04B-SEC-001 的本三份不可變契約；不授權任何額外 issue、產品修改或範圍擴張。

## 量測原則

所有量測均為本機 fake-only contract test；不需要也不得呼叫 live LINE、LINE webhook、LINE access token、CRM、真實 cookie 或真實個資。fixture 僅使用不可對應真人的 symbolic subject/contact/appointment/group/member 值；測試報告只輸出 case id、allow/deny、HTTP 類別、sink 計數及 exit code。

每個拒絕 case 的「零副作用」固定定義為：業務 CRM read = 0、CRM create/update/delete = 0、appointment manager create/update/delete = 0、`AppointmentsListManager` state write = 0、`ListManager` display/role/schedule state write = 0、equipment reload/read = 0、session/auth ticket/capability 寫入 = 0、通知 = 0、background job = 0。純 fake gate 的記憶體檢查不算業務 CRM read；本契約設計要求它不做遠端 I/O。每一項均須由 fake sink/state-write recorder 或 action 前後 shared-state snapshot 記錄為獨立計數，不能以「未見例外」代替。

## 基線

基線為 canonical source 可重現的未修復資料流：`LoadAppointmentByLineId` 直接接受 `UserLineId`，將它寫入 B04B in-memory state 和 `_LoginPassword`／`_SessionUserId`，再簽發 LINE auth ticket；`Schedule` 在 selector 驗證前呼叫 setup 並可改寫 shared appointment/list display state；`SchedulerView` 目前也經由 setup 改寫 shared display/role state；appointment/equipment loader 以 `LineIdLogin` 的 password 查 CRM。全域設定目前將 `EnforceGlobalAuthorization` 設為 false。這不是可執行的攻擊測試，也不使用任何真實識別資料。

```powershell
rg -n --glob '*.cs' 'LoadAppointmentByLineId|SchedulerView|Scheduler\(|SetupSchedulerViewBag|SetupSchedulerViewForLine|SetupLineBindingContext|SetupAppointmentAccountPasswordAsync|IssueAuthTicketAsync|RetrieveContactEntityByLineUserId' .\SpeechMessageProducts.ChurchReport
rg -n '"EnforceGlobalAuthorization"|"AllowSessionIdentityFallback"' .\SpeechMessageProducts.ChurchReport\appsettings.json
```

預期基線證據：上述 source matches 存在，且僅記錄檔案／行號／match count，不複製或記錄任何 runtime subject。

## 修復後 contract matrix

測試群組必須執行 **14** 個命名 case，總計 **9 rejected / 5 allowed**。每個 case 均以同一個 fake sink recorder 驗證拒絕前零副作用；allowed case 只允許表列的預期 fake business operation。

| ID | Fixture | 動作 | 預期 | 計數要求 |
| --- | --- | --- | --- | --- |
| ANON-01 | 無 principal、無 capability | `LoadAppointmentByLineId` | 401 deny | rejected 1；全部副作用 0。 |
| SIG-01 | principal 存在、capability 格式/簽章/綁定失效 | `LoadAppointmentByLineId` | 403 deny | rejected 1；全部副作用 0。 |
| SES-01 | 已驗證 principal、無 B04B capability | `LoadAppointments` | 401 deny | rejected 1；全部副作用 0。 |
| BIND-01 | 有效 LINE capability、principal/contact/session 完全相符、scope 含 own calendar 與 canonical `ScheduleType` | `LoadAppointmentByLineId`、`Schedule`、`LoadAppointments` | allow | allowed 3 actions；只允許 1 次 fake appointment read 與 1 次 allowed manager schedule-state write；identity/ticket 寫入 0。 |
| UNBOUND-01 | verified LINE provenance 但 server binding resolver 回傳未綁定 | capability issuance／`LoadAppointmentByLineId` | 403 deny | rejected 1；全部副作用 0。 |
| CROSS-01 | subject A capability；request LINE subject B | `LoadAppointmentByLineId` | 403 deny | rejected 1；全部副作用 0。 |
| CROSS-02 | subject A own scope；foreign appointment selector | `PutAppointments` | 403 deny | rejected 1；全部副作用 0。 |
| CROSS-03 | subject A own scope；foreign group/member selector | `LoadEquipmentContact` 與 `LoadEquipmentStorLessons` | 403 deny | rejected 2；全部副作用 0。 |
| STAFF-01 | server-issued staff scope 僅含 one delegated group／appointment | delegated appointment update 與 delegated equipment read | allow | allowed 2；預期 fake appointment update 1、equipment read 1，其餘 mutation 0。 |
| ADMIN-01 | server-issued explicit admin operation grant | in-scope appointment delete | allow | allowed 1；預期 fake delete 1；不得因 request role 字串授權。 |
| ROLE-01 | authenticated principal，但 client 提供 `行政同工`／`admin` 字串，capability 無 grant 或未含所請 `ScheduleType` | `Schedule` 與 equipment write placeholder | 403 deny | rejected 1；兩個 action attempt 的全部副作用均 0，尤其 manager schedule/display/role state write = 0。 |
| MIX-01 | batch policy fixture 含 one own + one foreign selector | shared `AuthorizeBatch` preflight | 403 deny | rejected 1；整批 business read/write 0，禁止 partial success。 |
| MIX-02 | batch policy fixture 全為 own 或 delegated scope | shared `AuthorizeBatch` preflight | allow | allowed 1；預期批次操作 1，無越界 selector。 |
| PUB-01 | anonymous | stateless `SchedulerView` shell | allow | allowed 1；CRM/read/write、session/auth/capability、`ListManager` display/role/schedule state write、`AppointmentsListManager` state write、notification/job 皆 0；不得取得保護資料。 |

合計驗證：`ANON-01,SIG-01,SES-01,UNBOUND-01,CROSS-01,CROSS-02,CROSS-03(2),ROLE-01,MIX-01` 為 **9 rejects**；`BIND-01(3),STAFF-01(2),ADMIN-01,MIX-02,PUB-01` 為 **8 action allows**，但依 capability assertion 統計為 **5 allowed cases**。測試輸出必須同時報告「14 cases passed、9 rejected cases、5 allowed cases、8 allowed actions」以避免 case/action 混淆。

## Action coverage 與證據

除矩陣外，controller tests 必須逐一覆蓋 `Schedule`、`NavigateAppointmentDate`、`PostAppointments`、`DeleteAppointments`、`LoadEquipmentList`、`UpdateEquipmentStatus`、`AddEquipmentLesson`、`ExportEquipmentReport`、`GetEquipmentSummary` 的 gate-before-sink 順序。這些可合併為 **9** 個 parameterized action assertions，不改變上表的 14 contract cases；每個 unauthorized assertion 都要求全部副作用 0，且 `Schedule` 額外要求 manager schedule/display/role state write = 0。`SchedulerView` 另有 `PUB-01` 的 stateless-shell assertion，要求所有 shared-state write counter = 0。現況 no-op equipment/export/summary 命令的 allowed assertion 只能得到現有 non-sensitive placeholder response，不能因此加入 CRM 寫入、匯出或 job。

```powershell
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~AppointmentEquipmentAccessGateTests|FullyQualifiedName~AppointmentEquipmentControllerAuthorizationTests" --logger "console;verbosity=minimal"
dotnet test .\ChurchReport.MemberInfo.Tests\ChurchReport.MemberInfo.Tests.csproj --filter "FullyQualifiedName~LoginClaimsFactoryTests|FullyQualifiedName~GlobalAuthorizationFilterTests" --logger "console;verbosity=minimal"
git diff --check
git diff --name-only
```

結果擷取位置：修復工作記錄內的非敏感 test summary，以及本目錄外由 review runner 建立的 `.ccg/dual-model-runs/` 摘要。不可在本文件填入 runtime user、contact、LINE、token、CRM payload、cookie 或 production response。

## 部署證據界線

本機 14-case fake proof 僅證明 code contract，不能證明部署環境的 LINE callback URL、OAuth state/nonce、code exchange、provider token/profile、session store、cookie protection、CRM binding schema 或反向代理設定。這些都屬部署前非生產驗證證據，必須使用受控 synthetic identity，由部署／LINE／CRM owner 保存結果；未取得時標記 `RUNTIME_PREREQUISITE_BLOCKED`，不得以本機綠燈宣稱已驗證 webhook/signature/LINE 整合。
