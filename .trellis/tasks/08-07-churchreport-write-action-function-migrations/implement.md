# P7.2 實作計畫

## 前置基線

- [x] P6.1、P7.0、P7.1 已完成並封存；P6.2 Official Worker 仍是獨立 `evidence-pending` 支線。
- [x] P7.0 matrix、P7.2 activation input 與 parent `p7.2-write-environment-readiness.md` 已讀取。
- [x] 24 個 P7.2 candidates 已按交易與回退責任拆成 8 個切片。
- [x] 讀取本 task 涵蓋 package 的 `.trellis/spec/` index 與 required guideline docs，再開始修改程式。
- [x] 以 `git status --short` 建立 scoped baseline；本輪開始時工作區乾淨，未混入其他 task 變更。

## Phase 1：Activation material

- [x] 建立 `prd.md`、`design.md`、本計畫及 `p7.2-fixture-activation-matrix.json`。
- [x] 驗證 matrix JSON、source matrix SHA-256、UTF-8 no-BOM、CRLF-only、final CRLF 與 `git diff --check`。
- [x] 以 task-local `implement.jsonl` 記錄 matrix、P7.1 contract、Data8／ProductClient／ChurchReport source 與 relevant specs；不得把 credential、endpoint、Organization ID 或 fixture ID 寫進 jsonl。
- [x] 依連續 Goal 授權執行 `task.py start`；目前 task status 為 `in_progress`，只允許本機測試與實作，尚不允許 CE write，直到 slice A bridge preflight 變為 go。

## Phase 2：Slice A — Contact basic-info（TDD）

1. 在 `SpeechMessage.Dynamics.Abstractions` 新增封閉 request／result／response discriminator 與固定 registry definition；測試先證明未知欄位、unknown operation、CE 8.2、wrong connector、missing idempotency key 與無變更均 fail closed。
2. 在 `SpeechMessage.Dynamics.Connectors.Data8` 新增唯一的 exact contact-update template 與 read-back projection；不得讓 `Entity`、FetchXML 或 query／field map 離開 connector-internal scope。每條 `AcquireAsync` 路徑用 `await using` 確定釋放 lease。
3. 在 `SpeechMessage.Dynamics.ProductClient` 新增具名 typed client；建立 Gateway／Embedded contract parity tests，驗證 profile、CE version、connector 均是 deployment-owned，沒有 request-time switch。
4. 在 ChurchReport 僅新增尚未啟用的 adapter／composition support，維持 feature gate false 與現有使用者授權語意。P7.4 才會切換 consumer。
5. 每個檔案完成即跑最小相關測試；修正任何 compilation、test、lifecycle、encoding 或 static-scan failure，最多三個不同假設的 self-repair cycles，同一根因連續兩次即停止並記錄。

## Phase 3：CE 9.1 fixture bridge 與證據

1. 先完成 repository 內 contract／unit tests，再建立 Windows PowerShell 5.1-compatible bridge。它必須 bounded、fail-closed，只允許 `crm91`／Data8／P7.2 marker contact／兩個字串欄位，且只輸出去識別化 JSON。已完成 preflight bridge、明確 `-ExecuteFixture` opt-in runner、固定 Credential Manager 讀取、bounded child process、strict TRX parser 與 20 項 PowerShell 契約測試；2026-08-08 preflight 已回傳 `outcome=go`、`operationExecuted=false`、`featureFlagChanged=false`。
2. 受控 live-evidence slice 依序執行 baseline read → sentinel update → read-back reconciliation → baseline restore → restore read-back；任何 ambiguous timeout、owner mismatch、profile mismatch 或 cleanup 失敗都停在 no-go，絕不自動重送不確定 write。使用者於 2026-08-08 明確授權 `sunnyvalechback` 全資料庫作為可新增／修改／刪除的虛構研發資料。Slice A 已任選既有 contact 並完成真實 CE 9.1 Data8 flow：`outcome=go`、`sentinelState=confirmed`、`cleanupState=restored`、`featureFlagChanged=false`。
3. 只有 preflight 與後續 live bridge 都回傳 sanitized `outcome=go` 才可寫入 task evidence；不得貼入密碼、token、cookie、endpoint、Organization ID、contact GUID、原始 baseline 或例外內容。
4. 如需要使用者在 Lenovo 執行 bridge，直接提供可 copy/paste 的完整命令與預期 JSON；不得重新要求 P6 credential profile 或手動輸入可由 bridge 建立的資料。

