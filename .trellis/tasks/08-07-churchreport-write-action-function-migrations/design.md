# P7.2 設計：Data8-first 具名寫入能力與受控 CE fixture

## 設計原則

P7.2 的基本單位不是 CRM entity，也不是「可傳 JSON 的 Gateway endpoint」，而是有固定商業語意、固定欄位、固定授權、固定 Profile、固定 CE support 與固定 rollback owner 的 capability。任何未列入 registry 的 operation、任何未核准的 fixture、任何 CE 8.2 write、任何 caller-supplied endpoint／organization／connector／credential 都在取得 lease 前 fail closed。

24 個 candidate 依 transaction／rollback boundary 分成八個切片，定義於 `p7.2-fixture-activation-matrix.json`。matrix 的 `fixture-pending` 不是「之後再說」，而是 dispatch 拒絕狀態。只有 `required-for-activation` row 同時滿足 fixture preflight、contract tests 與 scoped live-evidence policy 時，才可以實作或執行該切片。

## 首個垂直切片：Contact basic-info

`memberinfo.contact.update.basic.info` 取代既有 `MemberInfoController.UpdateContactInfo` 對 `contact` 的直接 SDK update，但第一個 live fixture 僅測試下列兩個字串欄位：

- `mobilephone`
- `address2_line1`

會員身分與信仰身分的 OptionSet 欄位屬同一 capability 的後續 contract branch；它們在擁有該組織 metadata 讀取、有效值驗證與 fixture baseline 前不能由 live bridge 修改。空字串仍沿用舊有「不覆寫」語意；沒有任一允許欄位時，operation 回傳具名 no-change 結果，不取得 lease、不呼叫 CE。

### 呼叫資料流

```text
ChurchReport authorized use case
  -> typed ContactBasicInfoUpdateRequest
  -> ProductClient typed client
  -> IDynamicsOperationExecutor
  -> Data8ProfileOperationExecutor
  -> allowlisted registry definition
  -> generation-owned Data8 connector lease
  -> exact contact update template + read-back projection
  -> typed ContactBasicInfoUpdateResult
  -> request scope disposes lease/client resources
```

ChurchReport 只能從已驗證的使用者／工作負載內容推導 contact authorization、profile alias 與 workload subject；這些值不得從 HTTP body 直接成為 CRM routing input。Gateway mode 與 Embedded mode 透過相同 ProductClient contract 執行，且 deployment composition root 已決定 Data8、CE version 與 immutable profile generation。

### 寫入與回應契約

- Request 是封閉 DTO：contact ID、最多兩個長度受限字串、具名 idempotency key；不接受欄位字典、CRM logical name、`Entity`、FetchXML 或 raw SDK request。
- Registry 只登錄 `memberinfo.contact.update.basic.info`；參數名稱、上限、Data8 support、CE 9.1 policy 與 response kind 必須同步驗證。
- 回應只包含 operation result、受控 changed/no-change discriminator 與安全的 correlation category；不回傳原始 contact、URL、token、cookie、例外、CRM response 或 baseline values。
- 最多一個 connector lease 存活於單次 operation。lease 由 executor 的 `await using` 擁有；Data8 service、request／response、buffer、cancellation registration 不得存入 static、cache、singleton 或 session。

## Idempotency、timeout 與 reconciliation

CE contact update 沒有可供本 contract 使用的伺服器端 idempotency token，因此它採用「不做盲目寫入重試」策略：

1. 呼叫前產生並驗證短、不可含個資的 idempotency key；key 只存於 request／短期 diagnostic scope，不成為 session state。
2. 若 transport 在 CE 回覆前失敗或 timeout，client 不自動重送 update。
3. fixture bridge 以 allowlisted read-back 比對 `mobilephone`／`address2_line1` 是否完全等於預期 sentinel：相符表示可能已提交，繼續 cleanup；完全等於 baseline 表示未提交；其他值視為 ambiguous，停止、保留 sanitized no-go evidence，且不覆寫未知資料。
4. cleanup 僅在 owner 與前述狀態可證明時，以 baseline 值復原兩欄；cleanup timeout 同樣以 read-back reconciliation，絕不以重送／刪除掩蓋不確定狀態。

