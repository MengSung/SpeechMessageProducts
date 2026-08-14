# P7 post-runtime-health current matrix reconciliation 設計

## Boundary and data flow

固定的封存 `build_rebaseline.py` 讀取 canonical Phase-0 matrix、coverage、OperationIds、
registry、Data8 executor、ProductClient source、local-only catalog 與 ChurchReport production
source，建立新的 task-owned `authoritative-gap-matrix.json`。同一程式再讀該輸出驗證 schema、hash、
70-row identity、finite-state consistency 與去識別化限制；不讀取環境變數、秘密、credential 或網路。

## Compatibility and state rules

- 封存目錄完全唯讀；新快照不能覆寫或修改歷史 matrix／Slice C evidence。
- `productClient` 狀態可由目前 source 進展為 `implemented`，但 `consumer`、CE 8.2／9.1、Embedded、
  Dedicated、rollout、rollback、temporary-legacy 與 P7.5 blocker 必須各自維持實際狀態。
- 此 reconciliation 不會建立 ProductClient、executor、consumer、fixture、ledger、feature flag 或
  deployment profile；它只產出 repository-side evidence。
- 產出資料僅可含固定分類、operation kind、去識別化 call-site key 與 bounded counts；不可含 CRM
  identity、名稱、endpoint、token、cookie、credential、原始路徑或例外。

## Failure and rollback

任何 source hash／row count／schema mismatch、malformed UTF-8、未知狀態或 analyzer 失敗都停止並輸出
no-go；不得手工修正 matrix、猜測狀態或採用舊快照。回復方式是刪除本次 task-owned 未提交輸出，
不觸碰封存證據與產品資料。
