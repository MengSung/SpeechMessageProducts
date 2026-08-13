# P7.2 受控定期奉獻付款回傳寫入家族

## 目標與使用者價值

建立一個全新的 P7.2「定期奉獻付款回傳」受控寫入家族。此家族必須把既有
`RecurringDonationPaymentProcessor.HandlePaymentReturn` 中混雜的付款去重、費用建立、
費用負責人指派與認獻預約完成，拆成可驗證的 server-owned 契約；先交付完整的本機
設計、local-only admission 與測試，然後僅在全新的測試 fixture cycle 的唯讀前置檢查為
`go` 時，才允許一次受控 CE 9.1 寫入。

這項工作不會重播歷史 Slice C，也不會藉由「read-new/write-legacy」或雙寫來製造看似可用的
結果。它的價值是讓未來的付款回傳具備單一 writer、可證明 idempotency、精確 read-back、
reconciliation 與 deterministic cleanup，而非只將 CRM SDK 呼叫換一個位置。

## 已確認事實

- 歷史 P7.2 Slice C cycle 已是 `write-not-committed` no-go，並已完成 exact cleanup；其
  nonce、ledger、fixture、descriptor、CE evidence 與任何輸入都永久不可重用或重播。
- 現行 legacy payment-return flow 會讀取 booking、查詢已處理的 `001` fee、更新 contact card
  profile、建立 fee、Assign fee owner、更新 booking completion/status，最後觸發通知。它缺少
  可獨立證明的 transaction、idempotency、read-back、reconciliation、rollback 與 cleanup owner。
- `fees.retrieve.by.dedication.period` 已有 typed read 基礎，但它不代表 fee create、owner assign 或
  booking update 已被遷移；`ORG-CALL-00064` 仍是 temporary-legacy。
- 已有的 `P72DonationPaymentLocalDecision` 與 `P72DonationPaymentLocalPlanBuilder` 是 local-only
  pure contract，且 `CeDispatchAllowed=false`、`ProductConsumerAllowed=false`；它們不是 CE evidence。
- 已有 `P72FreshSliceCFixture*` 基礎設施只屬於歷史 list-management family，不能複用其 nonce、
  ledger、descriptor、fixture、環境變數或任何 CRM baseline。
- 測試 CE 9.1 可執行 Create、Update、Assign、Delete、Associate、Disassociate，但只限本 task
  的 fresh fixture、明確 allowlist 與本次 ledger known keys。不得掃描或猜選 CRM 使用者／Owner。

## 範圍

1. 以一個獨立、可逆的「付款成功後 fee update」受控寫入 slice 作為此 family 的第一個 writer
   vertical slice；它先建立本機 cycle admission、ledger state、read-back/reconciliation/cleanup
   契約與測試。
2. 將 fee create、fee owner assign、booking completion 與 notification 明確建模為下一層依賴，
   不能把它們隱藏在第一個 slice 的 generic CRUD 或 partial plan 中。
3. 為第一個 slice 設計新的 task-owned fresh fixture descriptor、nonce、ledger、marker、
   mutation allowlist、唯讀 preflight、一次 dispatch、exact read-back、reconcile 及 cleanup。
4. 僅在本機品質閘門與新 cycle preflight 都為 `go` 時，才實作或執行一次對測試 CE 的 controlled
   dispatch；任何 `no-go`、timeout、ambiguous、read-back mismatch 或 cleanup uncertainty 都是
   該寫入 family 的 terminal no-replay 狀態。

## 不在範圍

- 重跑、修補、讀取或復用歷史 Slice C cycle。
- 修改既有付款 production records、共享資料、週報、舊 fixture、CE 8.2、Official Worker、feature
  flag、ChurchReport traffic、P7.5 ToolUtility removal 或 P8 deployment。
- 接受 caller supplied CRM ID、Owner、endpoint、credential、profile 或 connector selection 作為
  authority；不得以 CRM 使用者掃描或自動選擇補足 Owner。
- 實作 request-time fallback、dual-write、generic CRM proxy、`Entity`／`EntityCollection` bridge、
  無界 cache、Session／HttpContext retained state 或 retry。

## 必要安全與隔離條件

- 每個 request／cycle 只持有 immutable、bounded、去識別化的 local plan 與本次 task-owned ledger。
  任何 connector、lease、stream、file handle、cancellation registration、temporary directory 或 process
  都必須由明確 owner 在 finally／dispose 路徑釋放。
- writer 必須 server-authorized；fixture descriptor 的 exact IDs 由本 task ledger 記錄並於每一階段
  read-back 驗證。cleanup 只能反向操作 ledger 已知 keys，未知資料一律不猜測、不刪除。
- preflight 必須是零 mutation，且只輸出固定、去識別化分類。`go` 之外的結果絕不 provision 或 dispatch。
- CE 寫入成功不是完成條件；必須有 exact read-back、reconciliation、cleanup／rollback evidence。

## 驗收條件

- [ ] 本 task 有完整 `prd.md`、`design.md`、`implement.md`、context manifests、CCG task record 與
      45 秒上限的雙模型分析紀錄；quota／timeout 時正確標記「雙模型未完成」。
- [ ] 第一個 payment-return writer slice 有明確 typed operation contract、server-owned authorization
      boundary、immutable input model、idempotency state、read-back policy、reconciliation state、
      reverse-known-key cleanup order 與 rollback owner；沒有 generic write 或 legacy fallback。
- [ ] local-only cycle admission 在 fixture descriptor、nonce、ledger binding、allowlist、preflight、
      dispatch count、read-back 或 cleanup 任一條件不完整時 fail closed，且不允許 retry。
- [ ] 有 RED/GREEN unit tests 覆蓋 fresh success、already applied、pending／unknown、timeout／ambiguous、
      partial state、malformed descriptor、重播、A/B interleaving、read-back mismatch 與 cleanup uncertainty。
- [ ] 若 new fresh CE cycle 真正執行，證據必須包含 `bootstrap → preflight=go → provision=go → one
      dispatch → exact read-back/reconcile → deterministic cleanup`；任一失敗立即終止該 family，絕不重試。
- [ ] 每個實作 slice 通過相稱 targeted tests；在 child 邊界通過 Release build、encoding／CRLF、
      `git diff --check`、scope check、cross-user isolation 檢查與 CCG review。

## 尚待本機審計的問題

無需使用者手動設定才能回答的問題，都必須先透過 repository、既有測試、task history 與 deployment-
owned configuration 審計。只有外部條件（例如 test CE 缺少本 task 所需的固定 descriptor／權限）確實
無法由程式安全證明時，才記錄為去識別化 no-go，而不猜測或擴大 mutation 範圍。