這個策略使 duplicate delivery 不會造成第二筆 entity 或無法辨識的覆寫；它也明確承認「未知結果」不是成功。

## Fixture 與授權邊界

首個 fixture 由 P7.2 task-owned bridge 建立，或依使用者 2026-08-08 對 `sunnyvalechback` 全資料庫的明確研發操作授權選取任一既有 CE 9.1 contact。被選取的 contact 只在本切片執行兩個 allowlisted 欄位的 sentinel update，並在同一 bounded flow 還原 baseline；這不會把任意資料庫操作能力暴露給產品 API。fixture identity 僅儲存於目前 Windows identity 的 `%LOCALAPPDATA%\SpeechMessage\Dynamics\P7.2`，不寫入 repository、log、chat、test result 或 feature flag。bridge 先驗證：

1. Profile alias 是 deployment-owned `crm91`，ConnectorKind 是 Data8，CE version 是 9.1。
2. contact 帶有本機持有的 fixture marker，且不能與非 P7.2 record 混用。
3. 可讀取兩個 baseline 欄位、可更新兩個 allowlisted 欄位、可於當次流程讀回並復原。
4. 所有輸出僅含 go/no-go、operation alias、CE major.minor、owner category、changed/reconciled/cleaned 布林值與固定 error category。

其他七個 slices 在專屬 matrix row 取得其 graph fixture、reconciliation 及 cleanup 規則前保持 `fixture-pending`。尤其 donation、fee、attendance、list owner 與 appointment 都不得借用 contact fixture 或以 production-like data 假裝驗證。

## Slice B 重校：LINE profile、aggregate 與大型 image transport

原始 Slice B 將四個 call-site 放在同一列，repository research 證明它們實際有三種不同資源與 rollback owner，不能共用一個含糊的 fixture gate。

### B1：Contact LINE profile write

`memberinfo.contact.update.line.profile` 只接收單一 contact ID，以及三個固定欄位的封閉 mutation 指令：

- `new_line_picture_url`：`set` 或 `clear`；`set` 時只允許 bounded `https` URL。
- `new_line_status_message`：`set` 或 `clear`；`set` 時是 bounded UTF-8 純文字。
- `new_line_displayname`：`set` 或 `preserve`；沿用 legacy 行為，空白顯示名稱不能清空既有值。

LINE token、`new_lineid`、圖片探測、LINE API response 與 profile retrieval 全部留在 ChurchReport 產品流程；Gateway 只執行已授權且已正規化的 CRM 欄位 update。這可防止 LINE credential 或第三方 response 穿越 Dynamics contract。ProductClient 必須在第一次 await 前複製有限 scalar；Data8 template 只建立固定 `contact` Entity，完成 update 後只讀回三個 allowlisted 欄位。timeout 後不盲目重送，live fixture 只在可證明為 sentinel 或 baseline 時復原。

固定 request schema 為：`contactId: guid`、`pictureMode: enum(set|clear)`、`pictureUrl?: string`、`statusMode: enum(set|clear)`、`statusMessage?: string`、`displayNameMode: enum(set|preserve)`、`displayName?: string`。mode 與值必須成對：`set` 必須有 bounded value，`clear`／`preserve` 不得夾帶 value。picture URL 最多 1,024 UTF-8 bytes，status message 與 display name 各最多 512 UTF-8 bytes；三個欄位加上其餘 scalar 即使同時出現，也必須保持在 Embedded Data8 既有 4,096-byte admission envelope 內。URL 必須是 absolute HTTPS、不得含 user-info、fragment 或非預設 port。成功結果只回傳 `Changed + ReadBackConfirmed`，不包含欄位值、contact identity 或 LINE 資料。

### B2：Ungrouped commitment aggregate function

`memberinfo.contact.count.ungrouped.commitment` 是唯讀的封閉 function，不接受 FetchXML、QueryExpression、entity logical name、欄位名或 caller-selected sort。輸入只包含 bounded search、已驗證的「結案」OptionSet 值與 bounded matching-status values；小組名單與 grouped contact graph 由 connector 依固定 `statecode=0`、`purpose=小組名單`、`new_app_named=true` 規則在 request scope 內取得，不能由產品傳入無界 GUID array。