## Phase 4：其餘 P7.2 slices

依序完成 B 到 H，每個切片都重複以下 gate，且不得共享不相容的 fixture／rollback owner：

- Slice B 先依 repository 證據拆成 B1 LINE profile、B2 ungrouped commitment aggregate 與 B3 image media handoff。B1／B2 在 P7.2 以 TDD 完成 typed contract、Data8 executor、ProductClient、未啟用 ChurchReport composition 與 bounded CE 9.1 evidence；B3 的兩個 5 MiB image row 只完成 P7.3 可執行 handoff，P7.3 bounded media contract 完成前不得放入 JSON／canonical envelope、不得取得 lease、不得宣稱 live evidence。
- B1 的 LINE token、profile fetch 與 URL probe 留在 ChurchReport；Gateway 只接受三個固定欄位的 set／clear／preserve scalar。B2 由 connector 依 server-owned 小組規則建立 aggregate，不接受任意 FetchXML、QueryExpression 或無界 grouped-ID array。
- [x] 2026-08-08 已完成 Slice B1／B2：typed DTO、registry、Data8 executor、ProductClient、未啟用 ChurchReport composition、fixture bridge、Windows PowerShell 5.1 handoff 與 lifecycle tests 均已建立。真實 sunnyvalechback CE 9.1 Data8 evidence 為 `outcome=go`；B1 的 sentinel read-back 已確認且 baseline 已復原，B2 與 legacy 的四組 raw OptionSet value/count 結果一致。兩者均未變更 feature flag；evidence 保存於 `p7.2-slice-b-live-evidence.json`。
- [x] Slice B2 的 parity 修正已在 server-owned aggregate template 明確排除 null `customertypecode`，避免 CE 回傳沒有 `commitmenttype` alias 的 null group；live runner 在 TRX 無法穩定保存 stdout 時改用嚴格 OS-temp evidence file，並由 handoff 在 `finally` 清除。兩項可重複契約已寫入 backend Gateway spec。

- machine-readable matrix row 先從 `fixture-pending` 變為 `required-for-activation`，並寫明 exact operation IDs、fixture graph、allowed mutation、idempotency、timeout policy、cleanup／reconciliation、CE support、rollout／rollback owner。
- 先補 unit／fault／lifecycle tests，再補 registry、Data8 template、ProductClient 和未啟用 ChurchReport adapter。
- 取得一次 bounded real CE 9.1 evidence，完成 cleanup 和 sanitized evidence，才可將 row 標成 `evidence-complete`。
- CE 8.2／Official Worker／缺 fixture 的 row 保持 fail closed；不將 mock 當作 real CE evidence。

## Phase 5：P7.2 收尾

- [ ] coverage validator 對全部 P7.2 matrix-required row 成功，沒有 `required` row 缺 DTO、registry、executor、consumer、fixture、evidence、rollback owner 或 lifecycle owner。
- [x] 執行 focused tests、完整 `SpeechMessage.Dynamics.Tests`、ChurchReport tests 與 Release build；resource stress／soak、drain／dispose 與 rollback drill 仍待 live fixture／最後切片。
- [x] 執行 `git diff --check`、byte-level UTF-8 no-BOM／CRLF-only／final CRLF check，並確認目前變更只含 P7.2-owned files。
- [ ] 執行 Trellis check、spec update judgment、task-owned staged commit 與 archive；不得 push、PR、啟動 P8 或啟用 ChurchReport 流量。
- [ ] 封存後自動建立／規劃 P7.3，不需額外 PROCEED。
