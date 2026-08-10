# P7.2 Slice C operator handoff

## 目的與安全邊界

此 handoff 對應五個 P7.2 Slice C 寫入流程：

- `list.members.add.many`
- `list.members.remove.one`
- `listmanagement.smallgroup.update.fields`
- `contact.assign.owner`
- `newperson.contact.transfer.between.lists`

runner 僅允許 deployment-owned `crm91`、`sunnyvalechback`、CE 9.1 與 Data8。CE 8.2、
Official Worker、caller supplied endpoint、credential、connector、CE version、CRM SDK request
與任意欄位 map 都不會被接受。ChurchReport 現有 Package01／Package02 feature flags 必須保持
`false`；runner 不會變更任何設定。

預設模式只做本機預檢，不會讀取 password、啟動 `dotnet test`、寫入 CE 或改變 browser session。
`-ReconcileFixture` 是獨立的唯讀模式：它會使用既有 Credential Manager target 啟動一次有 180 秒
上限的 child test，但只執行 WhoAmI 與固定的 CRM 讀取 projection。它不會發出 Update、Delete、Assign、
list membership mutation、feature flag 變更或 retry。`-ExecuteFixture` 仍是另一條寫入驗證 lane；兩個
switch 不能同時使用，且目前不得以 reconciliation 結果作為執行寫入 lane 的授權。

## 先執行預檢

```powershell
$root = 'D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Invoke-Package02Data8ListManagementEvidence.ps1" `
  -RepositoryPath $root `
  -Json
```

成功的預檢只會輸出一行去識別化 JSON，包含 `outcome=go`、`preflightOnly=true`、
`operationExecuted=false`、`featureFlagChanged=false`。它確認 matrix、Embedded + Data8 設定、
既有 `crm91` Credential Manager reference、Slice A contact descriptor，以及 Slice C graph
descriptor 的 owner／版本／connector／schema 均符合要求。

## 目前可安全執行：唯讀 reconciliation

這是目前唯一建議執行的實機命令。它不使用 Edge／Chrome 的 cookie 或登入 session；瀏覽器能登入只是
人工確認帳密與 CE 首頁可用的輔助證據。實際連線由目前 Windows 使用者的既有 Credential Manager
Generic Credential 提供，password 只在 parent 與一個受限 child process 的短生命週期環境中存在。

```powershell
$root = 'D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Invoke-Package02Data8ListManagementEvidence.ps1" `
  -RepositoryPath $root `
  -ReconcileFixture `
  -Json