實際 public request 只接受 `search?: string`，trim 後最多 256 UTF-8 bytes。`closedStatus` 與 `matchingStatusValues` 不信任 caller；connector 以固定 `contact.customertypecode` metadata 取得「結案」值與 label search matches，metadata 缺失或歧義即 fail closed。這也避免為單一 function 擴張 canonical dispatch 的 array/object 輸入種類。

Connector 對 list、listmember 與 aggregate rows 都套用 page／row／byte 上限，所有 `IN`／`NOT IN` 值以 500 筆分塊；結果只回傳 bounded `{ value, count }` records，不回傳 Entity、AliasedValue、FetchXML 或 raw metadata。空值 segment count 仍是另一個既有產品查詢，不能被這個 operation 的非空 group-by 結果假裝涵蓋。

### B3：兩個 image writes 交由 P7.3 media owner

`memberinfo.contact.update.image` 與 `newperson.contact.update.image` 的 legacy ingress 都允許最多 5 MiB，並在產品端使用 ImageSharp 正規化。Dedicated Gateway operation body 預設 64 KiB，canonical dispatch 也只允許 bounded scalar；直接傳 `byte[]`、Base64、任意本機路徑或共享 mutable buffer 都不合法。P7.2 因此不實作假的小圖特例，也不放寬全域 operation body ceiling。

P7.3 必須建立 capability-scoped media ingress：最多 5 MiB、只接受產品正規化後的允許圖片格式、串流期間持續計數、取消即停止、內容 hash 與短生命週期 opaque handle 綁定 workload／profile／operation／contact、一次 consume、逾時或失敗確定刪除。operation request 只能引用 P7.3 mint 的 opaque handle，不能引用檔案路徑、URL、任意 blob key 或其他 tenant 的 handle。P7.3 完成前，兩個固定 operation ID 在 dispatch 前 fail closed；它們仍留在 coverage matrix，不視為 P7.2 evidence-complete。

## 相容性、啟用與回退

- CE 9.1 Data8 是初始唯一 required support。CE 8.2 及 Official Worker 都是 unsupported／not-selected，不得嘗試 fallback。
- P7.2 不打開 ChurchReport feature gate。P7.4 才能在 Dedicated Gateway listener preflight 完成後，逐 capability 啟用並觀測本機產品流量。
- 若 contract、fixture、read-back、cleanup、authorization、profile generation 或 lifecycle check 失敗，該 capability 回到 registry fail-closed 狀態；既有 ToolUtility route 只可維持至 P7.5 的正式 removal gate，不能在 P7.2 形成雙寫。
- 任何有資料、授權、錯誤語意、p95 latency、resource baseline 或 rollback regression 的切片，僅回退該 capability，保留其他已證明切片的 artifacts。

## 2026-08-11 Slice C Fresh Preflight Probe

### 邊界與資料流

`-FreshPreflightProbe` 是 provision 前的獨立、read-only 診斷 lane。PowerShell parent 只讀取
既有 deployment-owned descriptor，建立一個 nonce temporary evidence directory，透過既有
Credential Manager reference 把 password 僅傳入一個 child process。child 固定建立
`crm91 + Data8 + CE 9.1` runtime，以同一個 profile 完成 WhoAmI，並以 direct exact-ID
projections 執行 probe。它不建立 ledger、nonce、fresh entity、descriptor publication 或任何
remote mutation；parent 在 child exit=0 且 strict evidence schema 有效時，才投影唯一一行
sanitized JSON，最後無條件還原 environment、清空 password reference、dispose Process/streams，
並刪除唯一 temporary directory。

```text
FreshPreflightProbe
  -> parent descriptor/profile shape validation
  -> one crm91/Data8/CE 9.1 child
  -> WhoAmI
  -> five exact-ID list Retrieves
  -> exact-ID leader Retrieve + exact-ID owner Retrieve
  -> one TopCount=2 weekly-report RetrieveMultiple
  -> bounded evidence -> parent strict parser -> temporary cleanup
```

