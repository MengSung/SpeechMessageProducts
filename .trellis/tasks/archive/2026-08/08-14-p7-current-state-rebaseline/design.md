# P7 現況重新基準化設計

## 邊界與資料流

本 child 是離線、固定輸入的分析工作。它以 immutable Phase-0 70-row identity 作為唯一 call-site 基線，透過既有封存 analyzer 讀取 allowlisted repository source，輸出 task-owned JSON matrix。wrapper 不接觸 CRM、網路、Windows Credential Manager、瀏覽器、產品程序或 runtime profile，也不保存任何使用者、租戶、憑證或 CRM 資料。

```text
immutable phase-0 matrix + archived evidence + current source
    -> offline analyzer (read-only)
    -> task-owned authoritative-gap-matrix.json
    -> validator + fixed-category summary
    -> parent documentation checkpoint
    -> independently governed next P7 child
```

## 證據模型

每個 row 的 registry、Data8 executor、ProductClient、ChurchReport consumer、CE 8.2／9.1、Embedded／Dedicated、rollout／rollback、temporary legacy、P7.3 resource 與 P7.5 blocker 是相互獨立的 finite-state 欄位。靜態 source presence 只可作為 implementation evidence；local-only、feature gate=false、unit test 綠燈與歷史 CE cleanup 都不能轉換成 consumer、CE、host、traffic 或 P7.5 evidence。

P7.2 Slice C 的 `no-go-closed` 是 immutable historical classification。新寫入 family 若日後存在，必須以另建 child、新 nonce、ledger、fresh task-owned fixture、preflight、single dispatch、read-back、reconcile 與 cleanup 證明；本 child 不會建立任何這類資料。

## 可重複性與資源生命週期

task-owned PowerShell wrapper 僅啟動一次短命的 Python analyzer process，將輸出限制在本 child 目錄，並立即傳回 exit code。沒有 background process、cache、timer、subscription、socket、session、profile 或 credential state；驗證失敗時不保留部分 runtime state。輸出 JSON 固定排序、UTF-8 無 BOM、CRLF 與 final CRLF，錯誤輸出只允許固定 validator categories。

## Parent 校正原則

parent 僅修正可由 matrix、P7.5 prerequisite report 與封存 child 直接證明的現況：已封存基線、P7.4 local-only progress、P7.5 deterministic no-go、P8 predecessor gate、下一步 safe-candidate eligibility。它不會把本次 matrix 的結果升格為 consumer migration、CE 寫入、feature enablement、ToolUtility removal 或 Central Gateway deployment。

## 回復策略

本 child 的唯一可寫 artifact 是 task-owned matrix、summary、tests、wrapper、task records 與 parent 文件。若驗證失敗，刪除或回復本 child 和 parent documentation commit 即可；不需 CRM cleanup，因為完全沒有 CRM mutation。任何無法從 repository 判斷的外部 deployment 條件仍維持 P8 no-go，僅產生去識別化 handoff。
