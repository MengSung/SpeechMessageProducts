# P7.2 後續設計：安全雙軌與候選版邊界

## 設計原則

本任務不把「可寫入 CE 測試環境」視為可以跳過證據。寫入授權只允許 task-owned fresh fixture 的受控 mutation；可交付性仍取決於固定 capability、精確 read-back、reconciliation、deterministic cleanup 與跨使用者隔離。

軌道 A 與 B 可交錯處理，但只有本機工作可以並行；任何 CE cycle 皆串行，且每一個 cycle 使用獨立 nonce、ledger、fixture graph 與 cleanup owner。

## 軌道 A：Slice C 根因與 CE 證據

`DownloadListManager` 目前以 factory 取得共用 ToolUtility，且 `GetListManager` 可能把呼叫端提供的 `IOrganizationService` 寫入該共用物件。修正後，service 必須以操作範圍的顯式參數傳遞給實際工作；class、factory 與 ToolUtility 不得保留它。誰建立 service／lease，誰在同一 bounded flow 中釋放或歸還它。

### DownloadIntegrateData 的既知隔離缺口與修正邊界

唯讀追蹤確認 `InMemoryDataContextSmallGroup` 以 Session ID 快取 `ListManager` 最長三十分鐘；該
`ListManager` 建構後持有同一個 `DownloadIntegrateData`。現況又由後者的 Factory 欄位取得
process-wide `ToolUtilityClass`，而標頭、成員、IdentityConverter、圖表及 follow-up 寫入路徑會直接
讀取其 CRM service 欄位。因此，若把借用 service 寫回這條鏈，後續 request、profile 或同一 Session
的交錯操作可能取得前次 operation 的可變連線狀態，這是 P7.4/P7.5 的 release blocker。

安全修正的公開契約分成兩層。`DownloadIntegrateData.SetupIntegrateData(..., IOrganizationService, ref ...)`
使用完整的明確輸入，將借用 service 限制於同步呼叫並依序傳到已完成驗證的唯讀 helper；它不得放入
instance field、static、`AsyncLocal`、cache、Factory 或 ToolUtility，也不能由內層 Dispose。
`ListManager.SetupIntegrateData(string, IOrganizationService)` 雖保留二進位相容性，卻因 `ListManager`
本身是 session-cache 物件、其餘輸入仍會來自可變 instance fields，故固定在讀取那些欄位或 CRM I/O
之前 fail closed。未來若要接入產品，必須建立完整、不可變且由伺服器驗證的 operation context；不得以
此過渡 overload 偷渡 session state。舊的一參數 overload 也不得被用作 service-aware 安全路徑證據。

這項修正必須涵蓋 header/login lookup、list/weekly-report 讀取、list member lookup、batch contact
retrieval、IdentityConverter/metadata、all-group/chart fetch、identity/follow-up 寫入；只改其中一個 list
query 並不足以解除 blocker。service-aware flow 如遇 null、fault、timeout、cancellation 或不確定 transport
狀態，必須 fail closed，由外層 lease owner 依既有 pool 規則淘汰／歸還，內層不能 retry 或把它轉交給下一個
操作。

驗證使用 A/B 可辨識 fake service 交錯執行、受控例外後 B 操作、反射欄位巡檢以及 Dispose sentinel：每個
service 僅收到自身 SDK 呼叫，長生命 `ListManager`／`DownloadIntegrateData`／共用 ToolUtility 都不保留它，
且內層從不 Dispose caller 借用的 service。另需明確決定同一 Session 的併發語意；在沒有序列化或
request-local report state 證明前，不將同一 cached `ListManager` 的平行呼叫宣稱為安全。

例外路徑使用 `throw;` 保留原始 stack trace。child process 只能將 operation、runtime、cleanup、evidence 等有限 enum 類別回傳給 parent；parent 以固定欄位表示 no-go、child failure 或 unavailable，永遠不輸出原始 CRM exception。

新的 CE cycle 的唯一合法流程為：

```text
bootstrap → read-only preflight → provision → ExecuteFixture（一次）
          → exact read-back / reconcile → exact cleanup
```

任何步驟的 ambiguous／timeout／mismatch／cleanup uncertainty 都結束整個寫入家族。週報是唯讀前置資料：zero-active 為「建立不關聯週報的出席紀錄」分支，exactly-one-active 為精確關聯分支，其他狀態一律 no-go。

### 2026-08-12 新 fresh cycle 終態

在本機品質閘門後，新的 current-user fresh cycle 取得 `go` 的 read-only preflight：固定 Data8
deployment 身分、啟用中且不同於 Data8 的 systemuser owner，以及 `zero-active` 週報分類均已證明。
隨後 provision 以新 nonce／ledger 成功建立 task-owned graph；唯一一次 ExecuteFixture 的前兩個
operation 已 read-back 並 restored，第三個 `listmanagement.smallgroup.update.fields` 回報
`write-not-committed`／baseline，因此 child 發布 `live-evidence-incomplete` no-go。依 no-retry
規則，不再送出 owner 或 transfer 操作，也不建立第二次 cycle。fresh cleanup 已以 strict ledger 的
exact ID 完成並回報 `fresh-fixture-cleaned`；沒有變更週報、feature flag、流量、CE 8.2 或 Official Worker。

## 軌道 B：D–H capability 邊界

| Slice | coverage matrix family | 本機 capability 邊界 | CE 前置證據 |
| --- | --- | --- | --- |
| D | donation lifecycle | 固定付款／奉獻／聯絡人 operation allowlist；金額、狀態與 owner 不接受 caller 任意欄位 | 隔離 financial fixture graph、baseline、reconcile、cleanup |
| E | appointments | 一筆 task-owned appointment 的固定欄位 create/update；不得接受 caller owner | appointment fixture、owner baseline、exact delete/restore |
| F | contact onboarding | 固定 contact graph 的全新建立；各子記錄有 ledger ID | graph read-back、known-ID reverse cleanup |
| G | fee lessons | 固定費用與 stor-lesson 狀態轉換；金額／階段值為 allowlist | monetary/status baseline、reconcile、restore |
| H | attendance | attendance key 驗證、週報解析與 present record create/upsert | present-record baseline、weekly-report 分類、exact delete/restore |

每個 capability 的 request 都是不可變 DTO，必須由 server 端 contract 決定 entity、attribute、owner、profile、connector 與 cleanup owner。timeout 後不得重播 mutation；只有當 read-back 可證明是 baseline 或已知 expected state 時才可進行既定 cleanup。

## 發佈與回滾

第一版 Release candidate 是本機產物，不會啟用 P7.4 流量或 P7.5 移除。每一個 CE 未證實的 capability 在 runtime registry／consumer 端維持 fail closed。若日後 CE 證據完成，P7.4 仍須逐 capability 切流，P7.5 仍須在完整 migration 與 rollback drill 後才可移除 ToolUtility。

## 驗證策略

- TDD：每個新行為先建立最小失敗測試，確認紅燈，再寫最小實作。
- 隔離：以不同 operation-local service／lease／fixture marker 的雙實例測試，證明沒有共享 mutable state、identity 或 exception state。
- 生命週期：針對 success、exception、timeout／cancellation 與 partial completion 驗證 dispose／cleanup 只執行一次且不跨 operation。
- CE：只在本機契約、targeted tests 與 Release build 全綠後，以一個 fresh cycle 取得受控證據；證據不完整即停止該軌道。
