# B04C Wave 2 測量合約

CONTRACT_STATUS: WAVE_PLAN_APPROVED
Issues: B04C-SEC-001, B04C-SEC-002

Review evidence: Claude review produced no usable output. Exactly one controller-dispatched,
read-only Codex fallback review approved this contract with no Critical or Warning findings.
This approval does not satisfy any deployment gate or BLOCKED terminal condition below.

所有 local 測量使用固定 clock、non-secret opaque fixtures、in-memory fake signer、
fake replay/idempotency store、fake B01 principal/policy、fake X01 CSRF result，並且
不連接 LINE、CRM 或其他外部服務。這些 fake 只證明介面呼叫順序與 controller
分支；不證明 production signing key、durable store、server identity、route/filter
composition 或 multi-instance atomicity。

## B04C-SEC-001：QR endpoint matrix

測試 endpoint 是 QrCodeGetLineId、PollQrCodeGetLineId、SmallGroupQrCodeGetLineId、
SundayQrCodeGetLineId、PersonalQrCodeGetLineId 與 SavePoll。每個 endpoint 都各自
執行下列 matrix，因此固定結果為 12 requests、10 rejects、2 allows。capability
fixture 必須含對應 action；不得以一個 endpoint 的 token 通過另一 endpoint。

| Case | Requests | Expected status/outcome | Reject | Allow | 實際來源 boundary counters |
|---|---:|---|---:|---:|---|
| malformed/empty capability | 1 | 400，verifier 拒絕 | 1 | 0 | SetupLineContext=0；utility/PollManager target call=0 |
| tampered capability 或 target identifier | 1 | 400 或 403，完整性/target 拒絕 | 1 | 0 | 同上 |
| expired capability | 1 | 401 或 403，expiry 拒絕 | 1 | 0 | 同上 |
| replayed nonce/jti | 1 | 409 或 stable replay rejection | 1 | 0 | 同上 |
| wrong server subject | 1 | 403 | 1 | 0 | 同上 |
| anonymous/unverified subject | 1 | 401 | 1 | 0 | 同上 |
| unbound subject/capability | 1 | 403 | 1 | 0 | 同上 |
| cross-scope target | 1 | 403 | 1 | 0 | 同上 |
| cross-schedule/action capability | 1 | 403 | 1 | 0 | 同上 |
| authorized first use | 1 | 現有 success response shape | 0 | 1 | 五個 GetLineId：SetupLineContext=1 且其 named source call=1；SavePoll：PollManager.SavePoll=1 |
| same capability in parallel | 2 | 一個現有 success response；一個 replay rejection | 1 | 1 | 只有一個 source-specific allowed counter 遞增 |

五個 GetLineId 的 named source call 依序是 QrCodeUtility.SetupQrCodeIdString、
PollManager.GetClassName（另有 GetUserFullName）、SmallGroupQrCodeUtility.SetupQrCodeIdString、
SundayQrCodeUtility.SetupQrCodeIdString、PersonalQrCodeUtility.SetupQrCodeIdString。
SavePoll 的 source call 是 PollManager.SavePoll。任何 reject 不允許進 SetupLineContext、
上述 source call 或 PollManager.SavePoll。這是可直接觀察的 ordering；本合約不虛構
其他 manager/job/notification/downstream counters。

五個 landing actions（QrCodeView、PollQrCodeView、SmallGroupQrCodeView、
SundayQrCodeView、PersonalQrCodeView）各執行一個 fixture：untrusted locator 只能得到
相同 view 與 non-secret opaque capability reference，不能使 raw QrCodeId 成為 POST
authority。每個 landing case 的 POST utility/PollManager.SavePoll counter 都是 0。
若 issuer 或 B01 identity prerequisite 缺失，結果為 BLOCKED，不得用 local fake 宣稱
deployment 通過。

## B04C-SEC-002：scheduler per-action matrix