```

正常完成時會輸出一行去識別化 JSON，核心欄位為：

```json
{
  "outcome": "no-go",
  "reason": "baseline-unprovable",
  "preflightOnly": false,
  "readOnlyProbeExecuted": true,
  "operationExecuted": false,
  "featureFlagChanged": false,
  "safeToRetry": false,
  "ownerBinding": "matches-service-identity",
  "probeStage": "classification-complete",
  "states": { }
}
```

### `probeStage` 的去識別化診斷意義

`probeStage` 只會輸出下列固定分類，不會輸出 GUID、帳號、密碼、端點、例外內容或 CRM 欄位值。它不是
重試授權，任何結果仍維持 `safeToRetry=false`：

- `not-started`：尚未完成可驗證的唯讀階段。
- `whoami-verified`：Data8 的 WhoAmI 已驗證 service identity；下一個失敗點在 fixture store 建立前後。
- `fixture-store-created`、`add-membership-read`、`remove-membership-read`、`small-group-read`：前一個固定
  唯讀階段已完成；下一個階段未能完成。
- `small-group-expected-read`、`contact-owner-read`、`transfer-read`：同樣表示前一個固定讀取已完成。
- `classification-complete`：所有既定唯讀讀取與去識別化分類已完成；結果仍是歷史 baseline 不可證明的
  `no-go`，不是寫入或重試許可。

這個 `no-go` 是預期的保護結果：先前 Slice C 寫入前的歷史 baseline 沒有被保存，現在看到的 CE
表面狀態不足以證明可安全重跑。`safeToRetry=false` 是 PowerShell parent 自己加入，child 不能自行
宣告；輸出不會含 password、token、cookie、endpoint、Organization ID、GUID、baseline、原始例外或
temporary path。請勿把這個結果當成 `-ExecuteFixture` 的同意書。

## fixture descriptor

### 持久化與祕密界線

對 Slice C 而言，只有 `%LOCALAPPDATA%\SpeechMessage\Dynamics\P7.2\list-management-fixture.json`
會持久保存 Slice C fixture IDs。它只能保存下列 task-owned GUID、版本、connector、marker 與目前
Windows owner identity；絕不可保存 baseline snapshot、password、token、cookie、endpoint、原始 exception
或任何其他祕密。Slice A 的 contact ID 仍以它自己的 contact-basic-info descriptor 為唯一來源；runner
會自行讀取它，CLI 不要求 operator 貼上任何 ID 或 password。

runner 自動重用既有 Slice A descriptor：

```text
%LOCALAPPDATA%\SpeechMessage\Dynamics\P7.2\contact-basic-info-fixture.json
```

因此 operator 不需在命令列貼 contact GUID 或 password。Slice C 仍需要由受控 provisioning
流程建立下列 task-owned descriptor：

```text
%LOCALAPPDATA%\SpeechMessage\Dynamics\P7.2\list-management-fixture.json
```

其值僅能由受控 provisioning 寫入，不能以 chat、TRX、console 或手動命令列參數傳遞。descriptor
必須是 UTF-8 no-BOM、CRLF-only、32 KiB 以下的 JSON，並具備固定 schema：

```json
{
  "schemaVersion": 1,
  "fixtureId": "p7.2-list-management",
  "profileAlias": "sunnyvalechback",
  "ceVersion": "9.1",
  "connector": "Data8",
  "marker": "p7.2-list-management",
  "ownerIdentity": "<current Windows identity>",
  "addListId": "<task-owned static-list GUID>",
  "removeListId": "<task-owned static-list GUID>",
  "smallGroupListId": "<task-owned static-list GUID>",
  "smallGroupTargetLeaderContactId": "<task-owned contact GUID>",
  "smallGroupExpectedRelationshipListId": "<task-owned relationship-list GUID>",
  "transferSourceListId": "<task-owned static-list GUID>",
  "transferTargetListId": "<task-owned static-list GUID>",
  "transferWeekStartUtc": "<UTC Sunday 00:00 round-trip timestamp>"
}
```

`targetOwnerId` 不再是 descriptor 欄位。child 會先透過同一個 `crm91` Data8 runtime 執行封閉的
WhoAmI，只有 CE 9.1、operation discriminator、organization 與三個 identity GUID 都符合時，才把
目前 service user 當作 owner assignment 與 transfer 的共同目標；該 GUID 不會寫入 evidence 或 console。

`smallGroupExpectedRelationshipListId` 是必填、非空的 canonical GUID。它指定 store 用來證明
small-group expected projection 的 task-owned relationship list，且依 bridge/store 合約必須與
`smallGroupListId` 不同；runner 不會猜測、搜尋或 provision 任何 relationship graph。

五個 list identity 必須互異。runner 和 child test 都會重複驗證 owner、marker、CE 9.1、Data8、
GUID 格式、UTC Sunday、來源／目標 list 不同，以及 Slice A contact descriptor。它會在任何 dispatch
前，以既有 `P72Data8ListManagementFixtureStore` 讀取完整 graph：add baseline 缺席、remove baseline
存在、small-group expected 與 baseline 不同、owner target 與 baseline 不同、transfer source/target/
weekly-report/present-record/primary-list/owner 都可證明。無法證明時輸出 no-go，完全不寫入 CE。

目前 store/bridge 公開的安全契約只支援固定 graph 的讀取、mutation 與 rollback，沒有可安全重用的
generic graph discovery/provision API。為了避免猜測 CE schema 或建立非 task-owned 資料，runner 不會
自行建立 list、weekly report、present record 或 systemuser 關係；descriptor 缺失時的安全結果是：

```json
{"outcome":"no-go","reason":"fixture-input-required","preflightOnly":true,"operationExecuted":false,"featureFlagChanged":false}
```

這不是可略過的警告。必須先以受核准的 provisioning 建立並驗證完整 graph，再考慮 execution。

## 寫入驗證 lane（目前不要執行）

只有在使用者另行明確核准「建立新的受控 baseline 與執行 Slice C 寫入驗證」後，才可考慮這條 lane。
operator 身份、CE 9.1 Credential Manager、完整 graph 與新的可還原 baseline 都必須可證明；browser
session 不是 credential 傳遞機制，也不能取代這些前提。

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Invoke-Package02Data8ListManagementEvidence.ps1" `
  -RepositoryPath $root `
  -ExecuteFixture `
  -Json
