# B04C Wave 2 修復合約

CONTRACT_STATUS: WAVE_PLAN_APPROVED
Wave: Wave 2 / B04C-scheduling-qr
Selected issues: B04C-SEC-001, B04C-SEC-002

Review evidence: Claude review produced no usable output. Exactly one controller-dispatched,
read-only Codex fallback review approved this contract with no Critical or Warning findings.
This approval does not satisfy any deployment gate or BLOCKED terminal condition below.

## 不可變範圍

本 wave 僅處理 B04C-SEC-001 與 B04C-SEC-002。明確排除 B04C-PERF-001、
B04C-EXT-001、全部 B04A、B04B、B01、X05Q 工作，以及 CRM 批次化、資料模型
重設計、QR utility/PollManager 抽取、公開 UI 重設計。

本合約不宣稱現有 capability、簽章金鑰、replay store、LINE server identity、
scheduler policy 或 route composition 已存在。缺少任一部署前提時，修復為
BLOCKED；不得以 client 欄位、固定測試 principal、in-memory nonce store 或
development key 代替生產 authority。

## 現有 endpoint/action inventory

以下 inventory 只根據目前 QrCodeController、SchedulerDataController 與
InMemoryAppointmentsDataContext。五個 QR POST 與 SavePoll 只有 HttpPost 而沒有
action-level Route；scheduler 四個 action 也沒有有效的 class/action route。實際
部署 URL、filter 與 DI composition 屬 X01 前提，不能由本合約推定。

| Action | 現有來源行為 | Issue | 必要 gate | rollback treatment |
|---|---|---|---|---|
| QrCodeView | /QrCodeView、/Home/QrCodeView、/Home/QrCodeView/{QrCodeViewPatameter}、/QrCode/CourseView/{QrCodeViewPatameter}；將 query QrCodeId 寫入 mutable QR context 後回傳 course view | SEC-001 | QrCodeId 是 untrusted locator；只可經 server resolver 產生 action-bound opaque capability | 保持 landing route/view；issuer 回滾時後續 POST fail closed，raw QrCodeId 不可恢復 authority |
| PollQrCodeView | /PollQrCodeView、/Home/PollQrCodeView、/Home/PollQrCodeView/{PollQrCodeViewPatameter}、/QrCode/PollView/{PollQrCodeViewPatameter}；寫入 context 並呼叫 PollManager.SetDisplayFlag | SEC-001 | locator 解析為 poll target/action/scope | 保持 landing route/view；SavePoll fail closed |
| SmallGroupQrCodeView | /SmallGroupQrCodeView、/Home/SmallGroupQrCodeView、/Home/SmallGroupQrCodeView/{QrCodeViewPatameter}、/QrCode/SmallGroupView/{QrCodeViewPatameter}；寫入 context 後回傳 small-group view | SEC-001 | locator 解析為 small-group target/action/scope | 保持 landing route/view；POST fail closed |
| SundayQrCodeView | /SundayQrCodeView、/Home/SundayQrCodeView、/Home/SundayQrCodeView/{QrCodeViewPatameter}、/QrCode/SundayView/{QrCodeViewPatameter}；寫入 context 後回傳 Sunday view | SEC-001 | locator 解析為 Sunday target/action/scope | 保持 landing route/view；POST fail closed |
| PersonalQrCodeView | /PersonalQrCodeView、/Home/PersonalQrCodeView、/Home/PersonalQrCodeView/{QrCodeViewPatameter}、/QrCode/PersonalView/{QrCodeViewPatameter}；寫入 context 後回傳 personal view | SEC-001 | locator 解析為 personal target/action/scope | 保持 landing route/view；POST fail closed |
| QrCodeGetLineId POST | 先以 browser UserLineId/GroupId/RoomId/ViewType 呼叫 SetupLineContext，再以 mutable QR id 呼叫 QrCodeUtility.SetupQrCodeIdString | SEC-001 | capability、server subject、course action、target/scope、expiry、atomic nonce consume 全部先通過 | 移除 gate 時 fail closed；不得回到 client identity + mutable context |
| PollQrCodeGetLineId POST | 先 SetupLineContext，再以 browser user id 取 fullname，並以 mutable QR id 呼叫 PollManager.GetClassName | SEC-001 | poll-line-id capability 與 server subject binding 先通過 | fail closed；不得呼叫 GetUserFullName/GetClassName 繞過 gate |
| SmallGroupQrCodeGetLineId POST | 先 SetupLineContext，再以 mutable QR id 呼叫 SmallGroupQrCodeUtility.SetupQrCodeIdString | SEC-001 | small-group-line-id capability 與 server subject binding 先通過 | fail closed |
| SundayQrCodeGetLineId POST | 先 SetupLineContext，再以 mutable QR id 呼叫 SundayQrCodeUtility.SetupQrCodeIdString | SEC-001 | sunday-line-id capability 與 server subject binding 先通過 | fail closed |
| PersonalQrCodeGetLineId POST | 先 SetupLineContext，再以 mutable QR id 呼叫 PersonalQrCodeUtility.SetupQrCodeIdString | SEC-001 | personal-line-id capability 與 server subject binding 先通過 | fail closed |
| SavePoll POST | 以 mutable QR id 與 mutable LineUserId 呼叫 PollManager.SavePoll | SEC-001 | 獨立 poll-save capability、server subject binding、nonce consume 先通過 | fail closed；不得使用先前 POST 寫入的 context 直接儲存 |
| SchedulerDataController.Get | DataSourceLoader.Load(_data.Appointments, loadOptions)；無 Add/Remove/SaveChanges | SEC-002 | 不屬 mutation gate；read policy 為 B01/X01 deployment prerequisite | mutation-gate rollback 不影響 Get |
| SchedulerDataController.Post | PopulateObject(values, new Appointment)、Appointments.Add、Ok；SaveChanges 目前被註解 | SEC-002 | principal、B01 mutation decision、CSRF、server scope/owner、DTO validation、idempotency 先於 Add | gate 不可用時 fail closed；不得回到裸露 Add |
| SchedulerDataController.Put | First(key)、PopulateObject、SaveChanges(key)、Ok | SEC-002 | principal、CSRF、safe scoped lookup、owner/scope、allowlisted fields、idempotency/concurrency 先於 mutation/SaveChanges | gate 不可用時 fail closed；不得回到 First 路徑 |
| SchedulerDataController.Delete | First(key)、Remove、SaveChanges(key)，void action | SEC-002 | principal、CSRF、safe scoped lookup、owner/scope、idempotency/concurrency 先於 Remove/SaveChanges | gate 不可用時 fail closed；不得回到 First 路徑 |