同一 C# `P72FreshSliceCFixturePreflightProbe` 擁有 remote proof logic；它只借用 child-owned
`IOrganizationService`，不 Dispose、快取、static 保存或將 service/Entity/exception 交給背景
工作。request scalar 與 result classifications 都只在 invocation scope 存活。WhoAmI identity 是
deployment-owned executor 的輸出，絕不允許從 descriptor 或 parent environment 指定。

### 固定 evidence contract

child 只可寫入 version 1 的 strict JSON。parent 拒絕遺漏、額外、格式錯誤或不在 allowlist 的
欄位，並絕不轉發原始 child stdout/stderr、CRM response 或 exception。固定 top-level 欄位為
`schemaVersion`、`outcome`、`reason`、`profileAlias`、`deploymentProfileAlias`、`ceVersion`、
`connector`、`preflightOnly`、`operationExecuted`、`readOnlyProbeExecuted`、
`featureFlagChanged` 與 `probe`。`operationExecuted` 與 `featureFlagChanged` 永遠為 false；
`preflightOnly` 永遠為 true。

`probe` 固定為 `requestShape`、`operationalLists`、`leaderMarker`、`ownerKind`、`ownerState`、
`ownerRelation`、`weeklyReport`。每一欄只使用固定、去識別化值：

- `requestShape`: `valid` 或 `invalid`；
- `operationalLists`: `valid`、`invalid` 或 `unavailable`；
- `leaderMarker`: `valid`、`invalid` 或 `unavailable`；
- `ownerKind`: `systemuser`、`other-or-missing` 或 `unavailable`；
- `ownerState`: `active`、`inactive-or-missing` 或 `unavailable`；
- `ownerRelation`: `different-from-data8`、`same-as-data8` 或 `unavailable`；
- `weeklyReport`: `exactly-one-active`、`not-exactly-one-active` 或 `unavailable`。

所有七項為 green 時，child 回傳 `outcome=go`、`reason=fresh-preconditions-proven`、
`readOnlyProbeExecuted=true`。已完成 read-only calls 但任一 proof 為 false 時回傳
`outcome=no-go`、`reason=fresh-preconditions-not-proven`、`readOnlyProbeExecuted=true`；shape、
WhoAmI、runtime、transport 或 cleanup 不可證明時回傳 `outcome=no-go` 與固定 bounded reason，
不把 partial result 當作寫入授權。

### 安全、不確定性與回退

此 lane 不可重用 `Provision`，因為 provision 的 pending ledger、nonce 和寫入 allowlist 會擴張
診斷權限；亦不可重用 `RepairProbe`，因為後者驗證 stale relationship graph，而非五個 operational
lists、owner 和 weekly report 的 fresh provisioning prerequisites。任一 WhoAmI/remote read 或
resource cleanup exception 均 fail closed；probe 結果只能支援「是否外部狀態可能已改變」的判斷，
不能授權重試前一個 no-go、不能建立/清理 fixture，也不能開始 D--H。

## 測試策略

1. 先寫 contract tests：未知 operation、錯誤 profile／connector／CE、未授權 contact、空 update、超長字串、未知欄位、錯配 response、取消、timeout 與 duplicate idempotency key 都必須 fail closed。
2. 再寫 Data8 executor tests：驗證 request 在 await 前複製有限 scalar、僅允許新 registry operation、每條成功／失敗／取消路徑歸還 lease，且不保留 request／credential／profile references。
3. 寫 ProductClient／Gateway／Embedded parity tests：兩條 Lenovo route 使用同一 typed request/result，沒有 request-time connector/profile/CE switch。
4. 寫 fixture bridge tests：baseline、committed、not-committed、ambiguous、cleanup-committed、cleanup-ambiguous 六種狀態皆有決定性 outcome，且 JSON 無 secret、GUID、endpoint、raw exception 或 PII。
5. 真實 CE evidence 僅在 bridge 和本機測試全綠後執行一次 bounded flow；接著做本機 stress／soak／drain checks，證明 lease、permit、client、buffer 與 task return to baseline。