```

runner 從既有 Credential Manager `crm91` target 取得 password，僅在當前 parent/child process
的短生命週期環境變數使用，finally 會回復先前值並釋放 native credential pointer。它只啟動一個
child process 和一個 xUnit test。child test 先完成所有 graph 讀取，然後依序執行：

1. add members：baseline read、一次 add dispatch、read-back、restore、restore read-back。
2. remove member：baseline read、一次 remove dispatch、read-back、restore、restore read-back。
3. small-group fields：完整六欄 baseline、一次 fixed-mode dispatch、read-back、restore、restore read-back。
4. owner assignment：owner baseline、一次 Assign dispatch、read-back、restore、restore read-back。
5. transfer composite：完整 source/target/present-record/lookup/owner baseline、一次 dispatch、read-back、restore、restore read-back。

任一段有 timeout、transport ambiguity、read-back 不一致或 cleanup 無法證明時，後段不再啟動，
evidence operation 會以 `not-run` 說明；絕不重試或猜測補償。已開始的 bridge 只會在 baseline 或完整 expected
可證明時還原；未知／部分 graph 必須維持 no-go 並交由人工 reconciliation。

## 輸出與失敗處理

child 在清理 runtime、fixture store 與 logger 後，將唯一一份去識別化 evidence 寫入 parent 建立的
OS temporary nonce 目錄。檔案名稱固定、父目錄不得為 reparse point、不得預先存在，並以 create-new
語意寫入 UTF-8 no-BOM、CRLF-only、32 KiB 以下的 JSON。PowerShell 只在 child 結束後讀取這份檔案，
以 exact schema 重新投影固定五個 operation 的順序、outcome、一次 dispatch、reconciliation、cleanup、
CE、connector 與 feature flag；遺失、格式不符或額外欄位一律 no-go。最後 console 仍只輸出一行 JSON，
不輸出暫存路徑或檔案內容。

不應輸出或複製：password、credential target、cookie、token、endpoint、GUID、owner identity、
baseline、sentinel、CRM response、TRX、raw exception 或 browser session 資訊。

常見安全結果：

- `fixture-input-required`：尚未有受控 Slice C graph descriptor；無 CRM mutation。
- `fixture-input-invalid`：descriptor owner、schema、Data8／CE 9.1 或 graph scalar 不合；無 CRM mutation。
- `credential-unavailable`：既有 Credential Manager target 無法安全讀取；無 CRM mutation。
- `fixture-precondition-failed`：child 的唯讀 graph proof 不成立；無 CRM mutation。
- `test-timeout`：child 可能已到達 dispatch；不可重試，先人工 reconciliation。
- `live-evidence-incomplete`：某段 bridge 已 no-go；勿重試，依 sanitizer 的 operation state 判斷後續處置。
- `temporary-cleanup-failed`：parent 無法證明 task-owned temporary evidence directory 已刪除；即使 child
  原本成功也維持 no-go，不可重跑寫入，必須先完成唯讀 reconciliation。

## 本次狀態

本 handoff 對應的本機防呆測試與 child lane 都不會自行進入寫入 mode。下一個實機動作只能是上述
`-ReconcileFixture`；其結果無論為正常的 `baseline-unprovable` 或任何其他 no-go，都不會改動 CE、
產品程式、設定、feature flag 或 ChurchReport 流量。
## Slice C relationship-list repair precondition probe

Before any repair or execute lane, run this read-only probe from the Lenovo
operator PowerShell:

```powershell
$root = 'D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Invoke-Package02Data8ListManagementEvidence.ps1" `
  -RepositoryPath $root `
  -RepairProbe `
  -Json
```

The probe is diagnostic only. A completed probe still reports `outcome=no-go`,
keeps `operationExecuted=false`, and sets `safeToRetry=false`. Do not paste
passwords, tokens, cookies, GUIDs, endpoints, or raw exceptions; return only
the final sanitized JSON line.

The 2026-08-10 probe completed read-only but reported
`preconditionState=provenance-invalid`, with
`sourceContactMarkerValid=false` and
`expectedRelationshipRaceLeaderMatches=false`. The relationship fields were
blank. This is not permission to run `-RepairFixture`; the two provenance
conditions must be corrected first, then the probe must be run again.

## Slice C relationship-list repair gate

目前 relationship list 的「牧區」與「區別」欄位同時空白，但唯讀 probe 也發現
source contact marker 與 expected relationship race-leader provenance 尚未成立。因此
不得執行下方 `-RepairFixture`；必須先修正這兩個 task-owned provenance 條件，再重跑
`-RepairProbe`。

```powershell
$root = 'D:\音訊科技產品\系統平台\SpeechMessageProducts\.worktrees\1.0.0.3.Gateway&Embedded.Worktree'
powershell.exe -NoProfile -ExecutionPolicy Bypass -File `
  "$root\docs\scripts\Invoke-Package02Data8ListManagementEvidence.ps1" `
  -RepositoryPath $root `
  -RepairFixture `
  -Json
```

只有新的 probe 顯示兩個 provenance 條件成立後，才可由授權的下一個操作執行一次
`-RepairFixture`；成功且 `readBackConfirmed=true` 後，才可執行一次既有的
`-ReconcileFixture -Json`。任何 `safeToRetry=false`、`repair-ambiguous`、
`repair-readback-mismatch` 或 `cleanup-failure` 都不得重試。所有 lane 都只接受一行
sanitized JSON，不會輸出 GUID、密碼、token、cookie、端點或原始例外。