## 最小未來修復 allowlist

產品檔案：

- SpeechMessageProducts.ChurchReport/Controllers/QrCodeController.cs
- SpeechMessageProducts.ChurchReport/Controllers/ApiControllers/SchedulerDataController.cs
- SpeechMessageProducts.ChurchReport/Security/B04CQrScanCapabilityVerifier.cs
- SpeechMessageProducts.ChurchReport/Security/B04CSchedulerMutationGate.cs
- SpeechMessageProducts.ChurchReport/Views/QrCode/QrCodeView.cshtml
- SpeechMessageProducts.ChurchReport/Views/QrCode/PollQrCodeView.cshtml
- SpeechMessageProducts.ChurchReport/Views/QrCode/SmallGroupQrCodeView.cshtml
- SpeechMessageProducts.ChurchReport/Views/QrCode/SundayQrCodeView.cshtml
- SpeechMessageProducts.ChurchReport/Views/QrCode/PersonalQrCodeView.cshtml

測試檔案：

- ChurchReport.MemberInfo.Tests/Security/B04CQrScanCapabilityVerifierTests.cs
- ChurchReport.MemberInfo.Tests/Security/B04CSchedulerMutationGateTests.cs
- ChurchReport.MemberInfo.Tests/Security/B04CQrCodeControllerSecurityTests.cs
- ChurchReport.MemberInfo.Tests/Security/B04CSchedulerDataControllerSecurityTests.cs

InMemoryAppointmentsDataContext.cs 是 SEC-002 的證據來源，不在修復 allowlist。
目前可用真實 session/cache fixture 的 collection snapshots 與既有 SaveChanges 結果
觀察行為。若未來證明無法測量，必須取得新的範圍核准，不得自行擴寫該檔。

禁止修改 PollManager、QR utilities、InMemoryContext、Appointment model、Startup/Program、
全域設定、B01/B04A/B04B/X05Q、其他 diagnostics、.trellis、.ccg/tasks 或 Git state。

## B04C-SEC-001 capability 合約與部署阻塞

1. Landing QrCodeId 是 untrusted locator，不是 capability，也不是 subject 或 target
   authority。server resolver 必須解析為已知 target、發行 action 與 scope；POST
   只接受 opaque capability 的驗證結果。
2. capability 的介面 payload 至少包含 immutable target identifier、發行 action、
   scope/audience、expiry、nonce/jti、版本與完整性保護。POST 使用 B01
   server-verified LINE subject 綁定 capability。DisplayName、UserLineId、GroupId、
   RoomId、ViewType 與任何 browser identifier 僅可作一致性檢查或顯示資料。