Counter 格式為 [Add, Replace, Remove, SaveChanges]。Replace 指接受的 Put 對既有
appointment 的一次資料替換，不主張 ICollection 有 Replace API。測試使用真實
InMemoryAppointmentsDataContext session/cache fixture 觀察 collection 前後狀態；
Put 另以既有 SaveChanges(key) 的 source-specific ID 結果觀察 SaveChanges。若 repair
新增窄 gate，gate spy 只量測 authorization/CSRF/idempotency 決定在 collection
mutation 前，並非替代真實 context proof。

### Get

| Case | Expected status/outcome | [Add, Replace, Remove, SaveChanges] |
|---|---|---|
| composed read policy allows existing scope | 200，DataSourceLoader.Load 的既有結果 | [0, 0, 0, 0] |
| mutation gate rejects separate Post/Put/Delete request | Get 的既有讀取結果不變 | [0, 0, 0, 0] |
| B01/X01 read composition unavailable | BLOCKED deployment proof；不以 fake principal 宣稱 public route 已受保護 | [0, 0, 0, 0] |

### Post

| Case | Expected status/outcome | [Add, Replace, Remove, SaveChanges] |
|---|---|---|
| anonymous | 401 | [0, 0, 0, 0] |
| missing/invalid CSRF proof | 400 | [0, 0, 0, 0] |
| malformed values 或 invalid DTO | 400 | [0, 0, 0, 0] |
| unbound principal、wrong owner 或 cross-schedule scope | 403 | [0, 0, 0, 0] |
| first authorized command | 200，保留目前 Ok outcome | [1, 0, 0, 0] |
| same idempotency key replay | 200，回傳原成功 outcome；無第二次 Add | [0, 0, 0, 0] |
| different authorized command，同 target，parallel | 一個 200，一個 409 Conflict | winner [1, 0, 0, 0]；loser [0, 0, 0, 0] |

### Put

| Case | Expected status/outcome | [Add, Replace, Remove, SaveChanges] |
|---|---|---|
| anonymous | 401 | [0, 0, 0, 0] |
| missing/invalid CSRF proof | 400 | [0, 0, 0, 0] |
| malformed values 或 invalid DTO | 400 | [0, 0, 0, 0] |
| missing key、wrong owner 或 cross-schedule scope | safe non-enumerating 404 | [0, 0, 0, 0] |
| unbound principal/scope | 403 | [0, 0, 0, 0] |
| first authorized command | 200，保留目前 Ok outcome | [0, 1, 0, 1] |
| same idempotency key replay | 200，回傳原成功 outcome | [0, 0, 0, 0] |
| different authorized command，同 key/target，parallel | 一個 200，一個 409 Conflict | winner [0, 1, 0, 1]；loser [0, 0, 0, 0] |

### Delete

| Case | Expected status/outcome | [Add, Replace, Remove, SaveChanges] |
|---|---|---|
| anonymous | 401 | [0, 0, 0, 0] |
| missing/invalid CSRF proof | 400 | [0, 0, 0, 0] |
| missing key、wrong owner 或 cross-schedule scope | safe non-enumerating 404 | [0, 0, 0, 0] |
| unbound principal/scope | 403 | [0, 0, 0, 0] |
| first authorized command | 204，符合目前 void action 的 no-content outcome | [0, 0, 1, 1] |
| same idempotency key replay | 204，回傳原成功 outcome | [0, 0, 0, 0] |
| different authorized command，同 key/target，parallel | 一個 204，一個 409 Conflict | winner [0, 0, 1, 1]；loser [0, 0, 0, 0] |

Local command evidence：

dotnet build SpeechMessageProducts.ChurchReport/SpeechMessageProducts.ChurchReport.csproj --no-restore

dotnet test ChurchReport.MemberInfo.Tests/ChurchReport.MemberInfo.Tests.csproj --no-restore

結果必須捕捉上述 table rows、source call order、collection snapshots、zero real
external-service calls 與 authorized no-regression。Deployment evidence 仍是 B01、X01、
Security/Platform 與 B04B 的個別責任。
