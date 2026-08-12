# P7.2 後續設計：安全雙軌與候選版邊界

## 設計原則

本任務不把「可寫入 CE 測試環境」視為可以跳過證據。寫入授權只允許 task-owned fresh fixture 的受控 mutation；可交付性仍取決於固定 capability、精確 read-back、reconciliation、deterministic cleanup 與跨使用者隔離。

軌道 A 與 B 可交錯處理，但只有本機工作可以並行；任何 CE cycle 皆串行，且每一個 cycle 使用獨立 nonce、ledger、fixture graph 與 cleanup owner。

## 軌道 A：Slice C 根因與 CE 證據

`DownloadListManager` 目前以 factory 取得共用 ToolUtility，且 `GetListManager` 可能把呼叫端提供的 `IOrganizationService` 寫入該共用物件。修正後，service 必須以操作範圍的顯式參數傳遞給實際工作；class、factory 與 ToolUtility 不得保留它。誰建立 service／lease，誰在同一 bounded flow 中釋放或歸還它。

例外路徑使用 `throw;` 保留原始 stack trace。child process 只能將 operation、runtime、cleanup、evidence 等有限 enum 類別回傳給 parent；parent 以固定欄位表示 no-go、child failure 或 unavailable，永遠不輸出原始 CRM exception。

新的 CE cycle 的唯一合法流程為：

```text
bootstrap → read-only preflight → provision → ExecuteFixture（一次）
          → exact read-back / reconcile → exact cleanup
```

任何步驟的 ambiguous／timeout／mismatch／cleanup uncertainty 都結束整個寫入家族。週報是唯讀前置資料：zero-active 為「建立不關聯週報的出席紀錄」分支，exactly-one-active 為精確關聯分支，其他狀態一律 no-go。

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