3. 固定順序為：capability 格式/完整性、expiry、action/scope/target、B01 subject、
   binding、atomic nonce consume、既有 SetupLineContext/utility 或 PollManager.SavePoll。
   拒絕路徑不得進入後段來源呼叫。
4. nonce consume 成功者才可進入一次 command；同 capability concurrent 請求只能一個
   成功。不同 action 不可共享 nonce authority，poll display 與 SavePoll 須使用各自
   action-bound command capability。

| Owner | 必要產物 | 缺少時的處置 |
|---|---|---|
| B01 | server-side LINE identity、stable subject、subject-to-capability binding contract | BLOCKED；B04C 不得信任 LIFF/browser UserLineId 或 fake principal |
| X01 | conventional QR POST endpoint mapping、DI/filter composition、B01 identity wiring | BLOCKED；B04C 不得猜測 route/filter 或繞過 composition |
| Security/Platform deployment owner（目前 B04C 來源未指名） | production signing-key source、rotation/verification policy、durable shared atomic nonce/replay store | BLOCKED；in-memory key/store 僅可 local test，不能證明跨 process/instance replay 防護 |
| B04C | 將已提供的 contract 接入 controller/view 並維持 response shape | 僅能完成 local interface proof，不能自行宣稱 deployment ready |

## B04C-SEC-002 in-memory scheduler 合約

這不是 CRM/job/notification pipeline。InMemoryAppointmentsDataContext.Appointments 以 session
id + memory cache 取得 ICollection<Appointment>；Post 只 Add，Put/Delete 各呼叫既有
SaveChanges(key)。該 SaveChanges 對仍存在且 key 相同的 appointment 重新指定
AppointmentId；Delete 後通常沒有 matching item。本 wave 只保護這些直接 mutation
boundary，不能宣稱存在其他 side effect。

- Get：read scope 由已存在或 X01 組成的 B01 read policy 決定。本 wave 不把 Get 納入
  mutation gate，也不改變其 query/result behavior。
- Post：server policy 決定 principal 可建立的 scheduler scope 與 owner；payload OwnerId
  不可自行擴權。成功的 current-compatible boundary 是 Add=1、SaveChanges=0。
- Put：key 先以 server scope/owner 作 safe lookup；不存在或不屬於 scope 時不進
  PopulateObject。只允許已核准 mutable fields；成功為 Replace=1、SaveChanges=1。
- Delete：同樣先 safe scoped lookup；成功為 Remove=1、SaveChanges=1。
- 相同 command id 回傳原本成功結果且不再 mutation；同 target 的不同 concurrent command
  由 target-scoped gate 序列化，第一個合法 command commit，第二個為 409 Conflict 且
  counters 全為零。

| Owner | 必要產物 | 缺少時的處置 |
|---|---|---|
| B01 | authenticated principal、scheduler mutation decision、server-owned scope/owner decision | BLOCKED；不能由 OwnerId、key 或測試 principal 取代 |
| B04B | appointment/equipment ownership vocabulary 與可供 B01 decision 使用的 server mapping；本 wave 不修改 B04B | BLOCKED；沒有 owner/scope mapping 時不可接受 Put/Delete |
| X01 | conventional scheduler route mapping、B01 policy 與 anti-forgery/API-CSRF filter composition | BLOCKED；不能只在 controller test 假設 filter 已執行 |
| Security/Platform deployment owner（目前 B04C 來源未指名） | shared idempotency/target-concurrency state 的 durability 與 multi-instance behavior | local test 可用 deterministic fake；部署無 shared state 時不可宣稱 replay/parallel protection |

## 實施順序、命令與回滾

1. 先確認 B01、B04B、X01 與 Security/Platform prerequisite 的具體 provider。任一未
   提供即記錄 BLOCKED，不寫 fake authority。
2. 五個 QR views 傳遞 opaque capability，不輸出 raw QR target 作 POST authority；
   controller 在 SetupLineContext、utility 或 PollManager.SavePoll 前驗證。
3. 對 Post/Put/Delete 建立窄的 scheduler mutation gate，保留 in-memory collection
   與既有 SaveChanges 語意；Get 不進 mutation gate。
4. 執行 dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj --no-restore
   與 dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore。
   證據是 measurements.md 的 per-action counters、fake dependency call order、authorized
   no-regression 與 zero real external-service calls。

QR rollback：五個 landing actions 繼續回傳相同 view；五個 *QrCodeGetLineId 與 SavePoll
在 authority 不可用時 fail closed，絕不回到 client identity/mutable QR context。
Scheduler rollback：Get 完全不受 mutation-gate rollback 影響；Post/Put/Delete 不可回到
裸露 Add/First/Remove，新 gate 不可用時 fail closed。任何 fail-closed rollback 都表示
本 wave 未成功，不是 security approval。
