# P7 尚餘能力重新基準化

## 目標與使用者價值

本 child 是既有 `08-05-gateway-purpose-and-positioning` 的第一個尚餘工作交付。它以目前程式碼、設定與已封存的 P7.0、P7.1、P7.2 證據重新建立可機讀、可稽核的 70-row capability gap matrix，讓後續 child 能安全地完成 ChurchReport 的 Gateway 化、逐步移除產品端 ToolUtility／CRM SDK 依賴，並在 P7.5 全數通過後才進入 P8 Central Gateway。

這個 child 的目標是取得正確的後續工作基準；它不重做已封存的實作，不將 local-only contract 誤報為 CE 成功，也不執行任何 CE mutation、feature-gate 或流量切換。

## 已確認事實

- P3 Data8 generation-owned connector pool、P4 Embedded、P5 Dedicated Gateway、P6 Router／Pool／Lease 已完成；Official Worker live compatibility 維持 `evidence-pending`，不阻擋 Data8-first 路線。
- P7.0 已封存 70 個 normalized ChurchReport Dynamics call site、coverage validator、ToolUtility／CRM SDK reference baseline。
- P7.1 僅完成六個 Package01 typed Data8 read operation，且 CE 9.1 唯讀證據為 `go`；其 ChurchReport feature gate 維持關閉。
- P7.2 已完成本機候選版。Slice C 的最後一次 fresh CE cycle 為 `write-not-committed` no-go 且 strict cleanup 已完成；Slice D–H 只具 local-only reducer／plan contract，executor 與 consumer 均未啟用。
- 目前 registry、Data8 executor 與 ProductClient 已另有部分 Package02 implementation；其是否已被 ChurchReport 消費、是否有 CE 8.2／9.1、Embedded／Dedicated evidence，必須逐 row 證明，不能由型別或 unit test 推論。

## 範圍

1. 讀取封存 P7.0 matrix、P7.1／P7.2 evidence、現行 registry、Data8 executor、ProductClient、ChurchReport production call site 與 ToolUtility／CRM SDK reference baseline。
2. 建立 deterministic、machine-readable `authoritative-gap-matrix.json`，每一個原始 call site 保留下列彼此獨立的狀態：
   - Registry declared；Data8 executor implemented；typed ProductClient implemented；ChurchReport consumer migrated。
   - CE 8.2／CE 9.1 evidence；Embedded／Dedicated evidence；rollout／rollback owner；temporary legacy；P7.3 resource requirement；P7.5 removal blocker。
3. 建立可重複執行的 validator 與 focused tests，防止將 local-only、disabled feature gate、未驗證 execution 或 historical CE no-go 誤列為完成。
4. 依矩陣把剩餘 P7.1 read、P7.2 write／action／function、P7.3 special resources、P7.4 cutover、P7.5 removal 拆成可獨立驗收的後續 child；P8 只可在 P7.5 immutable handoff 後建立。
5. 更新 parent 的 PRD、design、implement、roadmap 與 metadata，使它反映 P6、P7.0、P7.1、P7.2 的封存事實及目前下一步。

## 不在本 child 範圍

- 不重新開啟、修改或重試已封存的 P4、P5、P6、P7.0、P7.1、P7.2 task 或其歷史 CE cycle。
- 不執行 Create、Update、Assign、Delete、Associate、Disassociate、feature flag、ChurchReport 流量、CE 8.2、Official Worker 或雲端部署操作。
- 不輸出 endpoint、帳號、CRM ID、名稱、credential、token、cookie、原始 CRM payload 或原始例外。
- 不以 matrix child 的本機結果宣稱已完成 P7.4、P7.5 或 P8。

## 安全與不可變規則

- Registry、executor、ProductClient、consumer、實機 CE evidence 與 rollout evidence 均為獨立欄位；任一欄位不得推導另一欄位成功。
- 未有明確 CE evidence 的 row 必須標示 `evidence-pending`、`unsupported` 或 `not-executed`；Slice C 的舊 `write-not-committed` no-go 維持 closed。
- Caller 不能選擇 credential、endpoint、connector kind、organization、profile 或 owner；後續 capability 只能使用 server-owned operation ID 與 deployment-owned immutable profile。
- 所有資料與診斷必須去識別化、有界、UTF-8 無 BOM、CRLF、最終 CRLF；不保留 session、credential 或跨使用者／跨 profile mutable state。

## 驗收條件

- [x] 70 個來源 call site 全數在新 matrix 中恰好一列，且 source matrix checksum／count 可驗證。
- [x] 每列具備 registry、executor、ProductClient、consumer、CE 8.2／9.1、Embedded／Dedicated、rollout、rollback、temporary legacy、P7.3、P7.5 欄位，且值受 schema／validator 約束。
- [x] Validator 以目前程式碼與封存證據正確辨識已完成 P7.1 六項 read、P7.2 local-only D–H、已完成 Package02 implementation，以及未遷移的 ChurchReport／ToolUtility 依賴。
- [x] 新 matrix 不將 disabled feature gate、local-only plan、歷史 CE no-go、registry declaration 或 unit-test pass 報成 CE evidence 或 consumer migration。
- [x] Parent 文件與 metadata 已以本次 evidence 更新，且明確將下一步指向本 child 的 gap-matrix validation。
- [x] 相關 targeted tests、Python／JSON parser、UTF-8 無 BOM／CRLF／final CRLF、`git diff --check` 與 scope check 通過；雙模型分析結果或降級原因已持久化紀錄。詳見 `final-quality-gate-2026-08-12.md`。
