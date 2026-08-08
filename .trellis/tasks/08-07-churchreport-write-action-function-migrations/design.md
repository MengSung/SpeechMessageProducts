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

## 相容性、啟用與回退

- CE 9.1 Data8 是初始唯一 required support。CE 8.2 及 Official Worker 都是 unsupported／not-selected，不得嘗試 fallback。
- P7.2 不打開 ChurchReport feature gate。P7.4 才能在 Dedicated Gateway listener preflight 完成後，逐 capability 啟用並觀測本機產品流量。
- 若 contract、fixture、read-back、cleanup、authorization、profile generation 或 lifecycle check 失敗，該 capability 回到 registry fail-closed 狀態；既有 ToolUtility route 只可維持至 P7.5 的正式 removal gate，不能在 P7.2 形成雙寫。
- 任何有資料、授權、錯誤語意、p95 latency、resource baseline 或 rollback regression 的切片，僅回退該 capability，保留其他已證明切片的 artifacts。

## 測試策略

1. 先寫 contract tests：未知 operation、錯誤 profile／connector／CE、未授權 contact、空 update、超長字串、未知欄位、錯配 response、取消、timeout 與 duplicate idempotency key 都必須 fail closed。
2. 再寫 Data8 executor tests：驗證 request 在 await 前複製有限 scalar、僅允許新 registry operation、每條成功／失敗／取消路徑歸還 lease，且不保留 request／credential／profile references。
3. 寫 ProductClient／Gateway／Embedded parity tests：兩條 Lenovo route 使用同一 typed request/result，沒有 request-time connector/profile/CE switch。
4. 寫 fixture bridge tests：baseline、committed、not-committed、ambiguous、cleanup-committed、cleanup-ambiguous 六種狀態皆有決定性 outcome，且 JSON 無 secret、GUID、endpoint、raw exception 或 PII。
5. 真實 CE evidence 僅在 bridge 和本機測試全綠後執行一次 bounded flow；接著做本機 stress／soak／drain checks，證明 lease、permit、client、buffer 與 task return to baseline。
